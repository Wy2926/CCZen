using System.Runtime.Versioning;
using CCZen.Engine.Rules;

namespace CCZen.Engine.Safety;

/// <summary>
/// Turns rule-engine recommendations into an immutable batch plan
/// (spec: 02 管线 4 默认行为 + 04 SAFE-FR-010). Only T0/T1 items are eligible
/// for one-click cleanup; T2/T3 must be opted in per item and can never be
/// promoted to automatic cleanup.
/// </summary>
[SupportedOSPlatform("windows")]
public static class CleanupPlanner
{
    /// <summary>Rule id attached to manually quarantined paths (large-file search).</summary>
    public const string ManualRuleId = "manual/quarantine";

    /// <summary>
    /// Builds a plan from recommendations: default selection is T0/T1 only;
    /// extra explicitly-confirmed T2 paths may be supplied. When
    /// <paramref name="selectedPaths"/> is non-null, only those paths are
    /// eligible (per-item checkbox selection). Protected paths are excluded
    /// up front (they would also be vetoed at execution time).
    /// </summary>
    public static BatchPlan Plan(
        IEnumerable<Recommendation> recommendations,
        ProtectedPaths protection,
        IReadOnlySet<string>? confirmedT2Paths = null,
        IReadOnlySet<string>? selectedPaths = null)
    {
        var items = new List<PlanItem>();
        foreach (Recommendation recommendation in recommendations)
        {
            if (recommendation.Action == "report-only")
            {
                continue;
            }

            bool eligible = recommendation.Tier is Tier.T0 or Tier.T1
                || (recommendation.Tier == Tier.T2 &&
                    confirmedT2Paths?.Contains(recommendation.Path) == true);
            if (selectedPaths is not null && !selectedPaths.Contains(recommendation.Path))
            {
                continue;
            }

            if (!eligible || protection.IsProtected(recommendation.Path))
            {
                continue;
            }

            if (PlanItem.FromPath(recommendation.Path, recommendation.RuleId) is { } item)
            {
                items.Add(item);
            }
        }

        return BatchPlan.Create(items);
    }

    /// <summary>
    /// Builds a plan that moves explicitly user-picked paths (e.g. large-file
    /// search results) into quarantine. Deletion stays reversible: items go to
    /// quarantine and can be restored per batch (spec 04 red line 1).
    /// </summary>
    public static BatchPlan PlanQuarantine(IEnumerable<string> paths, ProtectedPaths protection)
    {
        var items = new List<PlanItem>();
        foreach (string path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (protection.IsProtected(path))
            {
                continue;
            }

            if (PlanItem.FromPath(path, ManualRuleId) is { } item)
            {
                items.Add(item);
            }
        }

        return BatchPlan.Create(items);
    }
}
