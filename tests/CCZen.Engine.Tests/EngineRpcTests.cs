using CCZen.Engine.Index;
using CCZen.Engine.Service;
using Nerdbank.Streams;
using StreamJsonRpc;

namespace CCZen.Engine.Tests;

public class EngineRpcTests : IDisposable
{
    private readonly string _root;

    public EngineRpcTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cczen-rpc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "sub"));
        File.WriteAllBytes(Path.Combine(_root, "big.bin"), new byte[5000]);
        File.WriteAllBytes(Path.Combine(_root, "sub", "small.bin"), new byte[100]);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static (IEngineRpc Client, JsonRpc ServerRpc) Connect()
    {
        (System.IO.Stream serverStream, System.IO.Stream clientStream) = FullDuplexStream.CreatePair();
        JsonRpc serverRpc = JsonRpc.Attach(serverStream, new EngineRpcServer());
        var clientRpc = JsonRpc.Attach(clientStream);
        return (clientRpc.Attach<IEngineRpc>(), serverRpc);
    }

    [Fact]
    public async Task ScanThenQuery_ReturnsTopEntriesOverRpc()
    {
        (IEngineRpc client, JsonRpc _) = Connect();

        ScanSummary summary = await client.ScanAsync(_root, useCache: false, CancellationToken.None);

        Assert.Equal(2, summary.FileCount);
        Assert.Equal(5100, summary.TotalLogicalSize);
        Assert.False(summary.Incremental);

        IReadOnlyList<FileEntry> files = await client.GetTopFilesAsync(10, CancellationToken.None);
        Assert.Equal(2, files.Count);
        Assert.EndsWith("big.bin", files[0].Path);

        IReadOnlyList<FileEntry> dirs = await client.GetTopDirectoriesAsync(1, CancellationToken.None);
        Assert.Single(dirs);
        Assert.Equal(2, dirs[0].FileCount);
    }

    [Fact]
    public async Task GetStatus_BeforeScan_ReturnsNull()
    {
        (IEngineRpc client, JsonRpc _) = Connect();

        Assert.Null(await client.GetStatusAsync(CancellationToken.None));
    }

    [Fact]
    public async Task QueryWithoutScan_SurfacesRemoteError()
    {
        (IEngineRpc client, JsonRpc _) = Connect();

        await Assert.ThrowsAsync<RemoteInvocationException>(
            () => client.GetTopFilesAsync(5, CancellationToken.None));
    }

    [Fact]
    public async Task GetStatus_AfterScan_ReturnsLastSummary()
    {
        (IEngineRpc client, JsonRpc _) = Connect();
        await client.ScanAsync(_root, useCache: false, CancellationToken.None);

        ScanSummary? status = await client.GetStatusAsync(CancellationToken.None);

        Assert.NotNull(status);
        Assert.Equal(2, status.FileCount);
    }

    [Fact]
    public async Task ParallelSearch_AfterScan_DoesNotThrow()
    {
        (IEngineRpc client, JsonRpc _) = Connect();
        await client.ScanAsync(_root, useCache: false, CancellationToken.None);
        var query = new SearchQuery(SearchKind.Files, MinSizeBytes: 0, NameContains: null, MaxResults: 10);

        Task<IReadOnlyList<FileEntry>>[] searches =
            Enumerable.Range(0, 8)
                .Select(_ => client.SearchAsync(query, CancellationToken.None))
                .ToArray();

        IReadOnlyList<FileEntry>[] results = await Task.WhenAll(searches);

        Assert.All(results, r => Assert.Equal(2, r.Count));
    }
}
