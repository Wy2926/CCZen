using CCZen.Engine.Rules;

namespace CCZen.App.Models;

/// <summary>Immutable display row for one <see cref="Recommendation"/>.</summary>
public sealed record RecommendationRow(string Tier, string Size, string Path, string Explain)
{
    public static RecommendationRow From(Recommendation recommendation) =>
        new(
            recommendation.Tier.ToString(),
            SizeFormatter.Format(recommendation.SizeBytes),
            recommendation.Path,
            recommendation.Action == "report-only"
                ? $"[仅提示] {recommendation.Explain}"
                : recommendation.Explain);
}
