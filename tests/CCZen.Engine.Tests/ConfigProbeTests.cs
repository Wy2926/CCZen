using CCZen.Engine.Rules;

namespace CCZen.Engine.Tests;

public class ConfigProbeTests : IDisposable
{
    private readonly string _root;

    public ConfigProbeTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cczen-probe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private void Write(string relative, string content)
    {
        string path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private void WriteBytes(string relative, int bytes = 100)
    {
        string path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[bytes]);
    }

    private EnvironmentModel Env(params InstalledApp[] apps) => new()
    {
        Symbols = new Dictionary<string, string> { ["APPDATA"] = _root },
        InstalledApps = apps,
        RunningProcesses = new HashSet<string>(),
        Volumes = [],
    };

    [Fact]
    public void IniProbe_ExtractsValue_BySectionAndKey()
    {
        Write("app\\config.ini", "[General]\r\nOther=x\r\nDataPath=D:\\MyData\\App\r\n");
        var probe = new ConfigProbe
        {
            Symbol = "APP_DATA_DIR",
            Kind = "iniValue",
            Source = "${APPDATA}\\app\\config.ini",
            IniSection = "General",
            IniKey = "DataPath",
        };

        Assert.Equal("D:\\MyData\\App", ConfigProbeReader.Read(probe, Env()));
    }

    [Fact]
    public void JsonProbe_ExtractsNestedValue()
    {
        Write("app\\settings.json", """{"storage":{"dataDir":"E:\\Moved\\App"}}""");
        var probe = new ConfigProbe
        {
            Symbol = "APP_DATA_DIR",
            Kind = "jsonValue",
            Source = "${APPDATA}\\app\\settings.json",
            JsonPath = "storage.dataDir",
        };

        Assert.Equal("E:\\Moved\\App", ConfigProbeReader.Read(probe, Env()));
    }

    [Fact]
    public void Probe_MissingSourceOrKey_ReturnsNull()
    {
        var missingFile = new ConfigProbe
        {
            Symbol = "S", Kind = "iniValue", Source = "${APPDATA}\\nope.ini", IniKey = "K",
        };
        Write("app\\empty.json", "{}");
        var missingProperty = new ConfigProbe
        {
            Symbol = "S", Kind = "jsonValue", Source = "${APPDATA}\\app\\empty.json", JsonPath = "a.b",
        };

        Assert.Null(ConfigProbeReader.Read(missingFile, Env()));
        Assert.Null(ConfigProbeReader.Read(missingProperty, Env()));
    }

    private static AdapterPack ProbedPack(string verifiedVersions = "") => AdapterPack.Load($$"""
        {
          "schemaVersion": 1,
          "adapters": [
            {
              "id": "app",
              "name": "App",
              "category": "im",
              {{(verifiedVersions.Length > 0 ? $"\"verifiedVersions\": \"{verifiedVersions}\"," : "")}}
              "detect": {
                "uninstallNamePatterns": ["My App"],
                "pathPatterns": ["${APPDATA}\\app"],
                "configProbes": [
                  {
                    "symbol": "APP_CACHE_DIR",
                    "kind": "jsonValue",
                    "source": "${APPDATA}\\app\\settings.json",
                    "jsonPath": "storage.cacheDir"
                  }
                ]
              },
              "items": [
                {
                  "id": "cache",
                  "tier": "T1",
                  "targets": ["${APP_CACHE_DIR}\\Cache"],
                  "explain": "migrated cache"
                }
              ]
            }
          ]
        }
        """);

    [Fact]
    public void ProbedSymbol_ResolvesMigratedDataDir_InItemTargets()
    {
        WriteBytes("moved\\Cache\\blob.bin");
        string moved = Path.Combine(_root, "moved").Replace("\\", "\\\\");
        Write("app\\settings.json", "{\"storage\":{\"cacheDir\":\"" + moved + "\"}}");

        IReadOnlyList<Recommendation> hits = new AdapterEngine(Env(), ProbedPack()).Evaluate();

        Recommendation hit = Assert.Single(hits);
        Assert.Equal(Path.Combine(_root, "moved", "Cache"), hit.Path);
        Assert.Equal(Tier.T1, hit.Tier);
    }

    [Fact]
    public void ProbeFails_ItemWithUnboundSymbol_ProducesNothing()
    {
        Write("app\\settings.json", "{}");
        WriteBytes("app\\anything.bin");

        Assert.Empty(new AdapterEngine(Env(), ProbedPack()).Evaluate());
    }

    [Fact]
    public void VersionOutsideVerifiedRange_DemotesOneTier()
    {
        WriteBytes("moved\\Cache\\blob.bin");
        string moved = Path.Combine(_root, "moved").Replace("\\", "\\\\");
        Write("app\\settings.json", "{\"storage\":{\"cacheDir\":\"" + moved + "\"}}");
        var newerApp = new InstalledApp("My App", "9.9", null, null);

        IReadOnlyList<Recommendation> hits =
            new AdapterEngine(Env(newerApp), ProbedPack("1.0-3.9")).Evaluate();

        Assert.Equal(Tier.T2, Assert.Single(hits).Tier);
    }

    [Fact]
    public void VersionInsideVerifiedRange_KeepsTier()
    {
        WriteBytes("moved\\Cache\\blob.bin");
        string moved = Path.Combine(_root, "moved").Replace("\\", "\\\\");
        Write("app\\settings.json", "{\"storage\":{\"cacheDir\":\"" + moved + "\"}}");
        var knownApp = new InstalledApp("My App", "2.5", null, null);

        IReadOnlyList<Recommendation> hits =
            new AdapterEngine(Env(knownApp), ProbedPack("1.0-3.9")).Evaluate();

        Assert.Equal(Tier.T1, Assert.Single(hits).Tier);
    }
}
