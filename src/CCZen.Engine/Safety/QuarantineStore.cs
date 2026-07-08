using System.Runtime.Versioning;
using System.Text.Json;

namespace CCZen.Engine.Safety;

/// <summary>
/// Same-volume quarantine with write-ahead journal and batch restore
/// (spec: SAFE-FR-012, SAFE-FR-020, SAFE-FR-025..026). Moves are same-volume
/// renames (O(1) metadata operations); every move is journaled before it
/// happens so a crash leaves no untracked state.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class QuarantineStore
{
    public const string DirectoryName = "CCZen.Quarantine";

    private readonly ProtectedPaths _protectedPaths;

    public QuarantineStore(ProtectedPaths protectedPaths)
    {
        _protectedPaths = protectedPaths;
    }

    /// <summary>
    /// Executes a confirmed batch plan: each item is re-verified (protection,
    /// fingerprint, reparse point) and moved into
    /// <c>&lt;volume&gt;\CCZen.Quarantine\&lt;batchId&gt;\</c> on its own volume.
    /// With <paramref name="permanentDelete"/> the item is deleted directly
    /// (explicit user opt-in only). <paramref name="progress"/> receives
    /// (done, total, currentPath) after each item.
    /// </summary>
    public IReadOnlyList<ItemResult> Execute(BatchPlan plan, bool permanentDelete = false, Action<int, int, string>? progress = null)
    {
        var results = new List<ItemResult>(plan.Items.Count);
        var journals = new Dictionary<string, WalJournal>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (PlanItem item in plan.Items)
            {
                results.Add(ExecuteItem(plan, item, journals, permanentDelete));
                progress?.Invoke(results.Count, plan.Items.Count, item.Path);
            }
        }
        finally
        {
            foreach (WalJournal journal in journals.Values)
            {
                journal.Dispose();
            }
        }

        return results;
    }

    private ItemResult ExecuteItem(BatchPlan plan, PlanItem item, Dictionary<string, WalJournal> journals, bool permanentDelete)
    {
        if (_protectedPaths.IsProtected(item.Path))
        {
            return new ItemResult(item.Path, ItemOutcome.SkippedProtected, null, "protected path veto (SAFE-FR-001)");
        }

        if (PathNormalizer.IsReparsePoint(item.Path))
        {
            return new ItemResult(item.Path, ItemOutcome.SkippedReparsePoint, null, "reparse point (SAFE-FR-004)");
        }

        if (!File.Exists(item.Path) && !Directory.Exists(item.Path))
        {
            return new ItemResult(item.Path, ItemOutcome.SkippedMissing, null, null);
        }

        if (!item.FingerprintMatches())
        {
            return new ItemResult(item.Path, ItemOutcome.SkippedFingerprintMismatch, null, "size/mtime changed since plan (SAFE-FR-011)");
        }

        if (!item.IsDirectory && OperatingSystem.IsWindowsVersionAtLeast(6, 0, 6000))
        {
            IReadOnlyList<string> holders = LockProbe.GetLockingProcesses([item.Path]);
            if (holders.Count > 0)
            {
                return new ItemResult(item.Path, ItemOutcome.SkippedLocked, null, $"in use by: {string.Join(", ", holders)} (SAFE-FR-021)");
            }
        }

        string? volumeRoot = Path.GetPathRoot(item.Path);
        if (volumeRoot is null)
        {
            return new ItemResult(item.Path, ItemOutcome.Failed, null, "no volume root");
        }

        string batchDirectory = Path.Combine(volumeRoot, DirectoryName, plan.BatchId);
        Directory.CreateDirectory(batchDirectory);
        string quarantinePath = Path.Combine(batchDirectory, Guid.NewGuid().ToString("N"));

        if (!journals.TryGetValue(volumeRoot, out WalJournal? journal))
        {
            journal = new WalJournal(Path.Combine(batchDirectory, "journal.jsonl"));
            journals[volumeRoot] = journal;
        }

        try
        {
            if (permanentDelete)
            {
                // WAL: the delete intent is journaled for the audit trail even
                // though a delete cannot be restored (SAFE-FR-050).
                journal.Write(new WalRecord("delete-intent", item.Path, string.Empty, item.IsDirectory));
                if (item.IsDirectory)
                {
                    Directory.Delete(item.Path, recursive: true);
                }
                else
                {
                    File.Delete(item.Path);
                }

                journal.Write(new WalRecord("delete-done", item.Path, string.Empty, item.IsDirectory));
                return new ItemResult(item.Path, ItemOutcome.Deleted, null, null);
            }

            // WAL: intent is durable before the move (SAFE-FR-012).
            journal.Write(new WalRecord("move-intent", item.Path, quarantinePath, item.IsDirectory));
            if (item.IsDirectory)
            {
                Directory.Move(item.Path, quarantinePath);
            }
            else
            {
                File.Move(item.Path, quarantinePath);
            }

            journal.Write(new WalRecord("move-done", item.Path, quarantinePath, item.IsDirectory));
            return new ItemResult(item.Path, ItemOutcome.Quarantined, quarantinePath, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            journal.Write(new WalRecord(permanentDelete ? "delete-failed" : "move-failed", item.Path, quarantinePath, item.IsDirectory));
            return new ItemResult(item.Path, ItemOutcome.Failed, null, ex.Message);
        }
    }

    /// <summary>
    /// Restores a batch by replaying the journal in reverse (SAFE-FR-026).
    /// When the original path is occupied the item is restored alongside it
    /// with a <c>.restored</c> suffix.
    /// </summary>
    public IReadOnlyList<ItemResult> Restore(string volumeRoot, string batchId)
    {
        string batchDirectory = Path.Combine(volumeRoot, DirectoryName, batchId);
        string journalPath = Path.Combine(batchDirectory, "journal.jsonl");
        if (!File.Exists(journalPath))
        {
            throw new FileNotFoundException("Quarantine journal not found.", journalPath);
        }

        var results = new List<ItemResult>();
        foreach (WalRecord record in WalJournal.Read(journalPath).Where(r => r.Kind == "move-done").Reverse())
        {
            if (!File.Exists(record.QuarantinePath) && !Directory.Exists(record.QuarantinePath))
            {
                results.Add(new ItemResult(record.OriginalPath, ItemOutcome.SkippedMissing, record.QuarantinePath, "quarantined copy missing"));
                continue;
            }

            string destination = record.OriginalPath;
            if (File.Exists(destination) || Directory.Exists(destination))
            {
                destination += ".restored";
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                if (record.IsDirectory)
                {
                    Directory.Move(record.QuarantinePath, destination);
                }
                else
                {
                    File.Move(record.QuarantinePath, destination);
                }

                results.Add(new ItemResult(destination, ItemOutcome.Quarantined, record.QuarantinePath, "restored"));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                results.Add(new ItemResult(record.OriginalPath, ItemOutcome.Failed, record.QuarantinePath, ex.Message));
            }
        }

        return results;
    }

    /// <summary>Permanently deletes one quarantined batch (explicit user action, SAFE-FR-025).</summary>
    public bool PurgeBatch(string volumeRoot, string batchId)
    {
        string batchDirectory = Path.Combine(volumeRoot, DirectoryName, batchId);
        if (!Directory.Exists(batchDirectory))
        {
            return false;
        }

        Directory.Delete(batchDirectory, recursive: true);
        return true;
    }

    /// <summary>SAFE-FR-025: permanently deletes quarantined batches older than the retention window.</summary>
    public int PurgeExpired(string volumeRoot, TimeSpan retention)
    {
        string quarantineRoot = Path.Combine(volumeRoot, DirectoryName);
        if (!Directory.Exists(quarantineRoot))
        {
            return 0;
        }

        int purged = 0;
        foreach (string batch in Directory.EnumerateDirectories(quarantineRoot))
        {
            if (Directory.GetLastWriteTimeUtc(batch) < DateTime.UtcNow - retention)
            {
                Directory.Delete(batch, recursive: true);
                purged++;
            }
        }

        return purged;
    }
}

/// <summary>One durable journal record (JSON Lines).</summary>
public sealed record WalRecord(string Kind, string OriginalPath, string QuarantinePath, bool IsDirectory);

/// <summary>Write-ahead journal: append + Flush(true) before every move (SAFE-FR-012).</summary>
internal sealed class WalJournal : IDisposable
{
    private readonly FileStream _stream;

    public WalJournal(string path)
    {
        _stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
    }

    public void Write(WalRecord record)
    {
        byte[] line = System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(record) + "\n");
        _stream.Write(line);
        _stream.Flush(flushToDisk: true);
    }

    public static IReadOnlyList<WalRecord> Read(string path)
    {
        var records = new List<WalRecord>();
        foreach (string line in File.ReadLines(path))
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                records.Add(JsonSerializer.Deserialize<WalRecord>(line)!);
            }
        }

        return records;
    }

    public void Dispose() => _stream.Dispose();
}
