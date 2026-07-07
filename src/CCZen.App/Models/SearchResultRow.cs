using CCZen.Engine.Index;

namespace CCZen.App.Models;

/// <summary>Immutable display row for one search result <see cref="FileEntry"/>.</summary>
public sealed record SearchResultRow(string Kind, string Size, string Path, string Detail)
{
    public static SearchResultRow From(FileEntry entry) =>
        new(
            entry.IsDirectory ? "目录" : "文件",
            SizeFormatter.Format(entry.AllocatedSize),
            entry.Path,
            entry.IsDirectory ? $"{entry.FileCount:N0} 个文件" : string.Empty);
}
