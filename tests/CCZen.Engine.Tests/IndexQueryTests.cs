using CCZen.Engine.Index;

namespace CCZen.Engine.Tests;

public class IndexQueryTests
{
    private static (FileSystemIndex Index, IndexQuery Query) BuildFixture()
    {
        var builder = new IndexBuilder(rootFrn: 5);
        builder.AddEntry(10, 5, "Users", isDirectory: true);
        builder.AddEntry(11, 10, "Wy", isDirectory: true);
        builder.AddEntry(12, 11, "AppData", isDirectory: true);
        builder.AddEntry(13, 12, "Local", isDirectory: true);
        builder.AddEntry(14, 13, "Cache", isDirectory: true);
        builder.SetLastWriteUtc(builder.Count - 1, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        builder.AddEntry(15, 14, "old.log", isDirectory: false);
        builder.SetSizes(builder.Count - 1, 100, 100);
        builder.SetLastWriteUtc(builder.Count - 1, new DateTime(2020, 6, 1, 0, 0, 0, DateTimeKind.Utc));
        builder.AddEntry(16, 13, "Temp", isDirectory: true);
        builder.AddEntry(17, 16, "big.tmp", isDirectory: false);
        builder.SetSizes(builder.Count - 1, 600, 600);
        builder.AddEntry(18, 13, "Docker", isDirectory: true);
        builder.AddEntry(19, 18, "disk.vhdx", isDirectory: false);
        builder.SetSizes(builder.Count - 1, 500, 500);
        builder.AddEntry(20, 13, "docs", isDirectory: true);
        builder.AddEntry(21, 20, "report.docx", isDirectory: false);
        builder.SetSizes(builder.Count - 1, 50, 50);
        builder.AddEntry(22, 5, "Google", isDirectory: true);
        builder.AddEntry(23, 22, "Chrome", isDirectory: true);
        builder.AddEntry(24, 23, "User Data", isDirectory: true);
        builder.AddEntry(25, 24, "Default", isDirectory: true);
        builder.AddEntry(26, 25, "Cache", isDirectory: true);
        builder.AddEntry(27, 26, "data.bin", isDirectory: false);
        builder.SetSizes(builder.Count - 1, 200, 200);
        builder.AddEntry(28, 24, "Profile 1", isDirectory: true);
        builder.AddEntry(29, 28, "Cache", isDirectory: true);
        builder.AddEntry(30, 29, "data.bin", isDirectory: false);
        builder.SetSizes(builder.Count - 1, 300, 300);
        builder.AddEntry(31, 5, "other.iso", isDirectory: false);
        builder.SetSizes(builder.Count - 1, 300, 300);

        FileSystemIndex index = builder.Build("C:\\");
        return (index, new IndexQuery(index));
    }

    [Fact]
    public void TryResolvePrefix_FindsDirectoryPath()
    {
        (_, IndexQuery query) = BuildFixture();

        Assert.True(query.TryResolvePrefix(@"C:\Users\Wy\AppData\Local", out int node));
        Assert.Equal(@"C:\Users\Wy\AppData\Local", query.GetPath(node));
    }

    [Fact]
    public void TryResolvePrefix_RejectsUnknownPath()
    {
        (_, IndexQuery query) = BuildFixture();

        Assert.False(query.TryResolvePrefix(@"C:\missing", out _));
    }

    [Fact]
    public void GetSubtreeStats_ReturnsAggregatesAndMaxWrite()
    {
        (_, IndexQuery query) = BuildFixture();
        Assert.True(query.TryResolvePrefix(@"C:\Users\Wy\AppData\Local\Cache", out int cacheNode));

        SubtreeStats stats = query.GetSubtreeStats(cacheNode);

        Assert.Equal(100, stats.AllocatedSize);
        Assert.Equal(100, stats.LogicalSize);
        Assert.Equal(1, stats.FileCount);
        Assert.Equal(new DateTime(2020, 6, 1, 0, 0, 0, DateTimeKind.Utc), stats.MaxLastWriteUtc);
    }

    [Fact]
    public void FindDirectoriesByName_MatchesLexiconWithinDepth()
    {
        (_, IndexQuery query) = BuildFixture();
        Assert.True(query.TryResolvePrefix(@"C:\Users\Wy\AppData\Local", out int root));
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Cache", "Temp", "Docker" };

        var hits = query.FindDirectoriesByName(root, names, maxDepth: 8).OrderBy(p => p).ToList();

        Assert.Contains(@"C:\Users\Wy\AppData\Local\Cache", hits);
        Assert.Contains(@"C:\Users\Wy\AppData\Local\Temp", hits);
        Assert.Contains(@"C:\Users\Wy\AppData\Local\Docker", hits);
        Assert.DoesNotContain(hits, p => p.Contains("Google"));
    }

    [Fact]
    public void FindFilesByExtension_RecursiveCollectsMatches()
    {
        (_, IndexQuery query) = BuildFixture();
        Assert.True(query.TryResolvePrefix(@"C:\Users\Wy\AppData\Local", out int root));
        var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".log", ".tmp" };

        var files = query.FindFilesByExtension(root, extensions, recursive: true).Select(f => f.Path).ToList();

        Assert.Contains(@"C:\Users\Wy\AppData\Local\Cache\old.log", files);
        Assert.Contains(@"C:\Users\Wy\AppData\Local\Temp\big.tmp", files);
        Assert.Equal(2, files.Count);
    }

    [Fact]
    public void FindFilesByExtension_NonRecursive_OnlyDirectChildren()
    {
        (_, IndexQuery query) = BuildFixture();
        Assert.True(query.TryResolvePrefix(@"C:\Users\Wy\AppData\Local", out int root));
        var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".log", ".tmp", ".vhdx" };

        var files = query.FindFilesByExtension(root, extensions, recursive: false).ToList();

        Assert.Empty(files);
    }

    [Fact]
    public void ExpandGlob_MiddleWildcard_ExpandsProfileCaches()
    {
        (_, IndexQuery query) = BuildFixture();

        var paths = query.ExpandGlob(@"C:\Google\Chrome\User Data\*\Cache").OrderBy(p => p).ToList();

        Assert.Equal(2, paths.Count);
        Assert.Contains(@"C:\Google\Chrome\User Data\Default\Cache", paths);
        Assert.Contains(@"C:\Google\Chrome\User Data\Profile 1\Cache", paths);
    }

    [Fact]
    public void ExpandGlob_TrailingStar_YieldsImmediateChildren()
    {
        (_, IndexQuery query) = BuildFixture();

        var paths = query.ExpandGlob(@"C:\Google\Chrome\User Data\*").OrderBy(p => p).ToList();

        Assert.Equal(2, paths.Count);
        Assert.Contains(@"C:\Google\Chrome\User Data\Default", paths);
        Assert.Contains(@"C:\Google\Chrome\User Data\Profile 1", paths);
    }

    [Fact]
    public void SubtreeContainsExtension_DetectsUserAssets()
    {
        (_, IndexQuery query) = BuildFixture();
        Assert.True(query.TryResolvePrefix(@"C:\Users\Wy\AppData\Local\docs", out int docsDir));
        var assets = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".docx", ".pdf" };

        Assert.True(query.SubtreeContainsExtension(docsDir, assets));
        Assert.True(query.TryResolvePrefix(@"C:\Users\Wy\AppData\Local\Cache", out int cacheDir));
        Assert.False(query.SubtreeContainsExtension(cacheDir, assets));
    }
}
