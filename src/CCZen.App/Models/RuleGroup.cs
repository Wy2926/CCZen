using CCZen.Engine.Rules;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CCZen.App.Models;

/// <summary>
/// One collapsible group of recommendations sharing the same rule/adapter,
/// with a tri-state group checkbox: checked = all selectable items selected,
/// unchecked = none, indeterminate = mixed.
/// </summary>
public sealed partial class RuleGroup : ObservableObject
{
    private bool _updatingFromItems;

    [ObservableProperty]
    private bool? _groupChecked;

    [ObservableProperty]
    private bool _isExpanded;

    public RuleGroup(string ruleId, IReadOnlyList<Recommendation> hits)
    {
        RuleId = ruleId;
        Recommendation first = hits[0];
        Tier = first.Tier;
        Title = first.Explain;
        (Icon, FriendlyTitle) = Categorize(ruleId, first.Explain);
        TotalBytes = hits.Sum(r => r.SizeBytes);
        Detail = $"{hits.Count} 项 · {SizeFormatter.Format(TotalBytes)}";
        Items = hits
            .OrderByDescending(r => r.SizeBytes)
            .Select(r => new SelectableRecommendation(r))
            .ToList();
        HasSelectableItems = Items.Any(i => i.IsSelectable);
        foreach (SelectableRecommendation item in Items)
        {
            item.SelectionChanged += (_, _) => OnItemSelectionChanged();
        }

        RecomputeGroupState();
    }

    public string RuleId { get; }

    public Tier Tier { get; }

    public string Title { get; }

    /// <summary>Emoji icon for the plain-language category card.</summary>
    public string Icon { get; }

    /// <summary>Plain-language category name shown on the one-click clean page.</summary>
    public string FriendlyTitle { get; }

    public string Detail { get; }

    public long TotalBytes { get; }

    public IReadOnlyList<SelectableRecommendation> Items { get; }

    public bool HasSelectableItems { get; }

    /// <summary>Raised whenever any item selection inside this group changes.</summary>
    public event EventHandler? SelectionChanged;

    partial void OnGroupCheckedChanged(bool? value)
    {
        if (_updatingFromItems || value is null)
        {
            return;
        }

        _updatingFromItems = true;
        foreach (SelectableRecommendation item in Items.Where(i => i.IsSelectable))
        {
            item.IsSelected = value.Value;
        }

        _updatingFromItems = false;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnItemSelectionChanged()
    {
        if (_updatingFromItems)
        {
            return;
        }

        RecomputeGroupState();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Maps a rule/adapter id to a novice-friendly category icon and name.</summary>
    private static (string Icon, string Title) Categorize(string ruleId, string fallback)
    {
        string prefix = ruleId.Split('/')[0];
        return prefix switch
        {
            "chrome" => ("🌐", "Chrome 浏览器缓存"),
            "edge" => ("🌐", "Edge 浏览器缓存"),
            "firefox" => ("🌐", "Firefox 浏览器缓存"),
            "wechat" => ("💬", "微信缓存文件"),
            "telegram" => ("💬", "Telegram 缓存"),
            "steam" => ("🎮", "Steam 游戏缓存"),
            "npm" or "nuget" or "pip" or "gradle" or "maven" => ("🧰", $"开发工具缓存（{prefix}）"),
            "gpu-shader-cache" => ("🖥", "显卡着色器缓存"),
            "system-user-temp" => ("🗂", "系统临时文件"),
            "system-thumbnail-cache" => ("🖼", "缩略图缓存"),
            "system-wer-reports" => ("📋", "系统错误报告"),
            "generic-app-cache" => ("📦", "应用缓存文件"),
            "generic-log-dump-files" => ("📄", "日志与转储文件"),
            "generic-bak-files" => ("💾", "备份残留文件"),
            "generic-installer-leftover" => ("📥", "安装包残留"),
            _ => ("🧹", fallback),
        };
    }

    private void RecomputeGroupState()
    {
        var selectable = Items.Where(i => i.IsSelectable).ToList();
        int selected = selectable.Count(i => i.IsSelected);
        _updatingFromItems = true;
        GroupChecked = selectable.Count == 0 || selected == 0
            ? false
            : selected == selectable.Count ? true : null;
        _updatingFromItems = false;
    }
}
