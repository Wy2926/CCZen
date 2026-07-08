using CCZen.Engine.Index;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CCZen.App.Models;

/// <summary>One large-file search hit with a selection checkbox.</summary>
public sealed partial class SelectableSearchRow : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected;

    public SelectableSearchRow(FileEntry entry)
    {
        Kind = entry.IsDirectory ? "目录" : "文件";
        Path = entry.Path;
        SizeBytes = entry.AllocatedSize;
        SizeText = SizeFormatter.Format(entry.AllocatedSize);
        Detail = entry.IsDirectory ? $"{entry.FileCount:N0} 个文件" : string.Empty;
    }

    public string Kind { get; }

    public string Path { get; }

    public long SizeBytes { get; }

    public string SizeText { get; }

    public string Detail { get; }

    public event EventHandler? SelectionChanged;

    partial void OnIsSelectedChanged(bool value) => SelectionChanged?.Invoke(this, EventArgs.Empty);
}
