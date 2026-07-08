using System.Buffers.Binary;
using CCZen.Engine.Scanning.Mft;

namespace CCZen.Engine.Tests;

public class MftRecordParserTests
{
    [Fact]
    public void NonResidentData_ReturnsRealAndAllocatedSize()
    {
        byte[] record = BuildRecord(nonResidentData: true, realSize: 123456, allocatedSize: 126976);

        Assert.True(MftRecordParser.TryGetFileSizes(record, out long logical, out long allocated));
        Assert.Equal(123456, logical);
        Assert.Equal(126976, allocated);
    }

    [Fact]
    public void ResidentData_ReturnsContentLengthAndZeroAllocated()
    {
        byte[] record = BuildRecord(nonResidentData: false, realSize: 300, allocatedSize: 0);

        Assert.True(MftRecordParser.TryGetFileSizes(record, out long logical, out long allocated));
        Assert.Equal(300, logical);
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void CompressedData_UsesTotalAllocated()
    {
        byte[] record = BuildRecord(nonResidentData: true, realSize: 1_000_000, allocatedSize: 1_048_576, compressionUnit: 4, totalAllocated: 65536);

        Assert.True(MftRecordParser.TryGetFileSizes(record, out long logical, out long allocated));
        Assert.Equal(1_000_000, logical);
        Assert.Equal(65536, allocated);
    }

    [Fact]
    public void Fixups_AreAppliedBeforeParsing()
    {
        byte[] record = BuildRecord(nonResidentData: true, realSize: 42, allocatedSize: 4096, applyFixups: true);

        Assert.True(MftRecordParser.TryGetFileSizes(record, out long logical, out _));
        Assert.Equal(42, logical);
    }

    [Fact]
    public void InvalidMagic_ReturnsFalse()
    {
        byte[] record = new byte[1024];

        Assert.False(MftRecordParser.TryGetFileSizes(record, out _, out _));
    }

    [Fact]
    public void NotInUseRecord_ReturnsFalse()
    {
        byte[] record = BuildRecord(nonResidentData: true, realSize: 1, allocatedSize: 1, inUse: false);

        Assert.False(MftRecordParser.TryGetFileSizes(record, out _, out _));
    }

    [Fact]
    public void ExtensionRecord_ReturnsFalse()
    {
        byte[] record = BuildRecord(nonResidentData: true, realSize: 1, allocatedSize: 1, baseRecord: 99);

        Assert.False(MftRecordParser.TryGetFileSizes(record, out _, out _));
    }

    [Fact]
    public void StandardInformation_ReturnsLastModificationTime()
    {
        var expected = new DateTime(2024, 6, 15, 12, 30, 0, DateTimeKind.Utc);
        long fileTime = expected.ToFileTimeUtc();
        byte[] record = BuildRecord(nonResidentData: true, realSize: 100, allocatedSize: 4096, lastWriteFileTime: fileTime);

        Assert.True(MftRecordParser.TryGetFileMetadata(record, out long logical, out long allocated, out DateTime lastWrite));
        Assert.Equal(100, logical);
        Assert.Equal(4096, allocated);
        Assert.Equal(expected, lastWrite);
    }

    [Fact]
    public void MissingStandardInformation_ReturnsMinLastWrite()
    {
        byte[] record = BuildRecord(nonResidentData: true, realSize: 50, allocatedSize: 4096);

        Assert.True(MftRecordParser.TryGetFileMetadata(record, out _, out _, out DateTime lastWrite));
        Assert.Equal(DateTime.MinValue, lastWrite);
    }

    /// <summary>Builds a minimal synthetic FILE record with a single unnamed $DATA attribute.</summary>
    private static byte[] BuildRecord(
        bool nonResidentData,
        long realSize,
        long allocatedSize,
        ushort compressionUnit = 0,
        long totalAllocated = 0,
        bool inUse = true,
        ulong baseRecord = 0,
        bool applyFixups = false,
        long lastWriteFileTime = 0)
    {
        byte[] record = new byte[1024];
        var span = record.AsSpan();

        BinaryPrimitives.WriteUInt32LittleEndian(span, 0x454C4946); // "FILE"
        BinaryPrimitives.WriteUInt16LittleEndian(span[4..], 0x30);  // USA offset
        BinaryPrimitives.WriteUInt16LittleEndian(span[6..], 3);     // USA count (1 USN + 2 sectors)
        BinaryPrimitives.WriteUInt16LittleEndian(span[0x14..], 0x38); // first attribute offset
        ushort flags = (ushort)(inUse ? 0x0001 : 0x0000);
        BinaryPrimitives.WriteUInt16LittleEndian(span[0x16..], flags);
        BinaryPrimitives.WriteUInt32LittleEndian(span[0x18..], 0x200); // bytes in use
        BinaryPrimitives.WriteUInt64LittleEndian(span[0x20..], baseRecord);

        int attr = 0x38;
        if (lastWriteFileTime != 0)
        {
            const int stdInfoLength = 0x60; // 0x18 header + 0x48 value
            BinaryPrimitives.WriteUInt32LittleEndian(span[attr..], 0x10); // $STANDARD_INFORMATION
            BinaryPrimitives.WriteUInt32LittleEndian(span[(attr + 4)..], (uint)stdInfoLength);
            span[attr + 8] = 0; // resident
            BinaryPrimitives.WriteUInt32LittleEndian(span[(attr + 0x10)..], 0x48);
            BinaryPrimitives.WriteUInt16LittleEndian(span[(attr + 0x14)..], 0x18);
            int value = attr + 0x18;
            BinaryPrimitives.WriteInt64LittleEndian(span[(value + 0x08)..], lastWriteFileTime);
            attr += stdInfoLength;
        }

        BinaryPrimitives.WriteUInt32LittleEndian(span[attr..], 0x80); // $DATA
        int attrLength = nonResidentData ? 0x48 : 0x18;
        BinaryPrimitives.WriteUInt32LittleEndian(span[(attr + 4)..], (uint)attrLength);
        span[attr + 8] = (byte)(nonResidentData ? 1 : 0);
        span[attr + 9] = 0; // unnamed

        if (nonResidentData)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(span[(attr + 0x22)..], compressionUnit);
            BinaryPrimitives.WriteInt64LittleEndian(span[(attr + 0x28)..], allocatedSize);
            BinaryPrimitives.WriteInt64LittleEndian(span[(attr + 0x30)..], realSize);
            BinaryPrimitives.WriteInt64LittleEndian(span[(attr + 0x40)..], totalAllocated);
        }
        else
        {
            BinaryPrimitives.WriteUInt32LittleEndian(span[(attr + 0x10)..], (uint)realSize);
        }

        BinaryPrimitives.WriteUInt32LittleEndian(span[(attr + attrLength)..], 0xFFFFFFFF); // end marker

        // Update sequence array: sequence number + saved bytes for both sector tails.
        ushort sequence = 0x0007;
        BinaryPrimitives.WriteUInt16LittleEndian(span[0x30..], sequence);
        if (applyFixups)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(span[0x32..], BinaryPrimitives.ReadUInt16LittleEndian(span[510..]));
            BinaryPrimitives.WriteUInt16LittleEndian(span[0x34..], BinaryPrimitives.ReadUInt16LittleEndian(span[1022..]));
            BinaryPrimitives.WriteUInt16LittleEndian(span[510..], sequence);
            BinaryPrimitives.WriteUInt16LittleEndian(span[1022..], sequence);
        }
        else
        {
            // Consistent no-op fixups: stored tails equal the sequence, saved values are the same bytes.
            BinaryPrimitives.WriteUInt16LittleEndian(span[0x32..], BinaryPrimitives.ReadUInt16LittleEndian(span[510..]));
            BinaryPrimitives.WriteUInt16LittleEndian(span[0x34..], BinaryPrimitives.ReadUInt16LittleEndian(span[1022..]));
            BinaryPrimitives.WriteUInt16LittleEndian(span[510..], sequence);
            BinaryPrimitives.WriteUInt16LittleEndian(span[1022..], sequence);
        }

        return record;
    }
}
