using System.Collections.ObjectModel;
using System.IO;
using CCZen.App.Models;
using CCZen.App.Services;
using CCZen.Engine.Index;
using CCZen.Engine.Safety;
using CCZen.Engine.Service;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CCZen.App.ViewModels;

/// <summary>
/// View model for conditional large file/directory search (SCAN-FR-025):
/// scans on first use, then queries the in-memory index instantly. Checked
/// results can be deleted directly or moved to quarantine (reversible per batch).
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
    private string _indexStatus = IndexStatusFormatter.NotBuilt;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteSelectedCommand))]
    private int _selectedCount;

    [ObservableProperty]
    private string _selectionSummary = string.Empty;

    public SearchViewModel(IEngineClient engine)
    {
        _engine = engine;
    }

    public ObservableCollection<SelectableSearchRow> Results { get; } = [];

    /// <summary>Settings default: true = quarantine, false = direct delete.</summary>
    public Func<bool> DefaultUseQuarantine { get; set; } = () => true;

    /// <summary>Delete confirmation with quarantine toggle; returns null if cancelled.</summary>
    public Func<string, string, bool, Task<DeleteConfirmResult?>> ConfirmDeleteInteraction { get; set; } =
        (_, _, defaultUseQuarantine) => Task.FromResult<DeleteConfirmResult?>(
            new DeleteConfirmResult(Confirmed: true, UseQuarantine: defaultUseQuarantine));

    /// <summary>Set by the shell so quarantined batches land in the quarantine center.</summary>
    public Action<QuarantineBatchRow>? BatchRecorded { get; set; }

    [RelayCommand]
    private Task SearchAsync() => RunGuardedAsync(
        async () =>
        {
            await EnsureIndexAsync();
            await RunSearchAsync();
        },
        ScanPhases);

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private Task DeleteSelectedAsync() => RunGuardedAsync(ExecuteDeleteAsync);

    private bool CanDelete() => SelectedCount > 0;

    private async Task EnsureIndexAsync()
    {
        string root = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
        ScanSummary? existing = await _engine.GetStatusAsync();
        if (existing is not null &&
            string.Equals(NormalizeVolumeRoot(existing.Root), NormalizeVolumeRoot(root), StringComparison.OrdinalIgnoreCase))
        {
            IndexStatus = $"{IndexStatusFormatter.From(existing)} — 正在增量刷新…";
        }

        ScanSummary summary = await _engine.ScanAsync(root);
        IndexStatus = IndexStatusFormatter.From(summary);
    }

    private static string NormalizeVolumeRoot(string root) =>
        root.EndsWith('\\') ? root : root + "\\";

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
        Status = $"命中 {entries.Count} 项（按占用空间降序，最多 {MaxResults} 项；目录结果已去除嵌套父路径）。";
    }

    private void RecomputeSelection()
    {
        var selected = Results.Where(r => r.IsSelected).ToList();
        SelectedCount = selected.Count;
        SelectionSummary = selected.Count == 0
            ? "未选择任何项"
            : $"已选 {selected.Count} 项 · 约 {SizeFormatter.Format(selected.Sum(r => r.SizeBytes))}";
    }

    private async Task ExecuteDeleteAsync()
    {
        var selected = Results.Where(r => r.IsSelected).ToList();
        if (selected.Count == 0)
        {
            Status = "请先勾选需要删除的项目。";
            return;
        }

        long totalBytes = selected.Sum(r => r.SizeBytes);
        bool defaultQuarantine = DefaultUseQuarantine();
        DeleteConfirmResult? confirm = await ConfirmDeleteInteraction(
            "确认删除所选项目？",
            $"将处理 {selected.Count} 项（约 {SizeFormatter.Format(totalBytes)}）。可在下方选择移入隔离区或直接永久删除。",
            defaultQuarantine);
        if (confirm is null)
        {
            Status = "已取消。";
            return;
        }

        bool useQuarantine = confirm.UseQuarantine;
        BatchPlan plan = await _engine.PlanQuarantineAsync(selected.Select(r => r.Path).ToList());
        if (plan.Items.Count == 0)
        {
            Status = "所选项目均不可处理（受保护或已消失）。";
            return;
        }

        PrepareExecuteProgress(plan.Items.Select(i => i.Path));
        string verb = useQuarantine ? "正在移入隔离区" : "正在永久删除";
        IReadOnlyList<ItemResult> results = await _engine.ExecuteBatchAsync(
            plan.BatchId,
            permanentDelete: !useQuarantine,
            CreateBatchProgressReporter(verb));
        FinalizeExecuteProgress(results);

        int succeeded = results.Count(r => r.Outcome is ItemOutcome.Quarantined or ItemOutcome.Deleted);

        if (useQuarantine && succeeded > 0)
        {
            var batch = new QuarantineBatchRow(
                plan.BatchId,
                Path.GetPathRoot(plan.Items[0].Path) ?? "C:\\",
                $"{succeeded}/{results.Count} 项已移入隔离区（大文件搜索）",
                DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
            BatchRecorded?.Invoke(batch);
        }

        foreach (SelectableSearchRow row in results
                     .Where(r => r.Outcome is ItemOutcome.Quarantined or ItemOutcome.Deleted)
                     .Join(selected, r => r.Path, s => s.Path, (_, s) => s, StringComparer.OrdinalIgnoreCase))
        {
            Results.Remove(row);
        }

        RecomputeSelection();
        string actionLabel = useQuarantine ? "移入隔离区" : "永久删除";
        string failHint = succeeded < results.Count
            ? $"；{results.Count - succeeded} 项未处理（详见上方进度列表）"
            : string.Empty;
        Status = $"批次 {plan.BatchId}：{succeeded}/{results.Count} 项已{actionLabel}{failHint}。";
    }

    private long ParseMinSizeBytes() =>
        double.TryParse(MinSizeMb, out double mb) && mb > 0
            ? (long)(mb * 1024 * 1024)
            : 0;
}
