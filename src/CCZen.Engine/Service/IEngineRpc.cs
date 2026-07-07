using CCZen.Engine.Index;
using CCZen.Engine.Rules;
using CCZen.Engine.Safety;

namespace CCZen.Engine.Service;

/// <summary>
/// JSON-RPC contract exposed by the engine over a named pipe (spec: 05 进程模型).
/// The UI/CLI client attaches via StreamJsonRpc and issues queries against the
/// in-memory index held by the service.
/// </summary>
public interface IEngineRpc
{
    /// <summary>Scans (or incrementally refreshes) a volume and holds the resulting index.</summary>
    Task<ScanSummary> ScanAsync(string root, bool useCache, CancellationToken cancellationToken);

    /// <summary>Returns the largest files of the last scanned index.</summary>
    Task<IReadOnlyList<FileEntry>> GetTopFilesAsync(int count, CancellationToken cancellationToken);

    /// <summary>Returns the largest directory subtrees of the last scanned index.</summary>
    Task<IReadOnlyList<FileEntry>> GetTopDirectoriesAsync(int count, CancellationToken cancellationToken);

    /// <summary>Returns the current service/index status without side effects.</summary>
    Task<ScanSummary?> GetStatusAsync(CancellationToken cancellationToken);

    /// <summary>Runs adapter + generic rule evaluation and returns merged recommendations (specs/02, 03).</summary>
    Task<IReadOnlyList<Recommendation>> RecommendAsync(CancellationToken cancellationToken);

    /// <summary>Builds an immutable batch plan from the last recommendations (T0/T1 + confirmed T2 paths).</summary>
    Task<BatchPlan> PlanCleanAsync(IReadOnlyList<string>? confirmedT2Paths, CancellationToken cancellationToken);

    /// <summary>Executes a previously planned batch by id; returns per-item audit results (specs/04).</summary>
    Task<IReadOnlyList<ItemResult>> ExecuteBatchAsync(string batchId, CancellationToken cancellationToken);

    /// <summary>Restores a quarantined batch back to original paths.</summary>
    Task<IReadOnlyList<ItemResult>> RestoreBatchAsync(string volumeRoot, string batchId, CancellationToken cancellationToken);

    /// <summary>Lists quarantine batches on a volume.</summary>
    Task<IReadOnlyList<BatchInfo>> ListBatchesAsync(string volumeRoot, CancellationToken cancellationToken);
}

/// <summary>Quarantine batch listing entry (SAFE-FR-027).</summary>
public sealed record BatchInfo(string BatchId, long SizeBytes, DateTime LastWriteUtc);

/// <summary>Aggregate result of a completed scan.</summary>
public sealed record ScanSummary(
    string Root,
    int EntryCount,
    int FileCount,
    long TotalLogicalSize,
    long TotalAllocatedSize,
    double ElapsedSeconds,
    bool Incremental);
