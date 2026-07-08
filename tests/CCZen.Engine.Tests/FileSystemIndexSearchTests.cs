using CCZen.Engine.Index;

namespace CCZen.Engine.Tests;

public class FileSystemIndexSearchTests
{
    private static FileSystemIndex BuildAncestorChain()
    {
        // C:\
        //   Users\                      (chain: dominated by Wy)
        //     Wy\                       (chain: dominated by AppData)
        //       AppData\                (chain: dominated by Local)
        //         Local\                (distinct: Temp 600 vs Docker 500 of 1100)
        //           Temp\  big.tmp (600)
        //           Docker\ disk.vhdx (500)
        //   other.iso (300)
        var builder = new IndexBuilder(rootFrn: 5);
        builder.AddEntry(10, 5, "Users", isDirectory: true);
        builder.AddEntry(11, 10, "Wy", isDirectory: true);
        builder.AddEntry(12, 11, "AppData", isDirectory: true);
        builder.AddEntry(13, 12, "Local", isDirectory: true);
        builder.AddEntry(14, 13, "Temp", isDirectory: true);
        builder.AddEntry(15, 14, "big.tmp", isDirectory: false);
        builder.SetSizes(builder.Count - 1, 600, 600);
        builder.AddEntry(16, 13, "Docker", isDirectory: true);
        builder.AddEntry(17, 16, "disk.vhdx", isDirectory: false);
        builder.SetSizes(builder.Count - 1, 500, 500);
        builder.AddEntry(18, 5, "other.iso", isDirectory: false);
        builder.SetSizes(builder.Count - 1, 300, 300);
        return builder.Build("C:\\");
    }

    [Fact]
    public void TopDistinctDirectories_SuppressesAncestorChains()
    {
        var top = BuildAncestorChain().TopDistinctDirectories(20);
        var paths = top.Select(e => e.Path).ToList();

        // The Users → Wy → AppData chain is dominated by a single child each.
        Assert.DoesNotContain("C:\\Users", paths);
        Assert.DoesNotContain("C:\\Users\\Wy", paths);
        Assert.DoesNotContain("C:\\Users\\Wy\\AppData", paths);

        // Local (600 vs 500 split), Temp and Docker (leaves) are distinct.
        Assert.Contains("C:\\Users\\Wy\\AppData\\Local", paths);
        Assert.Contains("C:\\Users\\Wy\\AppData\\Local\\Temp", paths);
        Assert.Contains("C:\\Users\\Wy\\AppData\\Local\\Docker", paths);
    }

    [Fact]
    public void TopDistinctDirectories_ExcludesRoot()
    {
        var top = BuildAncestorChain().TopDistinctDirectories(20);

        Assert.DoesNotContain(top, e => e.Path == "C:\\");
    }

    [Fact]
    public void Search_Files_AppliesMinSizeFilter()
    {
        var results = BuildAncestorChain().Search(
            new SearchQuery(SearchKind.Files, MinSizeBytes: 400, NameContains: null, MaxResults: 10));

        Assert.Equal(2, results.Count);
        Assert.All(results, e => Assert.False(e.IsDirectory));
        Assert.All(results, e => Assert.True(e.AllocatedSize >= 400));
        Assert.Equal("C:\\Users\\Wy\\AppData\\Local\\Temp\\big.tmp", results[0].Path);
    }

    [Fact]
    public void Search_Files_NameFilterIsCaseInsensitive()
    {
        var results = BuildAncestorChain().Search(
            new SearchQuery(SearchKind.Files, MinSizeBytes: 0, NameContains: ".ISO", MaxResults: 10));

        Assert.Single(results);
        Assert.Equal("C:\\other.iso", results[0].Path);
    }

    [Fact]
    public void Search_Directories_UsesDistinctSemantics()
    {
        var results = BuildAncestorChain().Search(
            new SearchQuery(SearchKind.Directories, MinSizeBytes: 550, NameContains: null, MaxResults: 10));
        var paths = results.Select(e => e.Path).ToList();

        Assert.Contains("C:\\Users\\Wy\\AppData\\Local\\Temp", paths);
        Assert.DoesNotContain("C:\\Users\\Wy", paths);
        // Local is an ancestor of Temp; only the deeper matching folder is shown.
        Assert.DoesNotContain("C:\\Users\\Wy\\AppData\\Local", paths);
    }

    [Fact]
    public void Search_Directories_SiblingBranchesBothShown()
    {
        var results = BuildAncestorChain().Search(
            new SearchQuery(SearchKind.Directories, MinSizeBytes: 400, NameContains: null, MaxResults: 10));
        var paths = results.Select(e => e.Path).ToList();

        Assert.Contains("C:\\Users\\Wy\\AppData\\Local\\Temp", paths);
        Assert.Contains("C:\\Users\\Wy\\AppData\\Local\\Docker", paths);
        Assert.DoesNotContain("C:\\Users\\Wy\\AppData\\Local", paths);
    }

    [Fact]
    public void Search_All_MergesAndCapsResults()
    {
        var results = BuildAncestorChain().Search(
            new SearchQuery(SearchKind.All, MinSizeBytes: 0, NameContains: null, MaxResults: 3));

        Assert.Equal(3, results.Count);
        Assert.True(results[0].AllocatedSize >= results[1].AllocatedSize);
        Assert.True(results[1].AllocatedSize >= results[2].AllocatedSize);
    }
}
