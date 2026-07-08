using System.Buffers.Binary;
using System.IO.Hashing;
using System.Text;

namespace CCZen.Engine.Index;

/// <summary>
/// Accumulates raw scan entries keyed by file reference number (FRN) and
/// links them into a <see cref="FileSystemIndex"/>. Entries whose parent
/// chain cannot be resolved are attached to a synthetic orphan node.
/// </summary>
public sealed class IndexBuilder
{
    private readonly List<ulong> _frn = new();
    private readonly List<ulong> _parentFrn = new();
    private readonly List<string> _name = new();
    private readonly List<bool> _isDirectory = new();
    private readonly List<long> _logicalSize = new();
    private readonly List<long> _allocatedSize = new();
    private readonly List<long> _lastWriteUtcTicks = new();
    private readonly List<bool> _removed = new();
    private readonly Dictionary<ulong, int> _frnToIndex = new();
    private readonly ulong _rootFrn;

    public IndexBuilder(ulong rootFrn)
    {
        _rootFrn = rootFrn;
        AddEntry(rootFrn, 0, string.Empty, isDirectory: true);
    }

    public int Count => _frn.Count;

    /// <summary>USN journal identity/watermark used for incremental catch-up (SCAN-FR-030..032).</summary>
    public ulong UsnJournalId { get; set; }

    public long NextUsn { get; set; }

    public void AddEntry(ulong frn, ulong parentFrn, string name, bool isDirectory)
    {
        if (_frnToIndex.ContainsKey(frn))
        {
            return;
        }

        _frnToIndex.Add(frn, _frn.Count);
        _frn.Add(frn);
        _parentFrn.Add(parentFrn);
        _name.Add(name);
        _isDirectory.Add(isDirectory);
        _logicalSize.Add(0);
        _allocatedSize.Add(0);
        _lastWriteUtcTicks.Add(0);
        _removed.Add(false);
    }

    /// <summary>Adds a new entry or refreshes name/parent of an existing one (USN rename/move).</summary>
    public int Upsert(ulong frn, ulong parentFrn, string name, bool isDirectory)
    {
        if (_frnToIndex.TryGetValue(frn, out int index))
        {
            _parentFrn[index] = parentFrn;
            _name[index] = name;
            _isDirectory[index] = isDirectory;
            _removed[index] = false;
            return index;
        }

        AddEntry(frn, parentFrn, name, isDirectory);
        return _frn.Count - 1;
    }

    /// <summary>Tombstones an entry (USN FILE_DELETE); its FRN may later be reused by NTFS.</summary>
    public void Remove(ulong frn)
    {
        if (_frnToIndex.Remove(frn, out int index))
        {
            _removed[index] = true;
        }
    }

    public bool TryGetIndex(ulong frn, out int index) => _frnToIndex.TryGetValue(frn, out index);

    public bool IsDirectory(int index) => _isDirectory[index];

    public ulong GetFrn(int index) => _frn[index];

    public void SetSizes(int index, long logicalSize, long allocatedSize)
    {
        _logicalSize[index] = logicalSize;
        _allocatedSize[index] = allocatedSize;
    }

    public void SetLastWriteUtc(int index, DateTime lastWriteUtc)
    {
        _lastWriteUtcTicks[index] = lastWriteUtc == DateTime.MinValue ? 0 : lastWriteUtc.Ticks;
    }

    /// <summary>Reconstructs the full path of an entry before the index is built.</summary>
    public string GetPath(int index, string rootLabel)
    {
        var segments = new Stack<string>();
        int current = index;
        while (_frn[current] != _rootFrn)
        {
            segments.Push(_name[current]);
            if (!_frnToIndex.TryGetValue(_parentFrn[current], out current))
            {
                break;
            }
        }

        return rootLabel + string.Join('\\', segments);
    }

