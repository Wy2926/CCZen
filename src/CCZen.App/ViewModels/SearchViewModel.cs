using System.Collections.ObjectModel;
using CCZen.App.Models;
using CCZen.App.Services;
using CCZen.Engine.Index;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CCZen.App.ViewModels;

/// <summary>
/// View model for conditional large file/directory search (SCAN-FR-025):
/// scans on first use, then queries the in-memory index instantly.
/// </summary>
public sealed partial class SearchViewModel : OperationViewModel
{
    private const int MaxResults = 100;

    private static readonly string[] ScanPhases =
    [
        "正在打开卷并读取 MFT / USN 日志…",
        "正在枚举文件记录…",
        "正在聚合目录占用空间…",
        "正在构建内存索引…",
        "正在执行条件搜索…",
    ];

    private readonly IEngineClient _engine;

    [ObservableProperty]
    private string _minSizeMb = "100";

    [ObservableProperty]
    private string _nameFilter = string.Empty;

    [ObservableProperty]
    private int _kindIndex = (int)SearchKind.All;

    [ObservableProperty]
    private string _indexStatus = "索引未构建 — 首次搜索时自动扫描系统卷";

    public SearchViewModel(IEngineClient engine)
    {
        _engine = engine;
    }

    public ObservableCollection<SearchResultRow> Results { get; } = [];

    [RelayCommand]
    private Task SearchAsync() => RunGuardedAsync(
        async () =>
        {
            await EnsureIndexAsync();
            await RunSearchAsync();
        },
        ScanPhases);

    private async Task EnsureIndexAsync()
    {
        if (await _engine.GetStatusAsync() is not null)
        {
            return;
        }

        string root = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
        var summary = await _engine.ScanAsync(root);
        IndexStatus = $"已索引 {root} — {summary.FileCount:N0} 个文件（{summary.ElapsedSeconds:0.00} s）";
    }

    private async Task RunSearchAsync()
    {
        var query = new SearchQuery(
            (SearchKind)KindIndex,
            ParseMinSizeBytes(),
            string.IsNullOrWhiteSpace(NameFilter) ? null : NameFilter.Trim(),
            MaxResults);

        IReadOnlyList<FileEntry> entries = await _engine.SearchAsync(query);

        Results.Clear();
        foreach (FileEntry entry in entries)
        {
            Results.Add(SearchResultRow.From(entry));
        }

        Status = $"命中 {entries.Count} 项（按占用空间降序，最多 {MaxResults} 项）。";
    }

    private long ParseMinSizeBytes() =>
        double.TryParse(MinSizeMb, out double mb) && mb > 0
            ? (long)(mb * 1024 * 1024)
            : 0;
}
