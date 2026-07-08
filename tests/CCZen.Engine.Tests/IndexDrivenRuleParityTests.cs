using CCZen.Engine.Index;
using CCZen.Engine.Rules;

namespace CCZen.Engine.Tests;

/// <summary>
/// AC-B04: index-driven Rule/Adapter engines must match the pre-merge directory-walk baseline (spec: 02).
/// </summary>
public class IndexDrivenRuleParityTests : IDisposable
{
    private readonly string _root;
    private readonly EnvironmentModel _environment;

    public IndexDrivenRuleParityTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cczen-parity-" + Guid.NewGuid().ToString("N"));
        string localAppData = Path.Combine(_root, "AppData", "Local");
        string temp = Path.Combine(_root, "Temp");
        string downloads = Path.Combine(_root, "Downloads");
        string programData = Path.Combine(_root, "ProgramData");
        Directory.CreateDirectory(localAppData);
        Directory.CreateDirectory(temp);
        Directory.CreateDirectory(downloads);
        Directory.CreateDirectory(programData);

        _environment = new EnvironmentModel
        {
            Symbols = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["LOCALAPPDATA"] = localAppData,
                ["TEMP"] = temp,
                ["DOWNLOADS"] = downloads,
                ["PROGRAMDATA"] = programData,
            },
            InstalledApps = [],
            RunningProcesses = new HashSet<string>(),
            Volumes = [],
        };
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private void Write(string relative, int bytes = 100)
    {
        string path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[bytes]);
    }

    private void SeedCompositeFixture()
    {
        Write(@"Temp\session.tmp", 300);
        Write(@"AppData\Local\SomeApp\Cache\data.bin", 5000);
        Write(@"AppData\Local\SomeApp\cache\vacation.jpg", 5000);
        Write(@"AppData\Local\SomeApp\debug.log", 700);
        Write(@"AppData\Local\SomeApp\settings.bak", 900);
        Write(@"Downloads\setup-tool.exe", 1200);
        Write(@"AppData\Local\Google\Chrome\User Data\Default\Cache\f_000001", 4096);
        Write(@"AppData\Local\Google\Chrome\User Data\Profile 1\Cache\f_000001", 2048);
    }

    [Fact]
    public void RuleEngine_BaselinePack_MatchesWalkBaseline()
    {
        SeedCompositeFixture();
        RulePack pack = BaselineRulePack.Load();
        IndexQuery query = TestIndexFactory.FromDirectory(_root);

        IReadOnlyList<Recommendation> indexHits = new RuleEngine(_environment, pack, query).Evaluate();
        IReadOnlyList<Recommendation> walkHits = new RuleWalkBaseline(_environment, pack).Evaluate();

        ParityAssertions.AssertRecommendationsMatch(indexHits, walkHits);
        Assert.True(ParityAssertions.ParityRate(indexHits, walkHits) >= 0.999);
    }

    [Fact]
    public void AdapterEngine_ChromePack_MatchesWalkBaseline()
    {
        Write(@"Google\Chrome\User Data\Default\Cache\f_000001", 4096);
        Write(@"Google\Chrome\User Data\Profile 1\Cache\f_000001", 2048);
        Write(@"Google\Chrome\User Data\Profile 2\nothing.txt", 50);

        var env = new EnvironmentModel
        {
            Symbols = new Dictionary<string, string> { ["LOCALAPPDATA"] = _root },
            InstalledApps = [],
            RunningProcesses = new HashSet<string>(),
            Volumes = [],
        };

        AdapterPack pack = TestAdapterPacks.ChromeBrowser();
        IndexQuery query = TestIndexFactory.FromDirectory(_root);

        IReadOnlyList<Recommendation> indexHits = new AdapterEngine(env, pack, query).Evaluate();
        IReadOnlyList<Recommendation> walkHits = new AdapterWalkBaseline(env, pack).Evaluate();

        ParityAssertions.AssertRecommendationsMatch(indexHits, walkHits);
    }

    [Fact]
    public void MergedPipeline_BaselinePacks_MatchesWalkBaseline()
    {
        SeedCompositeFixture();
        IndexQuery query = TestIndexFactory.FromDirectory(_root);

        IReadOnlyList<Recommendation> indexMerged = AdapterEngine.Merge(
            new AdapterEngine(_environment, BaselineAdapterPack.Load(), query).Evaluate(),
            new RuleEngine(_environment, BaselineRulePack.Load(), query).Evaluate());

        IReadOnlyList<Recommendation> walkMerged = AdapterEngine.Merge(
            new AdapterWalkBaseline(_environment, BaselineAdapterPack.Load()).Evaluate(),
            new RuleWalkBaseline(_environment, BaselineRulePack.Load()).Evaluate());

        ParityAssertions.AssertRecommendationsMatch(indexMerged, walkMerged);
        Assert.True(ParityAssertions.ParityRate(indexMerged, walkMerged) >= 0.999);
    }

    [Fact]
    public void EmptyFixture_BothEnginesAgreeOnEmpty()
    {
        IndexQuery query = TestIndexFactory.FromDirectory(_root);

        IReadOnlyList<Recommendation> indexHits = new RuleEngine(_environment, BaselineRulePack.Load(), query).Evaluate();
        IReadOnlyList<Recommendation> walkHits = new RuleWalkBaseline(_environment, BaselineRulePack.Load()).Evaluate();

        Assert.Empty(indexHits);
        Assert.Empty(walkHits);
    }
}
