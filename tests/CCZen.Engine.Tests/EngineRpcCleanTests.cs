using CCZen.Engine.Rules;
using CCZen.Engine.Safety;
using CCZen.Engine.Service;
using Nerdbank.Streams;
using StreamJsonRpc;

namespace CCZen.Engine.Tests;

public class EngineRpcCleanTests : IDisposable
{
    private readonly string _root;

    public EngineRpcCleanTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cczen-rpcclean-" + Guid.NewGuid().ToString("N"));
        string cache = Path.Combine(_root, "Google", "Chrome", "User Data", "Default", "Cache");
        Directory.CreateDirectory(cache);
        File.WriteAllBytes(Path.Combine(cache, "f_000001"), new byte[4096]);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private EnvironmentModel FakeEnvironment() => new()
    {
        Symbols = new Dictionary<string, string> { ["LOCALAPPDATA"] = _root },
        InstalledApps = [],
        RunningProcesses = new HashSet<string>(),
        Volumes = [],
    };

    private IEngineRpc Connect()
    {
        (Stream serverStream, Stream clientStream) = FullDuplexStream.CreatePair();
        var server = new EngineRpcServer(
            cacheDirectory: null,
            discoverEnvironment: FakeEnvironment,
            protection: new ProtectedPaths([], windir: null));
        JsonRpc.Attach(serverStream, server);
        return JsonRpc.Attach(clientStream).Attach<IEngineRpc>();
    }

    [Fact]
    public async Task RecommendPlanExecuteRestore_RoundTripsOverRpc()
    {
        IEngineRpc client = Connect();

        IReadOnlyList<Recommendation> recommendations = await client.RecommendAsync(CancellationToken.None);
        Assert.Contains(recommendations, r => r.RuleId == "chrome/http-cache");

        BatchPlan plan = await client.PlanCleanAsync(null, CancellationToken.None);
        Assert.NotEmpty(plan.Items);

        IReadOnlyList<ItemResult> executed = await client.ExecuteBatchAsync(plan.BatchId, CancellationToken.None);
        Assert.Contains(executed, r => r.Outcome == ItemOutcome.Quarantined);

        IReadOnlyList<BatchInfo> batches = await client.ListBatchesAsync(Path.GetPathRoot(_root)!, CancellationToken.None);
        Assert.Contains(batches, b => b.BatchId == plan.BatchId);

        IReadOnlyList<ItemResult> restored = await client.RestoreBatchAsync(
            Path.GetPathRoot(_root)!, plan.BatchId, CancellationToken.None);
        Assert.All(restored, r => Assert.Equal(ItemOutcome.Quarantined, r.Outcome));
        Assert.True(Directory.Exists(Path.Combine(_root, "Google", "Chrome", "User Data", "Default", "Cache")));
    }

    [Fact]
    public async Task ExecuteBatch_UnknownOrReplayedId_SurfacesRemoteError()
    {
        IEngineRpc client = Connect();

        await Assert.ThrowsAsync<RemoteInvocationException>(
            () => client.ExecuteBatchAsync("no-such-batch", CancellationToken.None));

        BatchPlan plan = await client.PlanCleanAsync(null, CancellationToken.None);
        await client.ExecuteBatchAsync(plan.BatchId, CancellationToken.None);

        // SAFE-FR-010: a confirmed plan snapshot executes exactly once.
        await Assert.ThrowsAsync<RemoteInvocationException>(
            () => client.ExecuteBatchAsync(plan.BatchId, CancellationToken.None));

        await client.RestoreBatchAsync(Path.GetPathRoot(_root)!, plan.BatchId, CancellationToken.None);
    }
}
