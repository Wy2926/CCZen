using CCZen.Engine.Index;

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
}

/// <summary>Aggregate result of a completed scan.</summary>
public sealed record ScanSummary(
    string Root,
    int EntryCount,
    int FileCount,
    long TotalLogicalSize,
    long TotalAllocatedSize,
    double ElapsedSeconds,
    bool Incremental);
