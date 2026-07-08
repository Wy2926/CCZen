using System.Buffers.Binary;
using System.IO.Hashing;
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

    [Fact]
    public void SaveLoad_V2_RoundTripsLastWriteUtc()
    {
        var written = new DateTime(2023, 8, 20, 10, 0, 0, DateTimeKind.Utc);
        IndexBuilder original = MakeBuilder();
        original.SetLastWriteUtc(2, written);

        using var stream = new MemoryStream();
        original.Save(stream);
        stream.Position = 0;

        IndexBuilder? loaded = IndexBuilder.Load(stream);

        Assert.NotNull(loaded);
        FileSystemIndex index = loaded.Build("C:\\");
        FileEntry file = index.TopFiles(10).First(e => e.Path.EndsWith("a.bin"));
        Assert.Equal(written, index.GetLastWriteUtc(FindNode(index, file.Path)));
    }

    [Fact]
    public void Load_RejectsVersion1Cache()
    {
        using var stream = new MemoryStream();
        WriteVersion1Cache(MakeBuilder(), stream);
        stream.Position = 0;

        Assert.Null(IndexBuilder.Load(stream));
    }

    /// <summary>Writes a checksum-valid v1-format header; Load must reject unsupported versions.</summary>
    private static void WriteVersion1Cache(IndexBuilder builder, Stream stream)
    {
        using var v2 = new MemoryStream();
        builder.Save(v2);
        byte[] payload = v2.ToArray()[8..];
        payload[4] = 1;
        payload[5] = 0;
        payload[6] = 0;
        payload[7] = 0;
        Span<byte> checksum = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(checksum, XxHash64.HashToUInt64(payload));
        stream.Write(checksum);
        stream.Write(payload);
    }

    private static int FindNode(FileSystemIndex index, string path)
    {
        for (int i = 0; i < index.Count; i++)
        {
            if (index.GetPath(i) == path)
            {
                return i;
            }
        }

        throw new InvalidOperationException($"Node not found: {path}");
    }
}
