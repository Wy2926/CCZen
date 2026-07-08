namespace CCZen.Engine.Index;

/// <summary>
/// Default <see cref="IIndexQuery"/> over a built <see cref="FileSystemIndex"/>.
/// </summary>
public sealed class IndexQuery : IIndexQuery
{
    private readonly FileSystemIndex _index;
    private Dictionary<string, int>? _pathToNode;
    private List<int>[]? _children;

    public IndexQuery(FileSystemIndex index)
    {
        _index = index;
    }

    public bool TryResolvePrefix(string absolutePath, out int rootNodeIndex)
    {
        EnsurePathMap();
        string normalized = NormalizePath(absolutePath);
        return _pathToNode!.TryGetValue(normalized, out rootNodeIndex);
    }

    public string GetPath(int nodeIndex) => _index.GetPath(nodeIndex);

    public SubtreeStats GetSubtreeStats(int nodeIndex) => _index.GetSubtreeStats(nodeIndex);

    public IEnumerable<string> FindDirectoriesByName(int rootNodeIndex, IReadOnlySet<string> dirNames, int maxDepth)
    {
        EnsureChildren();
        var queue = new Queue<(int Node, int Depth)>();
        queue.Enqueue((rootNodeIndex, 0));

        while (queue.Count > 0)
        {
            (int node, int depth) = queue.Dequeue();
            if (depth > maxDepth)
            {
                continue;
            }

            if (depth > 0 && _index.IsDirectoryNode(node) && dirNames.Contains(_index.GetNodeName(node)))
            {
                yield return _index.GetPath(node);
            }

            if (depth == maxDepth)
            {
                continue;
            }

            foreach (int child in _children![node])
            {
                if (_index.IsDirectoryNode(child))
                {
                    queue.Enqueue((child, depth + 1));
                }
            }
        }
    }

    public IEnumerable<FileEntry> FindFilesByExtension(
        int rootNodeIndex, IReadOnlySet<string> extensions, bool recursive)
    {
        if (recursive)
        {
            foreach (int node in Descendants(rootNodeIndex))
            {
                if (!_index.IsDirectoryNode(node) && MatchesExtension(node, extensions))
                {
                    yield return _index.ToFileEntry(node);
                }
            }
        }
        else
        {
            EnsureChildren();
            foreach (int child in _children![rootNodeIndex])
            {
                if (!_index.IsDirectoryNode(child) && MatchesExtension(child, extensions))
                {
                    yield return _index.ToFileEntry(child);
                }
            }
        }
    }

    public IEnumerable<string> ExpandGlob(string pattern)
    {
        string normalized = pattern.Replace('/', '\\');
        int middle = normalized.IndexOf("\\*\\", StringComparison.Ordinal);
        if (middle >= 0)
        {
            string prefix = normalized[..middle];
            string suffix = normalized[(middle + 3)..];
            if (!TryResolvePrefix(prefix, out int prefixNode))
            {
                yield break;
            }

            EnsureChildren();
            foreach (int child in _children![prefixNode])
            {
                if (!_index.IsDirectoryNode(child))
                {
                    continue;
                }

                string candidate = _index.GetPath(child) + "\\" + suffix;
                if (TryResolvePrefix(candidate, out _))
                {
                    yield return candidate;
                }
            }

            yield break;
        }

        if (normalized.EndsWith("\\*", StringComparison.Ordinal))
        {
            string prefix = normalized[..^2];
            if (!TryResolvePrefix(prefix, out int prefixNode))
            {
                yield break;
            }

            EnsureChildren();
            foreach (int child in _children![prefixNode])
            {
                if (_index.IsDirectoryNode(child))
                {
                    yield return _index.GetPath(child);
                }
            }

            yield break;
        }

        if (TryResolvePrefix(normalized, out _))
        {
            yield return NormalizePath(normalized);
        }
    }

    public bool SubtreeContainsExtension(int nodeIndex, IReadOnlySet<string> extensions)
    {
        foreach (int descendant in Descendants(nodeIndex))
        {
            if (!_index.IsDirectoryNode(descendant) && MatchesExtension(descendant, extensions))
            {
                return true;
            }
        }

        return false;
    }

    private IEnumerable<int> Descendants(int rootNodeIndex)
    {
        EnsureChildren();
        var stack = new Stack<int>();
        stack.Push(rootNodeIndex);
        while (stack.Count > 0)
        {
            int node = stack.Pop();
            yield return node;
            foreach (int child in _children![node])
            {
                stack.Push(child);
            }
        }
    }

    private bool MatchesExtension(int nodeIndex, IReadOnlySet<string> extensions)
    {
        string ext = Path.GetExtension(_index.GetNodeName(nodeIndex));
        return extensions.Contains(ext);
    }

    private void EnsurePathMap()
    {
        if (_pathToNode is not null)
        {
            return;
        }

        _pathToNode = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < _index.Count; i++)
        {
            _pathToNode[NormalizePath(_index.GetPath(i))] = i;
        }
    }

    private void EnsureChildren()
    {
        if (_children is not null)
        {
            return;
        }

        _children = new List<int>[_index.Count];
        for (int i = 0; i < _index.Count; i++)
        {
            _children[i] = [];
        }

        for (int i = 0; i < _index.Count; i++)
        {
            int parent = _index.GetParentIndex(i);
            if (parent >= 0)
            {
                _children[parent].Add(i);
            }
        }
    }

    private static string NormalizePath(string path)
    {
        string normalized = path.Replace('/', '\\').TrimEnd('\\');
        if (normalized.Length == 2 && normalized[1] == ':')
        {
            return normalized + "\\";
        }

        return normalized;
    }
}
