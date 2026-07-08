using CCZen.Engine.Rules;
using CCZen.Engine.Safety;

namespace CCZen.Engine.Tests;

public class CleanupPlannerTests : IDisposable
{
    private readonly string _root;
    private readonly ProtectedPaths _protection;

    public CleanupPlannerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cczen-planner-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "protected"));
        Directory.CreateDirectory(Path.Combine(_root, "work"));
        _protection = new ProtectedPaths([Path.Combine(_root, "protected")], windir: null);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Write(string relative)
    {
        string path = Path.Combine(_root, relative);
        File.WriteAllBytes(path, new byte[10]);
        return path;
    }

    private static Recommendation Rec(string path, Tier tier) => new(
        path, IsDirectory: false, SizeBytes: 10, RuleId: "r", tier,
        Confidence: 0.8, Action: "quarantine", Explain: "x",
        Signals: new Dictionary<string, double>());

    [Fact]
    public void Plan_SelectsOnlyT0AndT1_ByDefault()
    {
        string t0 = Write(@"work\a.tmp");
        string t1 = Write(@"work\b.log");
        string t2 = Write(@"work\c.bak");

        BatchPlan plan = CleanupPlanner.Plan(
            [Rec(t0, Tier.T0), Rec(t1, Tier.T1), Rec(t2, Tier.T2)], _protection);

        Assert.Equal(2, plan.Items.Count);
        Assert.DoesNotContain(plan.Items, i => i.Path == t2);
    }

    [Fact]
    public void Plan_IncludesT2_OnlyWhenExplicitlyConfirmed()
    {
        string t2 = Write(@"work\c.bak");
        var confirmed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { t2 };

        BatchPlan plan = CleanupPlanner.Plan([Rec(t2, Tier.T2)], _protection, confirmed);

        Assert.Single(plan.Items);
    }

    [Fact]
    public void Plan_NeverIncludesT3_EvenWhenConfirmed()
    {
        string t3 = Write(@"work\d.big");
        var confirmed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { t3 };

        BatchPlan plan = CleanupPlanner.Plan([Rec(t3, Tier.T3)], _protection, confirmed);

        Assert.Empty(plan.Items);
    }

    [Fact]
    public void Plan_ExcludesProtectedPaths()
    {
        string inside = Write(@"protected\asset.tmp");

        BatchPlan plan = CleanupPlanner.Plan([Rec(inside, Tier.T0)], _protection);

        Assert.Empty(plan.Items);
    }

    [Fact]
    public void Plan_WithSelectedPaths_LimitsToSelection()
    {
        string t0 = Write(@"work\a.tmp");
        string t1 = Write(@"work\b.log");
        var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { t1 };

        BatchPlan plan = CleanupPlanner.Plan([Rec(t0, Tier.T0), Rec(t1, Tier.T1)], _protection, null, selected);

        Assert.Single(plan.Items);
        Assert.Equal(t1, plan.Items[0].Path);
    }

    [Fact]
    public void Plan_SelectedT2_StillRequiresConfirmation()
    {
        string t2 = Write(@"work\c.bak");
        var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { t2 };

        BatchPlan unconfirmed = CleanupPlanner.Plan([Rec(t2, Tier.T2)], _protection, null, selected);
        Assert.Empty(unconfirmed.Items);

        BatchPlan confirmed = CleanupPlanner.Plan([Rec(t2, Tier.T2)], _protection, selected, selected);
        Assert.Single(confirmed.Items);
    }

    [Fact]
    public void PlanQuarantine_ExcludesProtected_AndTagsManualRule()
    {
        string picked = Write(@"work\huge.iso");
        string inside = Write(@"protected\asset.tmp");

        BatchPlan plan = CleanupPlanner.PlanQuarantine([picked, inside], _protection);

        Assert.Single(plan.Items);
        Assert.Equal(picked, plan.Items[0].Path);
        Assert.Equal(CleanupPlanner.ManualRuleId, plan.Items[0].RuleId);
    }

    [Fact]
    public void Plan_SkipsVanishedPaths()
    {
        string ghost = Path.Combine(_root, "work", "gone.tmp");

        BatchPlan plan = CleanupPlanner.Plan([Rec(ghost, Tier.T0)], _protection);

        Assert.Empty(plan.Items);
    }
}
