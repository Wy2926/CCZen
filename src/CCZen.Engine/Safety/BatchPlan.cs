namespace CCZen.Engine.Safety;

/// <summary>
/// Immutable execution plan for one cleanup batch (spec: SAFE-FR-010). The
/// user confirms this exact snapshot; each item carries a fingerprint that is
/// re-verified at execution time to defeat TOCTOU swaps (SAFE-FR-011).
/// </summary>
public sealed record BatchPlan(string BatchId, DateTime CreatedUtc, IReadOnlyList<PlanItem> Items)
{
    public static BatchPlan Create(IEnumerable<PlanItem> items) =>
        new(Guid.NewGuid().ToString("N"), DateTime.UtcNow, items.ToList());
}

/// <summary>One planned deletion with its identity fingerprint.</summary>
public sealed record PlanItem(string Path, bool IsDirectory, long SizeBytes, DateTime LastWriteUtc, string RuleId)
{
    /// <summary>Fingerprints an existing filesystem entry, or null when it is gone.</summary>
    public static PlanItem? FromPath(string path, string ruleId)
    {
        if (Directory.Exists(path))
        {
            var info = new DirectoryInfo(path);
            return new PlanItem(info.FullName, IsDirectory: true, SizeBytes: 0, info.LastWriteTimeUtc, ruleId);
        }

        if (File.Exists(path))
        {
            var info = new FileInfo(path);
            return new PlanItem(info.FullName, IsDirectory: false, info.Length, info.LastWriteTimeUtc, ruleId);
        }

        return null;
    }

    /// <summary>SAFE-FR-011: path + size + mtime must still match at execution time.</summary>
    public bool FingerprintMatches()
    {
        if (IsDirectory)
        {
            return Directory.Exists(Path);
        }

        if (!File.Exists(Path))
        {
            return false;
        }

        var info = new FileInfo(Path);
        return info.Length == SizeBytes && info.LastWriteTimeUtc == LastWriteUtc;
    }
}

/// <summary>Outcome of one plan item during batch execution.</summary>
public enum ItemOutcome
{
    Quarantined,
    SkippedProtected,
    SkippedFingerprintMismatch,
    SkippedMissing,
    SkippedReparsePoint,
    Failed,
}

/// <summary>Per-item execution record for the audit log (SAFE-FR-050).</summary>
public sealed record ItemResult(string Path, ItemOutcome Outcome, string? QuarantinePath, string? Detail);
