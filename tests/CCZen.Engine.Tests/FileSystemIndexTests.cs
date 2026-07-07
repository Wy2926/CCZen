using CCZen.Engine.Index;

namespace CCZen.Engine.Tests;

public class FileSystemIndexTests
{
    private static FileSystemIndex BuildSample()
    {
        // C:\
        //   big.bin (1000/1024)
        //   dir1\
        //     a.txt (100/512)
        //     sub\
        //       b.txt (200/512)
        //   dir2\
        //     c.txt (50/512)
        var builder = new IndexBuilder(rootFrn: 5);
        builder.AddEntry(10, 5, "big.bin", isDirectory: false);
        builder.SetSizes(builder.Count - 1, 1000, 1024);
        builder.AddEntry(11, 5, "dir1", isDirectory: true);
        builder.AddEntry(12, 11, "a.txt", isDirectory: false);
        builder.SetSizes(builder.Count - 1, 100, 512);
        builder.AddEntry(13, 11, "sub", isDirectory: true);
        builder.AddEntry(14, 13, "b.txt", isDirectory: false);
        builder.SetSizes(builder.Count - 1, 200, 512);
        builder.AddEntry(15, 5, "dir2", isDirectory: true);
        builder.AddEntry(16, 15, "c.txt", isDirectory: false);
        builder.SetSizes(builder.Count - 1, 50, 512);
        return builder.Build("C:\\");
    }

    [Fact]
    public void Totals_AreAggregated()
    {
        FileSystemIndex index = BuildSample();

        Assert.Equal(4, index.FileCount);
        Assert.Equal(1350, index.TotalLogicalSize);
        Assert.Equal(2560, index.TotalAllocatedSize);
    }

    [Fact]
    public void TopFiles_ReturnsLargestFirst()
    {
        var top = BuildSample().TopFiles(2);

        Assert.Equal(2, top.Count);
        Assert.Equal("C:\\big.bin", top[0].Path);
        Assert.Equal(1024, top[0].AllocatedSize);
    }

    [Fact]
    public void TopDirectories_AggregateSubtrees()
    {
        var top = BuildSample().TopDirectories(3);

        // dir1 subtree = a.txt + b.txt = 1024 allocated, 2 files.
        FileEntry dir1 = top.First(e => e.Path == "C:\\dir1");
        Assert.Equal(1024, dir1.AllocatedSize);
        Assert.Equal(300, dir1.LogicalSize);
        Assert.Equal(2, dir1.FileCount);
    }

    [Fact]
    public void GetPath_ReconstructsNestedPaths()
    {
        var top = BuildSample().TopFiles(4);

        Assert.Contains(top, e => e.Path == "C:\\dir1\\sub\\b.txt");
    }

    [Fact]
    public void OrphanedEntries_AttachToSyntheticNode()
    {
        var builder = new IndexBuilder(rootFrn: 5);
        builder.AddEntry(20, 999, "lost.txt", isDirectory: false); // parent FRN never registered
        builder.SetSizes(builder.Count - 1, 10, 512);
        FileSystemIndex index = builder.Build("C:\\");

        Assert.Equal(1, index.FileCount);
        Assert.Equal(512, index.TotalAllocatedSize);
        Assert.Contains(index.TopFiles(1), e => e.Path.Contains("<orphaned>"));
    }

    [Fact]
    public void FileWithDeletedParent_DoesNotBreakAggregation()
    {
        var builder = new IndexBuilder(rootFrn: 5);
        builder.AddEntry(30, 5, "notadir.bin", isDirectory: false);
        builder.SetSizes(builder.Count - 1, 5, 512);
        builder.AddEntry(31, 30, "child.txt", isDirectory: false); // parent is a file (stale FRN reuse)
        builder.SetSizes(builder.Count - 1, 7, 512);
        FileSystemIndex index = builder.Build("C:\\");

        Assert.Equal(2, index.FileCount);
        Assert.Equal(1024, index.TotalAllocatedSize);
    }
}
