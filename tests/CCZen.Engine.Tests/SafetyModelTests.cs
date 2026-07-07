using CCZen.Engine.Safety;

namespace CCZen.Engine.Tests;

/// <summary>
/// Safety-model tests (spec: 04 测试要求): protected-path penetration via
/// symlinks, fingerprint-mismatch skip, quarantine + restore round-trips,
/// restore conflict handling, and retention purge.
/// </summary>
public class SafetyModelTests : IDisposable
{
    private readonly string _root;
    private readonly string _protectedDir;
    private readonly ProtectedPaths _protection;
    private readonly QuarantineStore _store;

    public SafetyModelTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cczen-safety-" + Guid.NewGuid().ToString("N"));
        _protectedDir = Path.Combine(_root, "Protected");
        Directory.CreateDirectory(Path.Combine(_root, "work"));
        Directory.CreateDirectory(_protectedDir);
        _protection = new ProtectedPaths([_protectedDir], windir: null);
        _store = new QuarantineStore(_protection);
    }

    public void Dispose()
    {
        string quarantine = Path.Combine(Path.GetPathRoot(_root)!, QuarantineStore.DirectoryName);
        if (Directory.Exists(quarantine))
        {
            Directory.Delete(quarantine, recursive: true);
        }

        Directory.Delete(_root, recursive: true);
    }

    private string Write(string relative, byte[]? content = null)
    {
        string path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content ?? new byte[64]);
        return path;
    }

    [Fact]
    public void ProtectedPath_DirectAndDotDot_AreVetoed()
    {
        string file = Path.Combine(_protectedDir, "critical.dll");
        File.WriteAllBytes(file, new byte[10]);

        Assert.True(_protection.IsProtected(file));
        Assert.True(_protection.IsProtected(Path.Combine(_root, "work", "..", "Protected", "critical.dll")));
        Assert.False(_protection.IsProtected(Write(@"work\safe.tmp")));
    }

    [Fact]
    public void ProtectedPath_SymlinkPenetration_IsVetoed()
    {
        // 穿透测试：链接指向保护区内目录，命名伪装成 cache
        string file = Path.Combine(_protectedDir, "data.bin");
        File.WriteAllBytes(file, new byte[10]);
        string link = Path.Combine(_root, "work", "cache-link");
        try
        {
            Directory.CreateSymbolicLink(link, _protectedDir);
        }
        catch (IOException)
        {
            return; // symlink creation requires privilege; environment-dependent
        }

        Assert.True(_protection.IsProtected(Path.Combine(link, "data.bin")));
    }

    [Fact]
    public void VolumeRoot_IsAlwaysProtected()
    {
        Assert.True(_protection.IsProtected(Path.GetPathRoot(_root)!));
    }

    [Fact]
    public void Execute_QuarantinesFile_AndJournalRecordsIt()
    {
        string file = Write(@"work\junk.log");
        var plan = BatchPlan.Create([PlanItem.FromPath(file, "r1")!]);

        IReadOnlyList<ItemResult> results = _store.Execute(plan);

        ItemResult result = Assert.Single(results);
        Assert.Equal(ItemOutcome.Quarantined, result.Outcome);
        Assert.False(File.Exists(file));
        Assert.True(File.Exists(result.QuarantinePath));
        string journal = Path.Combine(Path.GetPathRoot(file)!, QuarantineStore.DirectoryName, plan.BatchId, "journal.jsonl");
        Assert.Contains("move-done", File.ReadAllText(journal));
    }

    [Fact]
    public void Execute_FingerprintMismatch_IsSkipped()
    {
        string file = Write(@"work\swap.tmp");
        PlanItem item = PlanItem.FromPath(file, "r1")!;
        File.WriteAllBytes(file, new byte[999]); // TOCTOU swap after plan snapshot

        IReadOnlyList<ItemResult> results = _store.Execute(BatchPlan.Create([item]));

        Assert.Equal(ItemOutcome.SkippedFingerprintMismatch, Assert.Single(results).Outcome);
        Assert.True(File.Exists(file));
    }

    [Fact]
    public void Execute_ProtectedItem_IsSkipped()
    {
        string file = Path.Combine(_protectedDir, "asset.docx");
        File.WriteAllBytes(file, new byte[10]);

        IReadOnlyList<ItemResult> results = _store.Execute(BatchPlan.Create([PlanItem.FromPath(file, "r1")!]));

        Assert.Equal(ItemOutcome.SkippedProtected, Assert.Single(results).Outcome);
        Assert.True(File.Exists(file));
    }

    [Fact]
    public void Restore_ReturnsFileToOriginalPath()
    {
        string file = Write(@"work\restore-me.log", [1, 2, 3]);
        var plan = BatchPlan.Create([PlanItem.FromPath(file, "r1")!]);
        _store.Execute(plan);

        IReadOnlyList<ItemResult> restored = _store.Restore(Path.GetPathRoot(file)!, plan.BatchId);

        Assert.Equal("restored", Assert.Single(restored).Detail);
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(file));
    }

    [Fact]
    public void Restore_Conflict_CreatesRestoredCopy()
    {
        string file = Write(@"work\conflict.log", [1]);
        var plan = BatchPlan.Create([PlanItem.FromPath(file, "r1")!]);
        _store.Execute(plan);
        File.WriteAllBytes(file, [9]); // new file appeared at the original path

        _store.Restore(Path.GetPathRoot(file)!, plan.BatchId);

        Assert.Equal(new byte[] { 9 }, File.ReadAllBytes(file));
        Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(file + ".restored"));
    }

    [Fact]
    public void Execute_Directory_QuarantinesWholeTree_AndRestores()
    {
        Write(@"work\cachedir\a\b.bin", [7]);
        string dir = Path.Combine(_root, @"work\cachedir");
        var plan = BatchPlan.Create([PlanItem.FromPath(dir, "r1")!]);

        IReadOnlyList<ItemResult> results = _store.Execute(plan);

        Assert.Equal(ItemOutcome.Quarantined, Assert.Single(results).Outcome);
        Assert.False(Directory.Exists(dir));

        _store.Restore(Path.GetPathRoot(dir)!, plan.BatchId);
        Assert.Equal(new byte[] { 7 }, File.ReadAllBytes(Path.Combine(dir, "a", "b.bin")));
    }

    [Fact]
    public void PurgeExpired_RemovesOnlyOldBatches()
    {
        string file = Write(@"work\old.tmp");
        var plan = BatchPlan.Create([PlanItem.FromPath(file, "r1")!]);
        _store.Execute(plan);
        string volumeRoot = Path.GetPathRoot(file)!;
        string batchDir = Path.Combine(volumeRoot, QuarantineStore.DirectoryName, plan.BatchId);
        Directory.SetLastWriteTimeUtc(batchDir, DateTime.UtcNow.AddDays(-8));

        int purged = _store.PurgeExpired(volumeRoot, TimeSpan.FromDays(7));

        Assert.Equal(1, purged);
        Assert.False(Directory.Exists(batchDir));
    }
}
