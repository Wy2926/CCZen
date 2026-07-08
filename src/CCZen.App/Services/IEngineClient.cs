using CCZen.Engine.Index;
using CCZen.Engine.Rules;
using CCZen.Engine.Safety;
using CCZen.Engine.Service;

namespace CCZen.App.Services;

/// <summary>
/// UI-facing abstraction over the engine clean pipeline (specs/05 进程模型).
/// View models depend on this interface, never on transport details.
/// </summary>
public interface IEngineClient
{
    /// <summary>Returns the current index status, or null when nothing was scanned yet.</summary>
    Task<ScanSummary?> GetStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>Scans a volume and builds the in-memory index (SCAN-FR-020).</summary>
    Task<ScanSummary> ScanAsync(string root, CancellationToken cancellationToken = default);

    /// <summary>Conditional search for large files/directories over the index (SCAN-FR-025).</summary>
    Task<IReadOnlyList<FileEntry>> SearchAsync(SearchQuery query, CancellationToken cancellationToken = default);

    /// <summary>Runs adapter + generic rule evaluation and returns merged recommendations.</summary>
    Task<IReadOnlyList<Recommendation>> RecommendAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Builds an immutable batch plan from the last recommendations. Default
    /// selection is T0/T1; T2 paths need explicit confirmation. A non-null
    /// <paramref name="selectedPaths"/> limits the plan to checked items.
    /// </summary>
    Task<BatchPlan> PlanCleanAsync(IReadOnlyList<string>? confirmedT2Paths = null, IReadOnlyList<string>? selectedPaths = null, CancellationToken cancellationToken = default);

    /// <summary>Builds a reversible quarantine plan for user-picked paths (large-file search).</summary>
    Task<BatchPlan> PlanQuarantineAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken = default);

    /// <summary>Executes a planned batch exactly once (SAFE-FR-010); items move to quarantine.</summary>
    Task<IReadOnlyList<ItemResult>> ExecuteBatchAsync(string batchId, CancellationToken cancellationToken = default);

    /// <summary>Restores a quarantined batch back to original paths.</summary>
    Task<IReadOnlyList<ItemResult>> RestoreBatchAsync(string volumeRoot, string batchId, CancellationToken cancellationToken = default);
}
