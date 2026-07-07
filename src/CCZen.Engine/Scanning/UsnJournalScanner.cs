using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using CCZen.Engine.Index;
using CCZen.Engine.Scanning.Mft;
using Microsoft.Win32.SafeHandles;
using Windows.Win32;
using Windows.Win32.Storage.FileSystem;
using Windows.Win32.System.Ioctl;

namespace CCZen.Engine.Scanning;

/// <summary>
/// NTFS fast path (spec: SCAN-FR-001..004): enumerates every MFT record on the
/// volume via FSCTL_ENUM_USN_DATA to build the name/tree index, then reads MFT
/// file records in parallel via FSCTL_GET_NTFS_FILE_RECORD to obtain logical
/// and allocated sizes without touching the directory tree. Requires
/// administrator rights to open the volume handle (SCAN-FR-002).
/// </summary>
[SupportedOSPlatform("windows5.1.2600")]
public sealed class UsnJournalScanner : IVolumeScanner
{
    private const ulong RootFrnMask = 0x0000FFFFFFFFFFFF;
    private const int EnumBufferSize = 1 << 20; // 1 MB per FSCTL call (SCAN-FR-001)
    private const int Fsctl_Enum_Usn_Data = unchecked((int)0x000900B3);
    private const int Fsctl_Get_Ntfs_Volume_Data = 0x00090064;
    private const int Fsctl_Get_Ntfs_File_Record = 0x00090068;
    private const int ErrorHandleEof = 38;

    public unsafe FileSystemIndex Scan(string root, CancellationToken cancellationToken = default)
    {
        char driveLetter = char.ToUpperInvariant(root[0]);
        string rootLabel = $"{driveLetter}:\\";
        using SafeFileHandle volume = OpenVolume(driveLetter);

        (int recordLength, ulong rootFrn) = QueryVolumeData(volume);
        var builder = new IndexBuilder(rootFrn & RootFrnMask);
        var fileIndices = new List<int>();

        EnumerateUsnData(volume, builder, fileIndices, cancellationToken);
        ReadSizesFromMft(driveLetter, builder, fileIndices, recordLength, rootLabel, cancellationToken);

        return builder.Build(rootLabel);
    }

    private static SafeFileHandle OpenVolume(char driveLetter)
    {
        SafeFileHandle handle = PInvoke.CreateFile(
            $"\\\\.\\{driveLetter}:",
            0x0001 | 0x0080, // FILE_READ_DATA | FILE_READ_ATTRIBUTES (SCAN-FR-002)
            FILE_SHARE_MODE.FILE_SHARE_READ | FILE_SHARE_MODE.FILE_SHARE_WRITE,
            null,
            FILE_CREATION_DISPOSITION.OPEN_EXISTING,
            0,
            null);
        if (handle.IsInvalid)
        {
            throw new UnauthorizedAccessException(
                $"Cannot open volume {driveLetter}: (error {Marshal.GetLastWin32Error()}). Administrator rights are required for the NTFS fast path.");
        }

        return handle;
    }

    private static unsafe (int RecordLength, ulong RootFrn) QueryVolumeData(SafeFileHandle volume)
    {
        NTFS_VOLUME_DATA_BUFFER data = default;
        uint returned;
        if (!PInvoke.DeviceIoControl(volume, (uint)Fsctl_Get_Ntfs_Volume_Data, null, 0, &data, (uint)sizeof(NTFS_VOLUME_DATA_BUFFER), &returned, null))
        {
            throw new IOException($"FSCTL_GET_NTFS_VOLUME_DATA failed (error {Marshal.GetLastWin32Error()}). Is the volume NTFS?");
        }

        // The NTFS root directory is always file record 5.
        return ((int)data.BytesPerFileRecordSegment, 5);
    }

