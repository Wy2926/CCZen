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

    /// <summary>
    /// Largest directories with ancestor chains collapsed (SCAN-FR-024): a
    /// directory is suppressed when a single child directory accounts for at
    /// least <paramref name="dominanceThreshold"/> of its subtree size — the
    /// child carries the information. The scan root itself is always excluded.
    /// </summary>
    public IReadOnlyList<FileEntry> TopDistinctDirectories(int count, double dominanceThreshold = 0.7)
    {
        long[] largestChildDir = new long[_parent.Length];
        for (int i = 0; i < _parent.Length; i++)
        {
            int p = _parent[i];
            if (p >= 0 && _isDirectory[i] && _subtreeAllocated[i] > largestChildDir[p])
            {
                largestChildDir[p] = _subtreeAllocated[i];
            }
        }

        bool IsDistinct(int i) =>
            _isDirectory[i] &&
            _parent[i] >= 0 &&
            largestChildDir[i] < _subtreeAllocated[i] * dominanceThreshold;

        return TopBy(count, IsDistinct, i => _subtreeAllocated[i]);
    }

    /// <summary>
    /// Conditional query over the in-memory index (SCAN-FR-025): returns the
    /// largest files and/or directories matching a minimum size and an
    /// optional case-insensitive name fragment (e.g. an extension like ".iso"
    /// or a folder name). Directories use collapsed-chain semantics.
    /// </summary>
    public IReadOnlyList<FileEntry> Search(SearchQuery query)
    {
        IReadOnlyList<FileEntry> files = query.Kind is SearchKind.Files or SearchKind.All
            ? TopBy(query.MaxResults, i => !_isDirectory[i] && MatchesQuery(i, query, _allocatedSize[i]), i => _allocatedSize[i])
            : [];
        IReadOnlyList<FileEntry> directories = query.Kind is SearchKind.Directories or SearchKind.All
            ? SuppressNestedDirectoryAncestors(TopDistinctDirectoriesMatching(query))
            : [];

        return files.Concat(directories)
            .OrderByDescending(e => e.AllocatedSize)
            .Take(query.MaxResults)
            .ToList();
    }

    private IReadOnlyList<FileEntry> TopDistinctDirectoriesMatching(SearchQuery query) =>
        TopDistinctDirectories(int.MaxValue)
            .Where(e => e.AllocatedSize >= query.MinSizeBytes &&
                        (query.NameContains is null ||
                         e.Path.Contains(query.NameContains, StringComparison.OrdinalIgnoreCase)))
            .Take(query.MaxResults)
            .ToList();

    /// <summary>
    /// When a child directory matches, drop ancestor directories from the same
    /// result set so users do not see redundant nested parents (SCAN-FR-025 UX).
    /// </summary>
    internal static IReadOnlyList<FileEntry> SuppressNestedDirectoryAncestors(IReadOnlyList<FileEntry> directories)
    {
        if (directories.Count <= 1)
        {
            return directories;
        }

        return directories
            .Where(dir => !directories.Any(other =>
                !string.Equals(dir.Path, other.Path, StringComparison.OrdinalIgnoreCase) &&
                other.Path.StartsWith(dir.Path.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    private bool MatchesQuery(int i, SearchQuery query, long size) =>
        size >= query.MinSizeBytes &&
        (query.NameContains is null ||
         _name[i].Contains(query.NameContains, StringComparison.OrdinalIgnoreCase));

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
