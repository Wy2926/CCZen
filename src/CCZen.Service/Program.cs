using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using CCZen.Engine.Service;
using StreamJsonRpc;

// CCZen engine query service (spec: 05 进程模型). Serves IEngineRpc over a
// named pipe; one JSON-RPC connection at a time, loops until Ctrl+C.
const string PipeName = "cczen-engine";

string cacheDirectory = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CCZen", "cache");
var server = new EngineRpcServer(cacheDirectory);

using var shutdown = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    shutdown.Cancel();
};

Console.WriteLine($"CCZen engine service listening on \\\\.\\pipe\\{PipeName} (Ctrl+C to stop)");

while (!shutdown.IsCancellationRequested)
{
    // Restrict the pipe to the current user (SAFE-FR-041).
    var security = new PipeSecurity();
    security.AddAccessRule(new PipeAccessRule(
        WindowsIdentity.GetCurrent().User!, PipeAccessRights.ReadWrite, AccessControlType.Allow));

    using NamedPipeServerStream pipe = NamedPipeServerStreamAcl.Create(
        PipeName,
        PipeDirection.InOut,
        maxNumberOfServerInstances: 1,
        PipeTransmissionMode.Byte,
        PipeOptions.Asynchronous,
        inBufferSize: 0,
        outBufferSize: 0,
        security);

    try
    {
        await pipe.WaitForConnectionAsync(shutdown.Token);
    }
    catch (OperationCanceledException)
    {
        break;
    }

    Console.WriteLine("Client connected.");
    using var rpc = JsonRpc.Attach(pipe, server);
    try
    {
        await rpc.Completion.WaitAsync(shutdown.Token);
    }
    catch (Exception ex) when (ex is OperationCanceledException or ConnectionLostException or IOException)
    {
        // client disconnected or shutdown requested
    }

    Console.WriteLine("Client disconnected.");
}

return 0;
