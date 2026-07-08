namespace CCZen.Engine.Index;

/// <summary>Aggregated subtree metrics for rule-engine queries (SCAN-FR-028).</summary>
public readonly record struct SubtreeStats(
    long AllocatedSize,
    long LogicalSize,
    int FileCount,
    DateTime MaxLastWriteUtc);

/// <summary>
/// Read-only query surface over a <see cref="FileSystemIndex"/> for the
/// rules engine (SCAN-FR-022/028). All operations are in-memory.
/// </summary>
public interface IIndexQuery
{
    bool TryResolvePrefix(string absolutePath, out int rootNodeIndex);

    string GetPath(int nodeIndex);

    SubtreeStats GetSubtreeStats(int nodeIndex);

    IEnumerable<string> FindDirectoriesByName(int rootNodeIndex, IReadOnlySet<string> dirNames, int maxDepth);

    IEnumerable<FileEntry> FindFilesByExtension(int rootNodeIndex, IReadOnlySet<string> extensions, bool recursive);

    IEnumerable<string> ExpandGlob(string pattern);

    bool SubtreeContainsExtension(int nodeIndex, IReadOnlySet<string> extensions);
}
