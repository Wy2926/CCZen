using System.Collections.ObjectModel;
using System.IO;
using CCZen.App.Models;
using CCZen.App.Services;
using CCZen.Engine.Index;
using CCZen.Engine.Safety;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CCZen.App.ViewModels;

/// <summary>
/// View model for conditional large file/directory search (SCAN-FR-025):
/// scans on first use, then queries the in-memory index instantly. Checked
/// results can be moved to quarantine (reversible per batch).
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

    /// <summary>Combo order: 0 全部 / 1 仅文件 / 2 仅目录.</summary>
    private static readonly SearchKind[] KindByComboIndex =
        [SearchKind.All, SearchKind.Files, SearchKind.Directories];

    [ObservableProperty]
    private int _kindIndex;

    [ObservableProperty]
    private string _indexStatus = "索引未构建 — 首次搜索时自动扫描系统卷";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(QuarantineSelectedCommand))]
    private int _selectedCount;

    [ObservableProperty]
    private string _selectionSummary = string.Empty;

    public SearchViewModel(IEngineClient engine)
    {
        _engine = engine;
    }

    public ObservableCollection<SelectableSearchRow> Results { get; } = [];

    /// <summary>Set by the shell to show a modal confirm dialog; defaults to auto-confirm.</summary>
    public Func<string, string, Task<bool>> ConfirmInteraction { get; set; } =
        (_, _) => Task.FromResult(true);

    /// <summary>Set by the shell so executed batches land in the quarantine center.</summary>
    public Action<QuarantineBatchRow>? BatchRecorded { get; set; }

    [RelayCommand]
    private Task SearchAsync() => RunGuardedAsync(
        async () =>
        {
            await EnsureIndexAsync();
            await RunSearchAsync();
        },
        ScanPhases);

    [RelayCommand(CanExecute = nameof(CanQuarantine))]
    private Task QuarantineSelectedAsync() => RunGuardedAsync(ExecuteQuarantineAsync);

    private bool CanQuarantine() => SelectedCount > 0;

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
            KindByComboIndex[KindIndex],
            ParseMinSizeBytes(),
            string.IsNullOrWhiteSpace(NameFilter) ? null : NameFilter.Trim(),
            MaxResults);

        IReadOnlyList<FileEntry> entries = await _engine.SearchAsync(query);

        Results.Clear();
        foreach (FileEntry entry in entries)
        {
            var row = new SelectableSearchRow(entry);
            row.SelectionChanged += (_, _) => RecomputeSelection();
            Results.Add(row);
        }

        RecomputeSelection();
        Status = $"命中 {entries.Count} 项（按占用空间降序，最多 {MaxResults} 项）。";
    }

    private void RecomputeSelection()
    {
        var selected = Results.Where(r => r.IsSelected).ToList();
        SelectedCount = selected.Count;
        SelectionSummary = selected.Count == 0
            ? "未选择任何项"
            : $"已选 {selected.Count} 项 · 约 {SizeFormatter.Format(selected.Sum(r => r.SizeBytes))}";
    }

    private async Task ExecuteQuarantineAsync()
    {
        var selected = Results.Where(r => r.IsSelected).ToList();
        if (selected.Count == 0)
        {
            Status = "请先勾选需要隔离的项目。";
            return;
        }

        long totalBytes = selected.Sum(r => r.SizeBytes);
        bool confirmed = await ConfirmInteraction(
            "确认移入隔离区？",
            $"将把 {selected.Count} 项（约 {SizeFormatter.Format(totalBytes)}）移入隔离区。文件不会被删除，可在隔离区整批还原。");
        if (!confirmed)
        {
            Status = "已取消。";
            return;
        }

        BatchPlan plan = await _engine.PlanQuarantineAsync(selected.Select(r => r.Path).ToList());
        if (plan.Items.Count == 0)
        {
            Status = "所选项目均不可隔离（受保护或已消失）。";
            return;
        }

        IReadOnlyList<ItemResult> results = await _engine.ExecuteBatchAsync(plan.BatchId);
        int quarantined = results.Count(r => r.Outcome == ItemOutcome.Quarantined);

        var batch = new QuarantineBatchRow(
            plan.BatchId,
            Path.GetPathRoot(plan.Items[0].Path) ?? "C:\\",
            $"{quarantined}/{results.Count} 项已移入隔离区（大文件搜索）",
            DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
        BatchRecorded?.Invoke(batch);

        foreach (SelectableSearchRow row in selected)
        {
            Results.Remove(row);
        }

        RecomputeSelection();
        Status = $"批次 {plan.BatchId}：{quarantined}/{results.Count} 项已移入隔离区，可在隔离区还原。";
    }

    private long ParseMinSizeBytes() =>
        double.TryParse(MinSizeMb, out double mb) && mb > 0
            ? (long)(mb * 1024 * 1024)
            : 0;
}
