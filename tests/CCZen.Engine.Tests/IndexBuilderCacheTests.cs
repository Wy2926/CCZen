using CCZen.Engine.Index;

namespace CCZen.Engine.Tests;

public class IndexBuilderCacheTests
{
    private static IndexBuilder MakeBuilder()
    {
        var builder = new IndexBuilder(rootFrn: 5) { UsnJournalId = 0xABCDEF, NextUsn = 12345 };
        builder.AddEntry(10, 5, "dir", isDirectory: true);
        builder.AddEntry(11, 10, "a.bin", isDirectory: false);
        builder.SetSizes(builder.Count - 1, 100, 4096);
        builder.AddEntry(12, 5, "b.bin", isDirectory: false);
        builder.SetSizes(builder.Count - 1, 200, 8192);
        return builder;
    }

    [Fact]
    public void SaveLoad_RoundTripsEntriesAndWatermark()
    {
        IndexBuilder original = MakeBuilder();
        using var stream = new MemoryStream();
        original.Save(stream);
        stream.Position = 0;

        IndexBuilder? loaded = IndexBuilder.Load(stream);

        Assert.NotNull(loaded);
        Assert.Equal(0xABCDEFUL, loaded.UsnJournalId);
        Assert.Equal(12345, loaded.NextUsn);
        FileSystemIndex index = loaded.Build("C:\\");
        Assert.Equal(2, index.FileCount);
        Assert.Equal(300, index.TotalLogicalSize);
        Assert.Equal(4096 + 8192, index.TotalAllocatedSize);
    }

    [Fact]
    public void Load_RejectsCorruptedPayload()
    {
        using var stream = new MemoryStream();
        MakeBuilder().Save(stream);
        byte[] bytes = stream.ToArray();
        bytes[^1] ^= 0xFF;

        Assert.Null(IndexBuilder.Load(new MemoryStream(bytes)));
    }

    [Fact]
    public void Load_RejectsTruncatedStream()
    {
        Assert.Null(IndexBuilder.Load(new MemoryStream(new byte[4])));
    }

    [Fact]
    public void Remove_TombstonesEntryAndSubtractsFromAggregates()
    {
        IndexBuilder builder = MakeBuilder();
        builder.Remove(11);

        FileSystemIndex index = builder.Build("C:\\");

        Assert.Equal(1, index.FileCount);
        Assert.Equal(200, index.TotalLogicalSize);
        Assert.DoesNotContain(index.TopFiles(10), e => e.Path.EndsWith("a.bin"));
    }

    [Fact]
    public void Upsert_UpdatesNameAndParentForExistingFrn()
    {
        IndexBuilder builder = MakeBuilder();
        int index = builder.Upsert(12, 10, "renamed.bin", isDirectory: false);
        builder.SetSizes(index, 500, 4096);

        FileSystemIndex built = builder.Build("C:\\");

        Assert.Contains(built.TopFiles(10), e => e.Path == "C:\\dir\\renamed.bin" && e.LogicalSize == 500);
    }

    [Fact]
    public void Upsert_AfterRemove_RevivesFrn()
    {
        IndexBuilder builder = MakeBuilder();
        builder.Remove(12);
        int index = builder.Upsert(12, 5, "new.bin", isDirectory: false);
        builder.SetSizes(index, 42, 4096);

        FileSystemIndex built = builder.Build("C:\\");

        Assert.Equal(2, built.FileCount);
        Assert.Contains(built.TopFiles(10), e => e.Path == "C:\\new.bin");
    }
}