    public FileSystemIndex Build(string rootLabel)
    {
        int count = _frn.Count;
        var compact = new int[count];
        int live = 0;
        for (int i = 0; i < count; i++)
        {
            compact[i] = _removed[i] ? -1 : live++;
        }

        int orphanIndex = -1;
        var parent = new int[live + 1];
        var name = new string[live + 1];
        var isDirectory = new bool[live + 1];
        var logicalSize = new long[live + 1];
        var allocatedSize = new long[live + 1];
        var lastWriteUtcTicks = new long[live + 1];

        for (int i = 0; i < count; i++)
        {
            if (compact[i] < 0)
            {
                continue;
            }

            int j = compact[i];
            name[j] = _name[i];
            isDirectory[j] = _isDirectory[i];
            logicalSize[j] = _logicalSize[i];
            allocatedSize[j] = _allocatedSize[i];
            lastWriteUtcTicks[j] = _lastWriteUtcTicks[i];

            if (_frn[i] == _rootFrn)
            {
                parent[j] = -1;
            }
            else if (_frnToIndex.TryGetValue(_parentFrn[i], out int p) && _isDirectory[p] && !_removed[p])
            {
                parent[j] = compact[p];
            }
            else
            {
                if (orphanIndex < 0)
                {
                    orphanIndex = live;
                }

                parent[j] = orphanIndex;
            }
        }

        int total = orphanIndex < 0 ? live : live + 1;
        if (orphanIndex >= 0)
        {
            name[orphanIndex] = "<orphaned>";
            isDirectory[orphanIndex] = true;
            parent[orphanIndex] = compact[_frnToIndex[_rootFrn]];
        }

        return new FileSystemIndex(
            rootLabel,
            parent[..total],
            name[..total],
            isDirectory[..total],
            logicalSize[..total],
            allocatedSize[..total],
            lastWriteUtcTicks[..total]);
    }

    private const uint CacheMagic = 0x58445A43; // "CZDX"
    private const uint CacheVersion = 2;

    /// <summary>Persists the snapshot with an XxHash64 integrity checksum (SCAN-FR-032).</summary>
    public void Save(Stream stream)
    {
        using var payload = new MemoryStream();
        using (var writer = new BinaryWriter(payload, Encoding.Unicode, leaveOpen: true))
        {
            writer.Write(CacheMagic);
            writer.Write(CacheVersion);
            writer.Write(_rootFrn);
            writer.Write(UsnJournalId);
            writer.Write(NextUsn);

            int live = 0;
            for (int i = 0; i < _frn.Count; i++)
            {
                if (!_removed[i])
                {
                    live++;
                }
            }

            writer.Write(live);
            for (int i = 0; i < _frn.Count; i++)
            {
                if (_removed[i])
                {
                    continue;
                }

                writer.Write(_frn[i]);
                writer.Write(_parentFrn[i]);
                writer.Write(_isDirectory[i]);
                writer.Write(_logicalSize[i]);
                writer.Write(_allocatedSize[i]);
                writer.Write(_lastWriteUtcTicks[i]);
                writer.Write(_name[i]);
            }
        }

        byte[] bytes = payload.ToArray();
        Span<byte> checksum = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(checksum, XxHash64.HashToUInt64(bytes));
        stream.Write(checksum);
        stream.Write(bytes);
    }

    /// <summary>Loads a snapshot; returns null if the file is corrupt or from another format version.</summary>
    public static IndexBuilder? Load(Stream stream)
    {
        Span<byte> checksum = stackalloc byte[8];
        if (stream.Read(checksum) != 8)
        {
            return null;
        }

        using var payload = new MemoryStream();
        stream.CopyTo(payload);
        byte[] bytes = payload.ToArray();
        if (XxHash64.HashToUInt64(bytes) != BinaryPrimitives.ReadUInt64LittleEndian(checksum))
        {
            return null;
        }

        using var reader = new BinaryReader(new MemoryStream(bytes), Encoding.Unicode);
        if (reader.ReadUInt32() != CacheMagic || reader.ReadUInt32() != CacheVersion)
        {
            return null;
        }

        ulong rootFrn = reader.ReadUInt64();
        var builder = new IndexBuilder(rootFrn)
        {
            UsnJournalId = reader.ReadUInt64(),
            NextUsn = reader.ReadInt64(),
        };

        int count = reader.ReadInt32();
        for (int i = 0; i < count; i++)
        {
            ulong frn = reader.ReadUInt64();
            ulong parentFrn = reader.ReadUInt64();
            bool isDirectory = reader.ReadBoolean();
            long logical = reader.ReadInt64();
            long allocated = reader.ReadInt64();
            long lastWriteTicks = reader.ReadInt64();
            string name = reader.ReadString();
            int index = builder.Upsert(frn, parentFrn, name, isDirectory);
            builder.SetSizes(index, logical, allocated);
            builder.SetLastWriteUtc(index, lastWriteTicks == 0 ? DateTime.MinValue : new DateTime(lastWriteTicks, DateTimeKind.Utc));
        }

        return builder;
    }
}
