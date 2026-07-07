using System.Diagnostics;
using System.IO.Pipes;
using CCZen.Engine.Index;
using CCZen.Engine.Rules;
using CCZen.Engine.Safety;
using CCZen.Engine.Scanning;
using CCZen.Engine.Service;
using StreamJsonRpc;

if (args.Length < 1 || args[0] is "-h" or "--help")
{
    Console.WriteLine("CCZen M0 POC — NTFS fast scan (spec: docs/specs/01-scan-engine.md)");
    Console.WriteLine("Usage: cczen <root> [--top N] [--mode auto|usn|fallback|client] [--cache <file>]");
    Console.WriteLine("       cczen recommend                 # 规则引擎清理推荐 (specs/02)");
    Console.WriteLine("       cczen clean [--yes]             # T0/T1 推荐项 → 隔离区 (specs/04)");
    Console.WriteLine("       cczen restore <卷> <batchId>    # 按批次还原隔离区");
    Console.WriteLine("       cczen batches [卷]              # 列出隔离区批次");
    Console.WriteLine("Example: cczen C: --top 20");
    Console.WriteLine("        --mode client queries a running CCZen.Service over \\\\.\\pipe\\cczen-engine");
    return 1;
}

if (args[0] == "recommend")
{
    return RunRecommend();
}

if (args[0] == "clean")
{
    return RunClean(autoConfirm: args.Contains("--yes"));
}

if (args[0] == "restore" && args.Length >= 3)
{
    return RunRestore(args[1], args[2]);
}

if (args[0] == "batches")
{
    return RunBatches(args.Length >= 2 ? args[1] : "C:\\");
}

string root = args[0].Length == 2 && args[0][1] == ':' ? args[0] + "\\" : args[0];
int top = 20;
string mode = "auto";
string? cachePath = null;
for (int i = 1; i < args.Length - 1; i++)
{
    if (args[i] == "--top")
    {
        top = int.Parse(args[i + 1]);
    }
    else if (args[i] == "--mode")
    {
        mode = args[i + 1];
    }
    else if (args[i] == "--cache")
    {
        cachePath = args[i + 1];
    }
}

if (mode == "client")
{
    return await RunClientAsync(root, top);
}

IVolumeScanner scanner = mode switch
{
    "usn" => new UsnJournalScanner(),
    "fallback" => new FallbackScanner(),
    _ => VolumeScannerFactory.Create(root),
};

Console.WriteLine($"Scanning {root} using {scanner.GetType().Name} ...");
var stopwatch = Stopwatch.StartNew();
FileSystemIndex index;
bool incremental = false;
if (cachePath is not null && scanner is UsnJournalScanner usn)
{
    (index, incremental) = usn.ScanWithCache(root, cachePath);
}
else
{
    index = scanner.Scan(root);
}

stopwatch.Stop();

string how = incremental ? " (incremental USN catch-up)" : string.Empty;
Console.WriteLine($"Indexed {index.Count:N0} entries ({index.FileCount:N0} files) in {stopwatch.Elapsed.TotalSeconds:F2} s{how}");
Console.WriteLine($"Total logical size: {Format(index.TotalLogicalSize)}, allocated: {Format(index.TotalAllocatedSize)}");

Console.WriteLine($"\nTop {top} files (by allocated size):");
foreach (FileEntry entry in index.TopFiles(top))
{
    Console.WriteLine($"  {Format(entry.AllocatedSize),10}  {entry.Path}");
}

Console.WriteLine($"\nTop {top} directories (by allocated subtree size):");
foreach (FileEntry entry in index.TopDirectories(top))
{
    Console.WriteLine($"  {Format(entry.AllocatedSize),10}  {entry.FileCount,9:N0} files  {entry.Path}");
}

return 0;

static int RunRecommend()
{
    EnvironmentModel environment = EnvironmentDiscovery.Discover();
    var engine = new RuleEngine(environment, BaselineRulePack.Load());
    IReadOnlyList<Recommendation> recommendations = engine.Evaluate();

    foreach (var group in recommendations.GroupBy(r => r.Tier).OrderBy(g => g.Key))
    {
        Console.WriteLine($"\n[{group.Key}] {group.Count()} 项, 共 {Format(group.Sum(r => r.SizeBytes))}");
        foreach (Recommendation r in group.OrderByDescending(r => r.SizeBytes).Take(15))
        {
            Console.WriteLine($"  {Format(r.SizeBytes),10}  {r.Path}");
            Console.WriteLine($"              规则 {r.RuleId} (置信 {r.Confidence:F2}, 动作 {r.Action}): {r.Explain}");
        }
    }

    Console.WriteLine($"\n合计可清理: {Format(recommendations.Sum(r => r.SizeBytes))} ({recommendations.Count} 项)");
    return 0;
}

