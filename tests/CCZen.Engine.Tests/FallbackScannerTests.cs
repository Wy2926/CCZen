using CCZen.Engine.Index;
using CCZen.Engine.Scanning;

namespace CCZen.Engine.Tests;

public class FallbackScannerTests : IDisposable
{
    private readonly string _root;

    public FallbackScannerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cczen-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "dir1", "sub"));
        Directory.CreateDirectory(Path.Combine(_root, "dir2"));
        File.WriteAllBytes(Path.Combine(_root, "big.bin"), new byte[10_000]);
        File.WriteAllBytes(Path.Combine(_root, "dir1", "a.txt"), new byte[100]);
        File.WriteAllBytes(Path.Combine(_root, "dir1", "sub", "b.txt"), new byte[200]);
        File.WriteAllBytes(Path.Combine(_root, "dir2", "c.txt"), new byte[50]);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Scan_IndexesAllFilesWithSizes()
    {
        FileSystemIndex index = new FallbackScanner().Scan(_root);

        Assert.Equal(4, index.FileCount);
        Assert.Equal(10_350, index.TotalLogicalSize);
    }

    [Fact]
    public void Scan_TopFilesMatchesLargestFile()
    {
        FileSystemIndex index = new FallbackScanner().Scan(_root);
        var top = index.TopFiles(1);

        Assert.Single(top);
        Assert.EndsWith("big.bin", top[0].Path);
        Assert.Equal(10_000, top[0].LogicalSize);
    }

    [Fact]
    public void Scan_DirectoryAggregatesSubtree()
    {
        FileSystemIndex index = new FallbackScanner().Scan(_root);
        FileEntry dir1 = index.TopDirectories(10).First(e => e.Path.EndsWith("dir1"));

        Assert.Equal(300, dir1.LogicalSize);
        Assert.Equal(2, dir1.FileCount);
    }

    [Fact]
    public void Scan_AllocatedSizeRoundsUpToCluster()
    {
        FileSystemIndex index = new FallbackScanner(clusterSize: 4096).Scan(_root);
        FileEntry big = index.TopFiles(1)[0];

        Assert.Equal(12_288, big.AllocatedSize); // 10,000 -> 3 clusters
    }
}
