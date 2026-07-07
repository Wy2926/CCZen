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
    /// <summary>
    /// Builds a plan from recommendations: default selection is T0/T1 only;
    /// extra explicitly-confirmed T2 paths may be supplied. Protected paths
    /// are excluded up front (they would also be vetoed at execution time).
    /// </summary>
    public static BatchPlan Plan(
        IEnumerable<Recommendation> recommendations,
        ProtectedPaths protection,
        IReadOnlySet<string>? confirmedT2Paths = null)
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
}
