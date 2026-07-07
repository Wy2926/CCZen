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
    private readonly Dictionary<ulong, int> _frnToIndex = new();
    private readonly ulong _rootFrn;

    public IndexBuilder(ulong rootFrn)
    {
        _rootFrn = rootFrn;
        AddEntry(rootFrn, 0, string.Empty, isDirectory: true);
    }

    public int Count => _frn.Count;

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
    }

    public bool TryGetIndex(ulong frn, out int index) => _frnToIndex.TryGetValue(frn, out index);

    public bool IsDirectory(int index) => _isDirectory[index];

    public ulong GetFrn(int index) => _frn[index];

    public void SetSizes(int index, long logicalSize, long allocatedSize)
    {
        _logicalSize[index] = logicalSize;
        _allocatedSize[index] = allocatedSize;
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
        int orphanIndex = -1;
        var parent = new int[count + 1];
        for (int i = 0; i < count; i++)
        {
            if (_frn[i] == _rootFrn)
            {
                parent[i] = -1;
            }
            else if (_frnToIndex.TryGetValue(_parentFrn[i], out int p) && _isDirectory[p])
            {
                parent[i] = p;
            }
            else
            {
                if (orphanIndex < 0)
                {
                    orphanIndex = count;
                }

                parent[i] = orphanIndex;
            }
        }

        int total = orphanIndex < 0 ? count : count + 1;
        var name = new string[total];
        var isDirectory = new bool[total];
        var logicalSize = new long[total];
        var allocatedSize = new long[total];
        _name.CopyTo(name);
        _isDirectory.CopyTo(isDirectory);
        _logicalSize.CopyTo(logicalSize);
        _allocatedSize.CopyTo(allocatedSize);

        if (orphanIndex >= 0)
        {
            name[orphanIndex] = "<orphaned>";
            isDirectory[orphanIndex] = true;
            parent[orphanIndex] = _frnToIndex[_rootFrn];
        }
        else
        {
            parent = parent[..count];
        }

        return new FileSystemIndex(rootLabel, parent, name, isDirectory, logicalSize, allocatedSize);
    }
}
