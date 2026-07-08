using System.Diagnostics;
using CCZen.Engine.Index;

namespace CCZen.Engine.Tests;

/// <summary>
/// Smoke performance gate for in-memory index queries (AC-B05 precursor).
/// Full 4M-node BenchmarkDotNet suite is deferred to CI nightly.
/// </summary>
public class IndexQueryPerformanceTests
{
    [Fact]
    public void SubtreeStats_On100kNodes_CompletesUnder100ms()
    {
        FileSystemIndex index = BuildWideTree(appFolders: 100, filesPerApp: 1000);
        IndexQuery query = new(index);
        Assert.True(query.TryResolvePrefix("C:\\", out int rootNode));

        var stopwatch = Stopwatch.StartNew();
        for (int i = 0; i < 20; i++)
        {
            SubtreeStats stats = query.GetSubtreeStats(rootNode);
            Assert.True(stats.FileCount > 0);
        }

        stopwatch.Stop();
        double perQueryMs = stopwatch.Elapsed.TotalMilliseconds / 20.0;
        Assert.True(perQueryMs < 100.0, $"GetSubtreeStats averaged {perQueryMs:0.00} ms (budget 100 ms)");
    }

    [Fact]
    public void FindFilesByExtension_On100kNodes_CompletesUnder100ms()
    {
        FileSystemIndex index = BuildWideTree(appFolders: 100, filesPerApp: 1000);
        IndexQuery query = new(index);
        Assert.True(query.TryResolvePrefix("C:\\", out int rootNode));
        var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".log", ".tmp" };

        var stopwatch = Stopwatch.StartNew();
        int hits = query.FindFilesByExtension(rootNode, extensions, recursive: true).Count();
        stopwatch.Stop();

        Assert.True(hits > 0);
        Assert.True(stopwatch.Elapsed.TotalMilliseconds < 100.0,
            $"FindFilesByExtension took {stopwatch.Elapsed.TotalMilliseconds:0.00} ms (budget 100 ms)");
    }

    private static FileSystemIndex BuildWideTree(int appFolders, int filesPerApp)
    {
        var builder = new IndexBuilder(rootFrn: 1);
        builder.AddEntry(2, 1, "Users", isDirectory: true);
        builder.AddEntry(3, 2, "Wy", isDirectory: true);
        builder.AddEntry(4, 3, "AppData", isDirectory: true);
        builder.AddEntry(5, 4, "Local", isDirectory: true);

        ulong nextId = 6;
        for (int app = 0; app < appFolders; app++)
        {
            ulong appFrn = nextId++;
            builder.AddEntry(appFrn, 5, $"App{app}", isDirectory: true);
            int appNode = builder.Count - 1;

            for (int file = 0; file < filesPerApp; file++)
            {
                ulong fileFrn = nextId++;
                string name = file % 50 == 0 ? $"trace{file}.log" : $"blob{file}.bin";
                builder.AddEntry(fileFrn, appFrn, name, isDirectory: false);
                int fileNode = builder.Count - 1;
                builder.SetSizes(fileNode, 1024, 4096);
            }

            _ = appNode;
        }

        return builder.Build("C:\\");
    }
}
