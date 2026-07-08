using CCZen.Engine.Rules;
using CCZen.Engine.Safety;
using CCZen.Engine.Service;
using Microsoft.VisualStudio.Threading;
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
            protection: new ProtectedPaths([], windir: null),
            getScanRoot: () => _root);
        JsonRpc.Attach(serverStream, server);
        return JsonRpc.Attach(clientStream).Attach<IEngineRpc>();
    }

    [Fact]
    public async Task RecommendPlanExecuteRestore_RoundTripsOverRpc()
    {
        IEngineRpc client = Connect();

        IReadOnlyList<Recommendation> recommendations = await client.RecommendAsync(CancellationToken.None);
        Assert.Contains(recommendations, r => r.RuleId == "chrome/http-cache");

        BatchPlan plan = await client.PlanCleanAsync(null, null, CancellationToken.None);
        Assert.NotEmpty(plan.Items);

        IReadOnlyList<ItemResult> executed = await client.ExecuteBatchAsync(plan.BatchId, false, null, CancellationToken.None);
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
            () => client.ExecuteBatchAsync("no-such-batch", false, null, CancellationToken.None));

        BatchPlan plan = await client.PlanCleanAsync(null, null, CancellationToken.None);
        await client.ExecuteBatchAsync(plan.BatchId, false, null, CancellationToken.None);

        // SAFE-FR-010: a confirmed plan snapshot executes exactly once.
        await Assert.ThrowsAsync<RemoteInvocationException>(
            () => client.ExecuteBatchAsync(plan.BatchId, false, null, CancellationToken.None));

        await client.RestoreBatchAsync(Path.GetPathRoot(_root)!, plan.BatchId, CancellationToken.None);
    }

    [Fact]
    public async Task PlanClean_WithSelectedPaths_LimitsPlanToSelection()
    {
        IEngineRpc client = Connect();

        IReadOnlyList<Recommendation> recommendations = await client.RecommendAsync(CancellationToken.None);
        Assert.NotEmpty(recommendations);

        BatchPlan empty = await client.PlanCleanAsync(null, [], CancellationToken.None);
        Assert.Empty(empty.Items);

        string picked = recommendations[0].Path;
        BatchPlan plan = await client.PlanCleanAsync(null, [picked], CancellationToken.None);
        Assert.All(plan.Items, i => Assert.Equal(picked, i.Path));
    }

    [Fact]
    public async Task PlanQuarantineExecuteRestore_RoundTripsOverRpc()
    {
        IEngineRpc client = Connect();
        string big = Path.Combine(_root, "big.bin");
        File.WriteAllBytes(big, new byte[8192]);

        BatchPlan plan = await client.PlanQuarantineAsync([big], CancellationToken.None);
        Assert.Single(plan.Items);
        Assert.Equal(CleanupPlanner.ManualRuleId, plan.Items[0].RuleId);

        IReadOnlyList<ItemResult> executed = await client.ExecuteBatchAsync(plan.BatchId, false, null, CancellationToken.None);
        Assert.All(executed, r => Assert.Equal(ItemOutcome.Quarantined, r.Outcome));
        Assert.False(File.Exists(big));

        await client.RestoreBatchAsync(Path.GetPathRoot(_root)!, plan.BatchId, CancellationToken.None);
        Assert.True(File.Exists(big));
    }

    [Fact]
    public async Task ExecuteBatch_WithPermanentDelete_DeletesWithoutQuarantine()
    {
        IEngineRpc client = Connect();
        string big = Path.Combine(_root, "delete-me.bin");
        File.WriteAllBytes(big, new byte[8192]);

        BatchPlan plan = await client.PlanQuarantineAsync([big], CancellationToken.None);
        IReadOnlyList<ItemResult> executed = await client.ExecuteBatchAsync(plan.BatchId, true, null, CancellationToken.None);

        Assert.All(executed, r => Assert.Equal(ItemOutcome.Deleted, r.Outcome));
        Assert.False(File.Exists(big));

        // Nothing was quarantined, so the batch is not restorable.
        IReadOnlyList<ItemResult> restored = await client.RestoreBatchAsync(
            Path.GetPathRoot(_root)!, plan.BatchId, CancellationToken.None);
        Assert.DoesNotContain(restored, r => r.Outcome == ItemOutcome.Quarantined);
        Assert.False(File.Exists(big));
    }

    [Fact]
    public async Task PurgeBatch_RemovesQuarantinedBatchPermanently()
    {
        IEngineRpc client = Connect();
        string big = Path.Combine(_root, "purge-me.bin");
        File.WriteAllBytes(big, new byte[8192]);

        BatchPlan plan = await client.PlanQuarantineAsync([big], CancellationToken.None);
        await client.ExecuteBatchAsync(plan.BatchId, false, null, CancellationToken.None);

        string volumeRoot = Path.GetPathRoot(_root)!;
        Assert.True(await client.PurgeBatchAsync(volumeRoot, plan.BatchId, CancellationToken.None));

        IReadOnlyList<BatchInfo> batches = await client.ListBatchesAsync(volumeRoot, CancellationToken.None);
        Assert.DoesNotContain(batches, b => b.BatchId == plan.BatchId);
        Assert.False(await client.PurgeBatchAsync(volumeRoot, plan.BatchId, CancellationToken.None));
    }

    [Fact]
    public async Task ExecuteBatch_ReportsPerItemProgress()
    {
        IEngineRpc client = Connect();
        string a = Path.Combine(_root, "a.bin");
        string b = Path.Combine(_root, "b.bin");
        File.WriteAllBytes(a, new byte[1024]);
        File.WriteAllBytes(b, new byte[1024]);

        BatchPlan plan = await client.PlanQuarantineAsync([a, b], CancellationToken.None);
        var reports = new List<ExecuteProgress>();
        var progress = new ProgressWithCompletion<ExecuteProgress>(reports.Add);

        await client.ExecuteBatchAsync(plan.BatchId, false, progress, CancellationToken.None);
        await progress.WaitAsync(CancellationToken.None);

        Assert.Equal(2, reports.Count);
        Assert.Equal(2, reports[^1].Done);
        Assert.Equal(2, reports[^1].Total);

        await client.RestoreBatchAsync(Path.GetPathRoot(_root)!, plan.BatchId, CancellationToken.None);
    }
}
