using System.Diagnostics;
using CCZen.Engine.Index;
using CCZen.Engine.Scanning;

if (args.Length < 1 || args[0] is "-h" or "--help")
{
    Console.WriteLine("CCZen M0 POC — NTFS fast scan (spec: docs/specs/01-scan-engine.md)");
    Console.WriteLine("Usage: cczen <root> [--top N] [--mode auto|usn|fallback] [--cache <file>]");
    Console.WriteLine("Example: cczen C: --top 20");
    return 1;
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

static string Format(long bytes) => bytes switch
{
    >= 1L << 40 => $"{bytes / (double)(1L << 40):F2} TB",
    >= 1L << 30 => $"{bytes / (double)(1L << 30):F2} GB",
    >= 1L << 20 => $"{bytes / (double)(1L << 20):F2} MB",
    >= 1L << 10 => $"{bytes / (double)(1L << 10):F2} KB",
    _ => $"{bytes} B",
};
