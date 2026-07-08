using CCZen.Engine.Rules;

namespace CCZen.Engine.Tests;

internal static class ParityAssertions
{
    /// <summary>
    /// Asserts index-driven and walk-driven recommendations match on path, size, tier, and rule id.
    /// </summary>
    public static void AssertRecommendationsMatch(
        IReadOnlyList<Recommendation> indexDriven,
        IReadOnlyList<Recommendation> walkDriven)
    {
        var indexByPath = indexDriven.ToDictionary(r => r.Path, StringComparer.OrdinalIgnoreCase);
        var walkByPath = walkDriven.ToDictionary(r => r.Path, StringComparer.OrdinalIgnoreCase);

        Assert.Equal(walkByPath.Count, indexByPath.Count);

        foreach ((string path, Recommendation walk) in walkByPath)
        {
            Assert.True(indexByPath.TryGetValue(path, out Recommendation? index), $"Missing index hit for {path}");
            Assert.Equal(walk.RuleId, index.RuleId);
            Assert.Equal(walk.Tier, index.Tier);
            Assert.Equal(walk.SizeBytes, index.SizeBytes);
            Assert.Equal(walk.IsDirectory, index.IsDirectory);
            Assert.Equal(walk.Action, index.Action);
        }
    }

    public static double ParityRate(
        IReadOnlyList<Recommendation> indexDriven,
        IReadOnlyList<Recommendation> walkDriven)
    {
        if (walkDriven.Count == 0)
        {
            return indexDriven.Count == 0 ? 1.0 : 0.0;
        }

        var indexByPath = indexDriven.ToDictionary(r => r.Path, StringComparer.OrdinalIgnoreCase);
        int matches = walkDriven.Count(w =>
            indexByPath.TryGetValue(w.Path, out Recommendation? index) &&
            index.RuleId == w.RuleId &&
            index.Tier == w.Tier &&
            index.SizeBytes == w.SizeBytes);

        return (double)matches / walkDriven.Count;
    }
}
