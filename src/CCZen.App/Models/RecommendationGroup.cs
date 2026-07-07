using CCZen.Engine.Rules;

namespace CCZen.App.Models;

/// <summary>
/// One collapsible group of recommendations sharing the same rule/adapter:
/// the header carries tier, hit count and total size; children are the rows.
/// </summary>
public sealed record RecommendationGroup(
    string RuleId,
    string Tier,
    string Detail,
    long TotalBytes,
    IReadOnlyList<RecommendationRow> Items)
{
    public static RecommendationGroup From(IReadOnlyList<Recommendation> hits)
    {
        Recommendation first = hits[0];
        long totalBytes = hits.Sum(r => r.SizeBytes);
        return new RecommendationGroup(
            first.RuleId,
            first.Tier.ToString(),
            $"{hits.Count} 项 · {SizeFormatter.Format(totalBytes)}",
            totalBytes,
            hits.OrderByDescending(r => r.SizeBytes).Select(RecommendationRow.From).ToList());
    }
}
