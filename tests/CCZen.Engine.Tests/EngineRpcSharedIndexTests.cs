using CCZen.Engine.Index;
using CCZen.Engine.Scanning;
using CCZen.Engine.Service;
using Nerdbank.Streams;
using StreamJsonRpc;

namespace CCZen.Engine.Tests;

/// <summary>AC-B01: production rule/adapter code must not use directory enumeration.</summary>
public class RulesNoDirectoryWalkTests
{
    [Fact]
    public void RuleAndAdapterSources_DoNotEnumerateDirectories()
    {
        string rulesDir = Path.Combine(FindRepoRoot(), "src", "CCZen.Engine", "Rules");
        foreach (string file in Directory.EnumerateFiles(rulesDir, "*.cs"))
        {
            string content = File.ReadAllText(file);
            Assert.DoesNotContain("Directory.EnumerateFiles", content);
            Assert.DoesNotContain("Directory.EnumerateDirectories", content);
        }
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "CCZen.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate CCZen.sln from test output directory.");
    }
}

/// <summary>AC-B03: Recommend then Search reuses the same in-memory index (one scan).</summary>
public class EngineRpcSharedIndexTests : IDisposable
{
    private readonly string _root;
    private readonly CountingScanner _scanner;

    public EngineRpcSharedIndexTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cczen-shared-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "sub"));
        File.WriteAllBytes(Path.Combine(_root, "big.bin"), new byte[5000]);
        File.WriteAllBytes(Path.Combine(_root, "sub", "small.bin"), new byte[100]);
        _scanner = new CountingScanner(new FallbackScanner());
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private IEngineRpc Connect()
    {
        (Stream serverStream, Stream clientStream) = FullDuplexStream.CreatePair();
        var server = new EngineRpcServer(
            cacheDirectory: null,
            getScanRoot: () => _root,
            createScanner: _ => _scanner);
        JsonRpc.Attach(serverStream, server);
        return JsonRpc.Attach(clientStream).Attach<IEngineRpc>();
    }

    [Fact]
    public async Task RecommendThenSearch_UsesSingleScan()
    {
        IEngineRpc client = Connect();

        await client.RecommendAsync(CancellationToken.None);
        Assert.Equal(1, _scanner.ScanCount);

        var query = new SearchQuery(SearchKind.Files, MinSizeBytes: 0, NameContains: null, MaxResults: 10);
        IReadOnlyList<FileEntry> hits = await client.SearchAsync(query, CancellationToken.None);

        Assert.Equal(2, hits.Count);
        Assert.Equal(1, _scanner.ScanCount);
    }

    [Fact]
    public async Task RecommendThenParallelSearch_StillSingleScan()
    {
        IEngineRpc client = Connect();
        await client.RecommendAsync(CancellationToken.None);

        var query = new SearchQuery(SearchKind.Files, MinSizeBytes: 0, NameContains: null, MaxResults: 10);
        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => client.SearchAsync(query, CancellationToken.None)));

        Assert.Equal(1, _scanner.ScanCount);
    }

    private sealed class CountingScanner(IVolumeScanner inner) : IVolumeScanner
    {
        public int ScanCount { get; private set; }

        public FileSystemIndex Scan(string root, CancellationToken cancellationToken = default)
        {
            ScanCount++;
            return inner.Scan(root, cancellationToken);
        }
    }
}
