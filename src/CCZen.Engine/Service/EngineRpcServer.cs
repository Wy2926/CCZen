using System.Diagnostics;
using System.Runtime.Versioning;
using CCZen.Engine.Index;
using CCZen.Engine.Scanning;

namespace CCZen.Engine.Service;

/// <summary>
/// In-process implementation of <see cref="IEngineRpc"/>. Holds the most recent
/// index in memory so query methods answer without rescanning.
/// </summary>
[SupportedOSPlatform("windows5.1.2600")]
public sealed class EngineRpcServer : IEngineRpc
{
    private readonly string? _cacheDirectory;
    private FileSystemIndex? _index;
    private ScanSummary? _summary;

    public EngineRpcServer(string? cacheDirectory = null)
    {
        _cacheDirectory = cacheDirectory;
    }

    public Task<ScanSummary> ScanAsync(string root, bool useCache, CancellationToken cancellationToken) =>
        Task.Run(
            () =>
            {
                var stopwatch = Stopwatch.StartNew();
                FileSystemIndex index;
                bool incremental = false;
                IVolumeScanner scanner = VolumeScannerFactory.Create(root);
                if (useCache && scanner is UsnJournalScanner usn && _cacheDirectory is not null)
                {
                    Directory.CreateDirectory(_cacheDirectory);
                    string cachePath = Path.Combine(_cacheDirectory, $"{char.ToUpperInvariant(root[0])}.idx");
                    (index, incremental) = usn.ScanWithCache(root, cachePath, cancellationToken);
                }
                else
                {
                    index = scanner.Scan(root, cancellationToken);
                }

                stopwatch.Stop();
                var summary = new ScanSummary(
                    root,
                    index.Count,
                    index.FileCount,
                    index.TotalLogicalSize,
                    index.TotalAllocatedSize,
                    stopwatch.Elapsed.TotalSeconds,
                    incremental);
                _index = index;
                _summary = summary;
                return summary;
            },
            cancellationToken);

    public Task<IReadOnlyList<FileEntry>> GetTopFilesAsync(int count, CancellationToken cancellationToken) =>
        Task.FromResult(RequireIndex().TopFiles(count));

    public Task<IReadOnlyList<FileEntry>> GetTopDirectoriesAsync(int count, CancellationToken cancellationToken) =>
        Task.FromResult(RequireIndex().TopDirectories(count));

    public Task<ScanSummary?> GetStatusAsync(CancellationToken cancellationToken) => Task.FromResult(_summary);

    private FileSystemIndex RequireIndex() =>
        _index ?? throw new InvalidOperationException("No index available. Call Scan first.");
}
