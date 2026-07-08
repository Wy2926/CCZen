using System.Diagnostics;
using System.Runtime.Versioning;
using CCZen.Engine.Index;
using CCZen.Engine.Rules;
using CCZen.Engine.Safety;
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
    private readonly Func<EnvironmentModel> _discoverEnvironment;
    private readonly Func<string> _getScanRoot;
    private readonly Func<string, IVolumeScanner> _createScanner;
    private readonly ProtectedPaths _protection;
    private readonly QuarantineStore _quarantine;
    private readonly ReaderWriterLockSlim _indexLock = new(LockRecursionPolicy.NoRecursion);
    private readonly Dictionary<string, BatchPlan> _plans = new(StringComparer.OrdinalIgnoreCase);
    private FileSystemIndex? _index;
    private ScanSummary? _summary;
    private IReadOnlyList<Recommendation>? _recommendations;

    public EngineRpcServer(
        string? cacheDirectory = null,
        Func<EnvironmentModel>? discoverEnvironment = null,
        ProtectedPaths? protection = null,
        Func<string>? getScanRoot = null,
        Func<string, IVolumeScanner>? createScanner = null)
    {
        _cacheDirectory = cacheDirectory;
        _discoverEnvironment = discoverEnvironment ?? EnvironmentDiscovery.Discover;
        _getScanRoot = getScanRoot ?? GetDefaultScanRoot;
        _createScanner = createScanner ?? VolumeScannerFactory.Create;
        _protection = protection ?? new ProtectedPaths();
        _quarantine = new QuarantineStore(_protection);
    }

    private static string GetDefaultScanRoot() =>
        Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";

    public Task<ScanSummary> ScanAsync(string root, bool useCache, CancellationToken cancellationToken) =>
        Task.Run(() => ScanCore(root, useCache, cancellationToken), cancellationToken);

    private ScanSummary ScanCore(string root, bool useCache, CancellationToken cancellationToken)
    {
        _indexLock.EnterWriteLock();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var stopwatch = Stopwatch.StartNew();
            FileSystemIndex index;
            bool incremental = false;
            IVolumeScanner scanner = _createScanner(root);
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
        }
        finally
        {
            _indexLock.ExitWriteLock();
        }
    }

    private void EnsureIndex(string root, bool useCache, CancellationToken cancellationToken) =>
        ScanCore(root, useCache, cancellationToken);

    public Task<IReadOnlyList<FileEntry>> GetTopFilesAsync(int count, CancellationToken cancellationToken) =>
        Task.Run(
            () =>
            {
                _indexLock.EnterReadLock();
                try
                {
                    return RequireIndexCore().TopFiles(count);
                }
                finally
                {
                    _indexLock.ExitReadLock();
                }
            },
            cancellationToken);

    public Task<IReadOnlyList<FileEntry>> GetTopDirectoriesAsync(int count, CancellationToken cancellationToken) =>
        Task.Run(
            () =>
            {
                _indexLock.EnterReadLock();
                try
                {
                    return RequireIndexCore().TopDirectories(count);
                }
                finally
                {
                    _indexLock.ExitReadLock();
                }
            },
            cancellationToken);

    public Task<IReadOnlyList<FileEntry>> GetTopDistinctDirectoriesAsync(int count, CancellationToken cancellationToken) =>
        Task.Run(
            () =>
            {
                _indexLock.EnterReadLock();
                try
                {
                    return RequireIndexCore().TopDistinctDirectories(count);
                }
                finally
                {
                    _indexLock.ExitReadLock();
                }
            },
            cancellationToken);

    public Task<IReadOnlyList<FileEntry>> SearchAsync(SearchQuery query, CancellationToken cancellationToken) =>
        Task.Run(
            () =>
            {
                _indexLock.EnterReadLock();
                try
                {
                    return RequireIndexCore().Search(query);
                }
                finally
                {
                    _indexLock.ExitReadLock();
                }
            },
            cancellationToken);

    public Task<ScanSummary?> GetStatusAsync(CancellationToken cancellationToken) =>
        Task.Run(
            () =>
            {
                _indexLock.EnterReadLock();
                try
                {
                    return _summary;
                }
                finally
                {
                    _indexLock.ExitReadLock();
                }
            },
            cancellationToken);

    public Task<IReadOnlyList<Recommendation>> RecommendAsync(CancellationToken cancellationToken) =>
        Task.Run(
            () =>
            {
                EnsureIndex(_getScanRoot(), useCache: true, cancellationToken);
                _indexLock.EnterReadLock();
                try
                {
                    EnvironmentModel environment = _discoverEnvironment();
                    var query = new IndexQuery(RequireIndexCore());
                    IReadOnlyList<Recommendation> adapterHits =
                        new AdapterEngine(environment, BaselineAdapterPack.Load(), query).Evaluate();
                    IReadOnlyList<Recommendation> genericHits =
                        new RuleEngine(environment, BaselineRulePack.Load(), query).Evaluate();
                    IReadOnlyList<Recommendation> merged = AdapterEngine.Merge(adapterHits, genericHits);
                    _recommendations = merged;
                    return merged;
                }
                finally
                {
                    _indexLock.ExitReadLock();
                }
            },
            cancellationToken);

    public async Task<BatchPlan> PlanCleanAsync(IReadOnlyList<string>? confirmedT2Paths, IReadOnlyList<string>? selectedPaths, CancellationToken cancellationToken)
    {
        IReadOnlyList<Recommendation> recommendations =
            _recommendations ?? await RecommendAsync(cancellationToken).ConfigureAwait(false);
        HashSet<string>? confirmed = confirmedT2Paths is { Count: > 0 }
            ? new HashSet<string>(confirmedT2Paths, StringComparer.OrdinalIgnoreCase)
            : null;
        HashSet<string>? selected = selectedPaths is not null
            ? new HashSet<string>(selectedPaths, StringComparer.OrdinalIgnoreCase)
            : null;
        BatchPlan plan = CleanupPlanner.Plan(recommendations, _protection, confirmed, selected);
        _plans[plan.BatchId] = plan;
        return plan;
    }

    public Task<BatchPlan> PlanQuarantineAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken) =>
        Task.Run(
            () =>
            {
                BatchPlan plan = CleanupPlanner.PlanQuarantine(paths, _protection);
                _plans[plan.BatchId] = plan;
                return plan;
            },
            cancellationToken);

    public Task<IReadOnlyList<ItemResult>> ExecuteBatchAsync(string batchId, bool permanentDelete, IProgress<ExecuteProgress>? progress, CancellationToken cancellationToken)
    {
        if (!_plans.TryGetValue(batchId, out BatchPlan? plan))
        {
            throw new InvalidOperationException($"Unknown batch '{batchId}'. Call PlanClean first.");
        }

        _plans.Remove(batchId);
        return Task.Run<IReadOnlyList<ItemResult>>(
            () => _quarantine.Execute(
                plan,
                permanentDelete,
                (done, total, path) => progress?.Report(new ExecuteProgress(done, total, path))),
            cancellationToken);
    }

    public Task<IReadOnlyList<ItemResult>> RestoreBatchAsync(string volumeRoot, string batchId, CancellationToken cancellationToken) =>
        Task.Run<IReadOnlyList<ItemResult>>(() => _quarantine.Restore(volumeRoot, batchId), cancellationToken);

    public Task<IReadOnlyList<BatchInfo>> ListBatchesAsync(string volumeRoot, CancellationToken cancellationToken) =>
        Task.Run<IReadOnlyList<BatchInfo>>(
            () =>
            {
                string quarantineRoot = Path.Combine(volumeRoot, QuarantineStore.DirectoryName);
                if (!Directory.Exists(quarantineRoot))
                {
                    return [];
                }

                var batches = new List<BatchInfo>();
                foreach (string batch in Directory.EnumerateDirectories(quarantineRoot))
                {
                    long size = Directory.EnumerateFiles(batch, "*", SearchOption.AllDirectories)
                        .Sum(f => new FileInfo(f).Length);
                    batches.Add(new BatchInfo(Path.GetFileName(batch), size, Directory.GetLastWriteTimeUtc(batch)));
                }

                return batches;
            },
            cancellationToken);

    public Task<bool> PurgeBatchAsync(string volumeRoot, string batchId, CancellationToken cancellationToken) =>
        Task.Run(() => _quarantine.PurgeBatch(volumeRoot, batchId), cancellationToken);

    private FileSystemIndex RequireIndexCore() =>
        _index ?? throw new InvalidOperationException("No index available. Call Scan first.");
}
