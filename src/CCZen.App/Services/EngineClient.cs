using System.IO;
using System.IO.Pipes;
using System.Runtime.Versioning;
using CCZen.Engine.Index;
using CCZen.Engine.Rules;
using CCZen.Engine.Safety;
using CCZen.Engine.Service;
using StreamJsonRpc;

namespace CCZen.App.Services;

/// <summary>
/// Engine access for the UI: prefers the running CCZen.Service named pipe
/// (\\.\pipe\cczen-engine); falls back to an in-process engine when the
/// service is not running so the app remains usable standalone.
/// </summary>
[SupportedOSPlatform("windows10.0.17763")]
public sealed class EngineClient : IEngineClient, IAsyncDisposable
{
    private const string PipeName = "cczen-engine";
    private const int ConnectTimeoutMs = 1500;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private NamedPipeClientStream? _pipe;
    private JsonRpc? _rpc;
    private IEngineRpc? _engine;

    public async Task<ScanSummary?> GetStatusAsync(CancellationToken cancellationToken = default) =>
        await (await GetEngineAsync(cancellationToken)).GetStatusAsync(cancellationToken);

    public async Task<ScanSummary> ScanAsync(string root, CancellationToken cancellationToken = default) =>
        await (await GetEngineAsync(cancellationToken)).ScanAsync(root, useCache: true, cancellationToken);

    public async Task<IReadOnlyList<FileEntry>> SearchAsync(SearchQuery query, CancellationToken cancellationToken = default) =>
        await (await GetEngineAsync(cancellationToken)).SearchAsync(query, cancellationToken);

    public async Task<IReadOnlyList<Recommendation>> RecommendAsync(CancellationToken cancellationToken = default) =>
        await (await GetEngineAsync(cancellationToken)).RecommendAsync(cancellationToken);

    public async Task<BatchPlan> PlanCleanAsync(IReadOnlyList<string>? confirmedT2Paths = null, IReadOnlyList<string>? selectedPaths = null, CancellationToken cancellationToken = default) =>
        await (await GetEngineAsync(cancellationToken)).PlanCleanAsync(confirmedT2Paths, selectedPaths, cancellationToken);

    public async Task<BatchPlan> PlanQuarantineAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken = default) =>
        await (await GetEngineAsync(cancellationToken)).PlanQuarantineAsync(paths, cancellationToken);

    public async Task<IReadOnlyList<ItemResult>> ExecuteBatchAsync(string batchId, CancellationToken cancellationToken = default) =>
        await (await GetEngineAsync(cancellationToken)).ExecuteBatchAsync(batchId, cancellationToken);

    public async Task<IReadOnlyList<ItemResult>> RestoreBatchAsync(string volumeRoot, string batchId, CancellationToken cancellationToken = default) =>
        await (await GetEngineAsync(cancellationToken)).RestoreBatchAsync(volumeRoot, batchId, cancellationToken);

    public async ValueTask DisposeAsync()
    {
        _rpc?.Dispose();
        if (_pipe is not null)
        {
            await _pipe.DisposeAsync();
        }

        _gate.Dispose();
    }

    private async Task<IEngineRpc> GetEngineAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return _engine ??= await ConnectOrCreateAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IEngineRpc> ConnectOrCreateAsync(CancellationToken cancellationToken)
    {
        var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            await pipe.ConnectAsync(ConnectTimeoutMs, cancellationToken);
            _pipe = pipe;
            _rpc = JsonRpc.Attach(pipe);
            return _rpc.Attach<IEngineRpc>();
        }
        catch (Exception ex) when (ex is TimeoutException or IOException)
        {
            await pipe.DisposeAsync();
            return new EngineRpcServer();
        }
    }
}