static int RunClean(bool autoConfirm)
{
    EnvironmentModel environment = EnvironmentDiscovery.Discover();
    IReadOnlyList<Recommendation> recommendations =
        new RuleEngine(environment, BaselineRulePack.Load()).Evaluate();
    var protection = new ProtectedPaths();
    BatchPlan plan = CleanupPlanner.Plan(recommendations, protection);

    if (plan.Items.Count == 0)
    {
        Console.WriteLine("没有可自动清理的 T0/T1 推荐项。");
        return 0;
    }

    long total = plan.Items.Sum(i => i.SizeBytes);
    Console.WriteLine($"批次 {plan.BatchId}: {plan.Items.Count} 项 (文件尺寸合计 {Format(total)}) 将移入隔离区 <卷>\\CCZen.Quarantine\\{plan.BatchId}");
    foreach (PlanItem item in plan.Items.OrderByDescending(i => i.SizeBytes).Take(20))
    {
        Console.WriteLine($"  {Format(item.SizeBytes),10}  {item.Path}");
    }

    if (!autoConfirm)
    {
        Console.Write("确认执行? [y/N] ");
        if (Console.ReadLine()?.Trim().ToLowerInvariant() != "y")
        {
            Console.WriteLine("已取消。");
            return 0;
        }
    }

    var store = new QuarantineStore(protection);
    IReadOnlyList<ItemResult> results = store.Execute(plan);
    foreach (var group in results.GroupBy(r => r.Outcome))
    {
        Console.WriteLine($"  {group.Key}: {group.Count()} 项");
    }

    Console.WriteLine($"还原命令: cczen restore <卷> {plan.BatchId}");
    return 0;
}

static int RunRestore(string volume, string batchId)
{
    string volumeRoot = volume.Length == 2 && volume[1] == ':' ? volume + "\\" : volume;
    var store = new QuarantineStore(new ProtectedPaths());
    IReadOnlyList<ItemResult> results = store.Restore(volumeRoot, batchId);
    foreach (ItemResult result in results)
    {
        Console.WriteLine($"  {result.Outcome}  {result.Path}");
    }

    Console.WriteLine($"已处理 {results.Count} 项。");
    return 0;
}

static int RunBatches(string volume)
{
    string volumeRoot = volume.Length == 2 && volume[1] == ':' ? volume + "\\" : volume;
    string quarantineRoot = Path.Combine(volumeRoot, QuarantineStore.DirectoryName);
    if (!Directory.Exists(quarantineRoot))
    {
        Console.WriteLine("隔离区为空。");
        return 0;
    }

    foreach (string batch in Directory.EnumerateDirectories(quarantineRoot))
    {
        long size = Directory.EnumerateFiles(batch, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);
        Console.WriteLine($"  {Path.GetFileName(batch)}  {Format(size),10}  {Directory.GetLastWriteTime(batch):yyyy-MM-dd HH:mm}");
    }

    return 0;
}

static async Task<int> RunClientAsync(string root, int top)
{
    using var pipe = new NamedPipeClientStream(".", "cczen-engine", PipeDirection.InOut, PipeOptions.Asynchronous);
    try
    {
        await pipe.ConnectAsync(3000);
    }
    catch (TimeoutException)
    {
        Console.Error.WriteLine("Cannot reach CCZen.Service on \\\\.\\pipe\\cczen-engine. Start it first: dotnet run --project src\\CCZen.Service");
        return 2;
    }

    using var rpc = JsonRpc.Attach(pipe);
    var engine = rpc.Attach<IEngineRpc>();

    ScanSummary summary = await engine.ScanAsync(root, useCache: true, CancellationToken.None);
    string how = summary.Incremental ? " (incremental USN catch-up)" : string.Empty;
    Console.WriteLine($"Indexed {summary.EntryCount:N0} entries ({summary.FileCount:N0} files) in {summary.ElapsedSeconds:F2} s{how}");
    Console.WriteLine($"Total logical size: {Format(summary.TotalLogicalSize)}, allocated: {Format(summary.TotalAllocatedSize)}");

    Console.WriteLine($"\nTop {top} files (by allocated size):");
    foreach (FileEntry entry in await engine.GetTopFilesAsync(top, CancellationToken.None))
    {
        Console.WriteLine($"  {Format(entry.AllocatedSize),10}  {entry.Path}");
    }

    Console.WriteLine($"\nTop {top} directories (by allocated subtree size):");
    foreach (FileEntry entry in await engine.GetTopDirectoriesAsync(top, CancellationToken.None))
    {
        Console.WriteLine($"  {Format(entry.AllocatedSize),10}  {entry.FileCount,9:N0} files  {entry.Path}");
    }

    return 0;
}

static string Format(long bytes) => bytes switch
{
    >= 1L << 40 => $"{bytes / (double)(1L << 40):F2} TB",
    >= 1L << 30 => $"{bytes / (double)(1L << 30):F2} GB",
    >= 1L << 20 => $"{bytes / (double)(1L << 20):F2} MB",
    >= 1L << 10 => $"{bytes / (double)(1L << 10):F2} KB",
    _ => $"{bytes} B",
};
