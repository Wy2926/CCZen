using CommunityToolkit.Mvvm.ComponentModel;

namespace CCZen.App.Models;

public enum ExecuteItemStatus
{
    Pending,
    Running,
    Completed,
    Failed,
    Skipped,
}

/// <summary>One row in the per-item delete/quarantine progress list.</summary>
public sealed partial class ExecuteProgressRow : ObservableObject
{
    public ExecuteProgressRow(string path)
    {
        Path = path;
        _status = ExecuteItemStatus.Pending;
    }

    public string Path { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private ExecuteItemStatus _status;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private string _detail = string.Empty;

    public string StatusText => Status switch
    {
        ExecuteItemStatus.Pending => "等待",
        ExecuteItemStatus.Running => "处理中…",
        ExecuteItemStatus.Completed => "完成",
        ExecuteItemStatus.Failed => "失败",
        ExecuteItemStatus.Skipped => "跳过",
        _ => string.Empty,
    };
}
