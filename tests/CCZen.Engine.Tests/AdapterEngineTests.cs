using CCZen.Engine.Index;
using CCZen.Engine.Rules;

namespace CCZen.Engine.Tests;

public class AdapterEngineTests : IDisposable
{
    private readonly string _root;

    public AdapterEngineTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cczen-adapter-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private void Write(string relative, int bytes = 100)
    {
        string path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[bytes]);
    }

    private EnvironmentModel Env(params string[] runningProcesses) => new()
    {
        Symbols = new Dictionary<string, string> { ["LOCALAPPDATA"] = _root },
        InstalledApps = [],
        RunningProcesses = new HashSet<string>(runningProcesses),
        Volumes = [],
    };

    private static AdapterPack BrowserPack() => TestAdapterPacks.ChromeBrowser();

    [Fact]
    public void SchemaValidation_RejectsUnknownFields()
    {
        Assert.Throws<InvalidDataException>(() => AdapterPack.Load(
            """{"schemaVersion":1,"adapters":[{"id":"x","name":"x","category":"browser","detect":{},"items":[],"evil":"code"}]}"""));
    }

    private IndexQuery Index() => TestIndexFactory.FromDirectory(_root);

    [Fact]
    public void MiddleWildcard_EnumeratesPerProfileCaches()
    {
        Write(@"Google\Chrome\User Data\Default\Cache\f_000001");
        Write(@"Google\Chrome\User Data\Profile 1\Cache\f_000001");
        Write(@"Google\Chrome\User Data\Profile 2\nothing.txt");

        IReadOnlyList<Recommendation> hits = new AdapterEngine(Env(), BrowserPack(), Index()).Evaluate();

        Assert.Equal(2, hits.Count);
        Assert.All(hits, h => Assert.Equal("chrome/http-cache", h.RuleId));
        Assert.All(hits, h => Assert.Equal("quarantine", h.Action));
    }

    [Fact]
    public void AppRunning_DowngradesToReportOnly()
    {
        Write(@"Google\Chrome\User Data\Default\Cache\f_000001");

        IReadOnlyList<Recommendation> hits = new AdapterEngine(Env("chrome"), BrowserPack(), Index()).Evaluate();

        Assert.Equal("report-only", Assert.Single(hits).Action);
    }

    [Fact]
    public void UndetectedAdapter_ProducesNothing()
    {
        // No Chrome install dir, no process.
        IReadOnlyList<Recommendation> hits = new AdapterEngine(Env(), BrowserPack(), Index()).Evaluate();

        Assert.Empty(hits);
    }

    [Fact]
    public void Merge_AdapterHitTakesOverGenericHit()
    {
        var adapterHit = new Recommendation(
            Path.Combine(_root, "cachedir"), true, 100, "chrome/http-cache", Tier.T1, 0.9,
            "quarantine", "x", new Dictionary<string, double>());
        var genericSame = adapterHit with { RuleId = "generic-cache" };
        var genericInside = adapterHit with { Path = Path.Combine(_root, "cachedir", "sub"), RuleId = "generic-cache" };
        var genericOther = adapterHit with { Path = Path.Combine(_root, "other"), RuleId = "generic-cache" };

        IReadOnlyList<Recommendation> merged = AdapterEngine.Merge([adapterHit], [genericSame, genericInside, genericOther]);

        Assert.Equal(2, merged.Count);
        Assert.Contains(merged, r => r.RuleId == "chrome/http-cache");
        Assert.Contains(merged, r => r.Path.EndsWith("other"));
    }

    [Fact]
    public void BaselineAdapterPack_PassesSchemaValidation()
    {
        AdapterPack pack = BaselineAdapterPack.Load();

        Assert.True(pack.Adapters.Count >= 10);
        string[] ids = pack.Adapters.Select(a => a.Id).ToArray();
        Assert.Contains("wechat", ids);
        Assert.Contains("telegram", ids);
        Assert.Contains("gradle", ids);
        Assert.Contains("maven", ids);
        Assert.Contains("steam", ids);
    }

    [Fact]
    public void WeChat_DefaultDocumentsPath_EnumeratesPerAccountCaches()
    {
        Write(@"WeChat Files\wxid_alpha\FileStorage\Cache\blob.bin");
        Write(@"WeChat Files\wxid_beta\FileStorage\Cache\blob.bin");
        var env = new EnvironmentModel
        {
            Symbols = new Dictionary<string, string> { ["DOCUMENTS"] = _root },
            InstalledApps = [],
            RunningProcesses = new HashSet<string>(),
            Volumes = [],
        };

        IReadOnlyList<Recommendation> hits = new AdapterEngine(env, BaselineAdapterPack.Load(), Index()).Evaluate()
            .Where(r => r.RuleId == "wechat/file-cache").ToList();

        Assert.Equal(2, hits.Count);
        Assert.All(hits, h => Assert.Equal("quarantine", h.Action));
    }

    [Fact]
    public void WeChat_Running_ChatMediaIsReportOnly()
    {
        Write(@"WeChat Files\wxid_alpha\FileStorage\Image\img.dat");
        var env = new EnvironmentModel
        {
            Symbols = new Dictionary<string, string> { ["DOCUMENTS"] = _root },
            InstalledApps = [],
            RunningProcesses = new HashSet<string> { "wechat" },
            Volumes = [],
        };

        IReadOnlyList<Recommendation> hits = new AdapterEngine(env, BaselineAdapterPack.Load(), Index()).Evaluate()
            .Where(r => r.RuleId == "wechat/chat-media").ToList();

        Assert.Equal("report-only", Assert.Single(hits).Action);
    }
}
