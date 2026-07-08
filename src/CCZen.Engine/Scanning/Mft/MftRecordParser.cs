using System.Buffers.Binary;

namespace CCZen.Engine.Scanning.Mft;

/// <summary>
/// Parses raw NTFS FILE records (as returned by FSCTL_GET_NTFS_FILE_RECORD) to
/// extract the logical and allocated size of the unnamed $DATA attribute and
/// LastWriteTime from $STANDARD_INFORMATION (SCAN-FR-026).
/// Layout reference: Microsoft Learn "Master File Table" and NTFS_FILE_RECORD_OUTPUT_BUFFER.
/// </summary>
public static class MftRecordParser
{
    private const uint FileRecordMagic = 0x454C4946; // "FILE"
    private const uint AttributeStandardInformation = 0x10;
    private const uint AttributeData = 0x80;
    private const uint AttributeEnd = 0xFFFFFFFF;
    private const int SectorSize = 512;

    public static bool TryGetFileSizes(Span<byte> record, out long logicalSize, out long allocatedSize) =>
        TryGetFileMetadata(record, out logicalSize, out allocatedSize, out _);

    public static bool TryGetFileMetadata(
        Span<byte> record, out long logicalSize, out long allocatedSize, out DateTime lastWriteUtc)
    {
        logicalSize = 0;
        allocatedSize = 0;
        lastWriteUtc = DateTime.MinValue;

        if (record.Length < 0x30 || BinaryPrimitives.ReadUInt32LittleEndian(record) != FileRecordMagic)
        {
            return false;
        }

        if (!ApplyFixups(record))
        {
            return false;
        }

        ushort flags = BinaryPrimitives.ReadUInt16LittleEndian(record[0x16..]);
        bool inUse = (flags & 0x0001) != 0;
        if (!inUse)
        {
            return false;
        }

        ulong baseRecord = BinaryPrimitives.ReadUInt64LittleEndian(record[0x20..]);
        if (baseRecord != 0)
        {
            return false; // extension record; caller must query the base record
        }

        int offset = BinaryPrimitives.ReadUInt16LittleEndian(record[0x14..]);
        int bytesInUse = (int)BinaryPrimitives.ReadUInt32LittleEndian(record[0x18..]);
        int limit = Math.Min(bytesInUse, record.Length);

        bool hasData = false;
        while (offset + 8 <= limit)
        {
            uint type = BinaryPrimitives.ReadUInt32LittleEndian(record[offset..]);
            if (type == AttributeEnd)
            {
                break;
            }

            int length = (int)BinaryPrimitives.ReadUInt32LittleEndian(record[(offset + 4)..]);
            if (length <= 0 || offset + length > limit)
            {
                break;
            }

            if (type == AttributeStandardInformation && record[offset + 8] == 0)
            {
                int valueOffset = BinaryPrimitives.ReadUInt16LittleEndian(record[(offset + 0x14)..]);
                int valueLength = (int)BinaryPrimitives.ReadUInt32LittleEndian(record[(offset + 0x10)..]);
                int valueStart = offset + valueOffset;
                if (valueLength >= 0x10 && valueStart + 0x10 <= limit)
                {
                    long fileTime = BinaryPrimitives.ReadInt64LittleEndian(record[(valueStart + 0x08)..]);
                    if (fileTime > 0)
                    {
                        lastWriteUtc = DateTime.FromFileTimeUtc(fileTime);
                    }
                }
            }
            else if (type == AttributeData && record[offset + 9] == 0)
            {
                hasData = TryParseDataAttribute(record, offset, limit, out logicalSize, out allocatedSize);
            }

            offset += length;
        }

        return hasData;
    }

    private static bool TryParseDataAttribute(Span<byte> record, int offset, int limit, out long logicalSize, out long allocatedSize)
    {
        logicalSize = 0;
        allocatedSize = 0;

        bool nonResident = record[offset + 8] != 0;
        if (!nonResident)
        {
            logicalSize = BinaryPrimitives.ReadUInt32LittleEndian(record[(offset + 0x10)..]);
            allocatedSize = 0; // resident data lives inside the MFT record itself
            return true;
        }

        allocatedSize = BinaryPrimitives.ReadInt64LittleEndian(record[(offset + 0x28)..]);
        logicalSize = BinaryPrimitives.ReadInt64LittleEndian(record[(offset + 0x30)..]);

        ushort compressionUnit = BinaryPrimitives.ReadUInt16LittleEndian(record[(offset + 0x22)..]);
        if (compressionUnit != 0 && offset + 0x48 <= limit)
        {
            // Compressed/sparse attribute: TotalAllocated reflects actual clusters.
            allocatedSize = BinaryPrimitives.ReadInt64LittleEndian(record[(offset + 0x40)..]);
        }

        return true;
    }

    private static bool ApplyFixups(Span<byte> record)
    {
        int usaOffset = BinaryPrimitives.ReadUInt16LittleEndian(record[4..]);
        int usaCount = BinaryPrimitives.ReadUInt16LittleEndian(record[6..]);
        if (usaCount < 2 || usaOffset + usaCount * 2 > record.Length)
        {
            return true; // nothing to fix (defensive: some fixtures omit fixups)
        }

        ushort sequence = BinaryPrimitives.ReadUInt16LittleEndian(record[usaOffset..]);
        for (int i = 1; i < usaCount; i++)
        {
            int sectorEnd = i * SectorSize;
            if (sectorEnd > record.Length)
            {
                break;
            }

            ushort stored = BinaryPrimitives.ReadUInt16LittleEndian(record[(sectorEnd - 2)..]);
            if (stored != sequence)
            {
                return false; // torn write
            }

            ushort original = BinaryPrimitives.ReadUInt16LittleEndian(record[(usaOffset + i * 2)..]);
            BinaryPrimitives.WriteUInt16LittleEndian(record[(sectorEnd - 2)..], original);
        }

        return true;
    }
}
