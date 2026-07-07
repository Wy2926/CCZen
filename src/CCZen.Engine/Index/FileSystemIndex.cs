namespace CCZen.Engine.Index;

/// <summary>
/// Immutable struct-of-arrays index of a scanned volume (spec: SCAN-FR-020, ARCH-NFR-001).
/// </summary>
public sealed class FileSystemIndex
{
    private readonly int[] _parent;
    private readonly string[] _name;
    private readonly bool[] _isDirectory;
    private readonly long[] _logicalSize;
    private readonly long[] _allocatedSize;
    private readonly long[] _subtreeLogical;
    private readonly long[] _subtreeAllocated;
    private readonly int[] _subtreeFileCount;
    private readonly string _rootLabel;

    internal FileSystemIndex(
        string rootLabel,
        int[] parent,
        string[] name,
        bool[] isDirectory,
        long[] logicalSize,
        long[] allocatedSize)
    {
        _rootLabel = rootLabel;
        _parent = parent;
        _name = name;
        _isDirectory = isDirectory;
        _logicalSize = logicalSize;
        _allocatedSize = allocatedSize;
        _subtreeLogical = new long[parent.Length];
        _subtreeAllocated = new long[parent.Length];
        _subtreeFileCount = new int[parent.Length];
        Aggregate();
    }

    public int Count => _parent.Length;

    public int FileCount { get; private set; }

    public long TotalLogicalSize { get; private set; }

    public long TotalAllocatedSize { get; private set; }

    private void Aggregate()
    {
        for (int i = 0; i < _parent.Length; i++)
        {
            if (_isDirectory[i])
            {
                continue;
            }

            FileCount++;
            long logical = _logicalSize[i];
            long allocated = _allocatedSize[i];
            TotalLogicalSize += logical;
            TotalAllocatedSize += allocated;

            for (int p = _parent[i]; p >= 0; p = _parent[p])
            {
                _subtreeLogical[p] += logical;
                _subtreeAllocated[p] += allocated;
                _subtreeFileCount[p]++;
            }
        }
    }

    public string GetPath(int nodeIndex)
    {
        if (_parent[nodeIndex] < 0)
        {
            return _rootLabel;
        }

        var segments = new Stack<string>();
        int current = nodeIndex;
        while (current >= 0 && _parent[current] >= 0)
        {
            segments.Push(_name[current]);
            current = _parent[current];
        }

        return _rootLabel + string.Join('\\', segments);
    }

    public IReadOnlyList<FileEntry> TopFiles(int count) =>
        TopBy(count, i => !_isDirectory[i], i => _allocatedSize[i]);

    public IReadOnlyList<FileEntry> TopDirectories(int count) =>
        TopBy(count, i => _isDirectory[i], i => _subtreeAllocated[i]);

    private List<FileEntry> TopBy(int count, Func<int, bool> filter, Func<int, long> key)
    {
        var queue = new PriorityQueue<int, long>();
        for (int i = 0; i < _parent.Length; i++)
        {
            if (!filter(i))
            {
                continue;
            }

            long value = key(i);
            if (queue.Count < count)
            {
                queue.Enqueue(i, value);
            }
            else if (queue.TryPeek(out _, out long min) && value > min)
            {
                queue.DequeueEnqueue(i, value);
            }
        }

        var result = new List<FileEntry>(queue.Count);
        while (queue.TryDequeue(out int idx, out _))
        {
            result.Add(ToEntry(idx));
        }

        result.Reverse();
        return result;
    }

    private FileEntry ToEntry(int i) => new(
        GetPath(i),
        _isDirectory[i],
        _isDirectory[i] ? _subtreeLogical[i] : _logicalSize[i],
        _isDirectory[i] ? _subtreeAllocated[i] : _allocatedSize[i],
        _isDirectory[i] ? _subtreeFileCount[i] : 1);
}

/// <summary>A file or directory (with aggregated subtree totals) returned by index queries.</summary>
public readonly record struct FileEntry(
    string Path,
    bool IsDirectory,
    long LogicalSize,
    long AllocatedSize,
    int FileCount);
