using CCZen.Engine.Index;

namespace CCZen.Engine.Scanning;

public interface IVolumeScanner
{
    /// <summary>Scans the given root (e.g. "C:\") and builds a full file system index.</summary>
    FileSystemIndex Scan(string root, CancellationToken cancellationToken = default);
}

/// <summary>Result of a cache-aware scan; <see cref="Incremental"/> is true when USN catch-up was used.</summary>
public readonly record struct ScanResult(FileSystemIndex Index, bool Incremental);
