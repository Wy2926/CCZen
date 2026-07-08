using CCZen.Engine.Rules;

namespace CCZen.Engine.Tests;

/// <summary>
/// Golden tests for the rules pipeline (spec: 02 测试要求): a fixed fixture
/// tree must produce exactly the expected recommendation set and tiers.
/// </summary>
public class RuleEngineTests : IDisposable
{
    private readonly string _root;
    private readonly EnvironmentModel _environment;

    public RuleEngineTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cczen-rules-" + Guid.NewGuid().ToString("N"));
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

    private IReadOnlyList<Recommendation> Evaluate() =>
        new RuleEngine(_environment, BaselineRulePack.Load(), TestIndexFactory.FromDirectory(_root)).Evaluate();

    [Fact]
    public void BaselinePack_PassesSchemaValidation()
    {
        RulePack pack = BaselineRulePack.Load();

        Assert.True(pack.Rules.Count >= 5);
        Assert.Contains("cache_words", pack.Lexicons.Keys);
    }

    [Fact]
    public void RulePack_InvalidTier_IsRejected()
    {
        string json = """{"schemaVersion":1,"lexicons":{},"rules":[{"id":"x","tierCap":"T9","targets":["${TEMP}"],"action":"quarantine","explain":"x"}]}""";

        Assert.Throws<InvalidDataException>(() => RulePack.Load(json));
    }

    [Fact]
    public void CacheDirectory_IsRecommendedAtT1()
    {
        Write(@"AppData\Local\SomeApp\Cache\data.bin", 5000);

        IReadOnlyList<Recommendation> recommendations = Evaluate();

        Recommendation hit = Assert.Single(recommendations, r => r.RuleId == "generic-app-cache");
        Assert.Equal(Tier.T1, hit.Tier);
        Assert.EndsWith("Cache", hit.Path);
        Assert.Equal(5000, hit.SizeBytes);
    }

    [Fact]
    public void PhotosInsideCacheDirectory_AreDemotedToT2()
    {
        // 对抗夹具：用户照片放入名为 cache 的目录 → content_type 一票降级
        Write(@"AppData\Local\SomeApp\cache\vacation.jpg", 5000);

        IReadOnlyList<Recommendation> recommendations = Evaluate();

        Recommendation hit = Assert.Single(recommendations, r => r.RuleId == "generic-app-cache");
        Assert.Equal(Tier.T2, hit.Tier);
        Assert.Equal(0, hit.Signals["content_type"]);
    }

    [Fact]
    public void TempDirectoryContents_AreT0()
    {
        Write(@"Temp\junk.dat", 300);

        IReadOnlyList<Recommendation> recommendations = Evaluate();

        Recommendation hit = Assert.Single(recommendations, r => r.RuleId == "system-user-temp");
        Assert.Equal(Tier.T0, hit.Tier);
        Assert.Equal("delete-contents", hit.Action);
    }

    [Fact]
    public void LogAndDumpFiles_AreT1_AndBakIsT2()
    {
        Write(@"AppData\Local\SomeApp\debug.log", 700);
        Write(@"AppData\Local\SomeApp\settings.bak", 900);

        IReadOnlyList<Recommendation> recommendations = Evaluate();

        Recommendation log = Assert.Single(recommendations, r => r.RuleId == "generic-log-dump-files");
        Assert.Equal(Tier.T1, log.Tier);
        Recommendation bak = Assert.Single(recommendations, r => r.RuleId == "generic-bak-files");
        Assert.Equal(Tier.T2, bak.Tier);
    }

    [Fact]
    public void InstallerInDownloads_IsT2Recycle()
    {
        Write(@"Downloads\setup-tool.exe", 1200);

        IReadOnlyList<Recommendation> recommendations = Evaluate();

        Recommendation hit = Assert.Single(recommendations, r => r.RuleId == "generic-installer-leftover");
        Assert.Equal(Tier.T2, hit.Tier);
        Assert.Equal("recycle", hit.Action);
    }

    [Fact]
    public void UserDocumentArea_ProducesNoRecommendations()
    {
        Write(@"Documents\thesis.docx", 5000);

        Assert.Empty(Evaluate());
    }

    [Fact]
    public void ClaimedPath_IsNotDoubleReported()
    {
        // A .log inside a cache dir: the cache-dir rule claims the directory,
        // and the file rule may still claim the file, but the same path is
        // never reported twice.
        Write(@"AppData\Local\App\cache\trace.log", 400);

        IReadOnlyList<Recommendation> recommendations = Evaluate();

        Assert.Equal(recommendations.Count, recommendations.Select(r => r.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Expand_UnboundSymbol_ReturnsNull()
    {
        Assert.Null(_environment.Expand("${NOPE}/x"));
        Assert.Equal(_environment.Symbols["TEMP"] + "\\y", _environment.Expand("${TEMP}\\y"));
    }
}