    private static unsafe void EnumerateUsnData(
        SafeFileHandle volume, IndexBuilder builder, List<int> fileIndices, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[EnumBufferSize];
        // MFT_ENUM_DATA_V0: StartFileReferenceNumber, LowUsn, HighUsn
        Span<byte> input = stackalloc byte[24];
        BinaryPrimitives.WriteInt64LittleEndian(input[8..], 0);
        BinaryPrimitives.WriteInt64LittleEndian(input[16..], long.MaxValue);

        fixed (byte* pBuffer = buffer)
        fixed (byte* pInput = input)
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                uint returned;
                if (!PInvoke.DeviceIoControl(volume, (uint)Fsctl_Enum_Usn_Data, pInput, 24, pBuffer, EnumBufferSize, &returned, null))
                {
                    int error = Marshal.GetLastWin32Error();
                    if (error == ErrorHandleEof)
                    {
                        break;
                    }

                    throw new IOException($"FSCTL_ENUM_USN_DATA failed (error {error}).");
                }

                if (returned < 8)
                {
                    break;
                }

                Span<byte> chunk = buffer.AsSpan(0, (int)returned);
                BinaryPrimitives.WriteUInt64LittleEndian(input, BinaryPrimitives.ReadUInt64LittleEndian(chunk));

                int offset = 8;
                while (offset + 60 <= chunk.Length)
                {
                    Span<byte> record = chunk[offset..];
                    int recordLength = (int)BinaryPrimitives.ReadUInt32LittleEndian(record);
                    if (recordLength < 60 || offset + recordLength > chunk.Length)
                    {
                        break;
                    }

                    // USN_RECORD_V2 layout (Microsoft Learn).
                    ulong frn = BinaryPrimitives.ReadUInt64LittleEndian(record[8..]) & RootFrnMask;
                    ulong parentFrn = BinaryPrimitives.ReadUInt64LittleEndian(record[16..]) & RootFrnMask;
                    uint attributes = BinaryPrimitives.ReadUInt32LittleEndian(record[52..]);
                    int nameLength = BinaryPrimitives.ReadUInt16LittleEndian(record[56..]);
                    int nameOffset = BinaryPrimitives.ReadUInt16LittleEndian(record[58..]);
                    string name = new(MemoryMarshal.Cast<byte, char>(record.Slice(nameOffset, nameLength)));
                    bool isDirectory = (attributes & (uint)FileAttributes.Directory) != 0;

                    int before = builder.Count;
                    builder.AddEntry(frn, parentFrn, name, isDirectory);
                    if (!isDirectory && builder.Count > before)
                    {
                        fileIndices.Add(builder.Count - 1);
                    }

                    offset += recordLength;
                }
            }
        }
    }

    private static unsafe void ReadSizesFromMft(
        char driveLetter, IndexBuilder builder, List<int> fileIndices, int recordLength, string rootLabel, CancellationToken cancellationToken)
    {
        int outputSize = 12 + recordLength; // NTFS_FILE_RECORD_OUTPUT_BUFFER header + record
        int workers = Math.Max(1, Environment.ProcessorCount);

        Parallel.For(
            0,
            workers,
            new ParallelOptions { CancellationToken = cancellationToken },
            worker => SizeWorker(driveLetter, builder, fileIndices, recordLength, outputSize, rootLabel, worker, workers));
    }

    private static unsafe void SizeWorker(
        char driveLetter, IndexBuilder builder, List<int> fileIndices, int recordLength, int outputSize, string rootLabel, int worker, int workers)
    {
        using SafeFileHandle volume = OpenVolume(driveLetter);
        byte[] output = new byte[outputSize];
        Span<byte> input = stackalloc byte[8];

        fixed (byte* pOutput = output)
        fixed (byte* pInput = input)
        {
            for (int i = worker; i < fileIndices.Count; i += workers)
            {
                int index = fileIndices[i];
                BinaryPrimitives.WriteUInt64LittleEndian(input, builder.GetFrn(index));
                uint returned;
                if (!PInvoke.DeviceIoControl(volume, (uint)Fsctl_Get_Ntfs_File_Record, pInput, 8, pOutput, (uint)outputSize, &returned, null))
                {
                    continue;
                }

                // Skip if NTFS returned a different (earlier) record for a stale FRN.
                if (BinaryPrimitives.ReadUInt64LittleEndian(output) != builder.GetFrn(index))
                {
                    continue;
                }

                int length = (int)BinaryPrimitives.ReadUInt32LittleEndian(output.AsSpan(8));
                Span<byte> record = output.AsSpan(12, Math.Min(length, recordLength));
                if (MftRecordParser.TryGetFileSizes(record, out long logical, out long allocated))
                {
                    builder.SetSizes(index, logical, allocated);
                }
                else
                {
                    FallbackSize(builder, index, rootLabel);
                }
            }
        }
    }

    private static void FallbackSize(IndexBuilder builder, int index, string rootLabel)
    {
        // Rare path: attribute lists / extension records. Query through the file system instead.
        try
        {
            var info = new FileInfo(builder.GetPath(index, rootLabel));
            if (info.Exists)
            {
                builder.SetSizes(index, info.Length, info.Length);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
