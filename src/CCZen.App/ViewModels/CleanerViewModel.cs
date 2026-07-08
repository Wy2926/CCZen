using System.Collections.ObjectModel;
using System.IO;
using CCZen.App.Models;
using CCZen.App.Services;
using CCZen.Engine.Rules;
using CCZen.Engine.Safety;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CCZen.App.ViewModels;

/// <summary>
/// Smart-clean pipeline: recommend → check items → clean selected → undo
/// (specs/05 M4). Selection defaults to T0/T1; T2 needs an explicit checkbox
/// opt-in and a confirmation dialog before it enters the plan. Talks to the
/// engine only via <see cref="IEngineClient"/>; executed batches are kept as
/// history rows for the quarantine/undo center.
/// </summary>
public sealed partial class CleanerViewModel : OperationViewModel
{
    private static readonly string[] ScanPhases =
    [
        "正在发现应用环境（注册表 / 路径 / 进程）…",
        "正在评估应用适配器规则…",
        "正在评估通用清理规则…",
        "正在评分并分级（T0–T3）…",
        "正在汇总推荐结果…",
    ];

    private readonly IEngineClient _engine;

    [ObservableProperty]
    private string _summary = string.Empty;

    [ObservableProperty]
    private bool _hasScanned;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CleanSelectedCommand))]
    private int _selectedCount;

    [ObservableProperty]
    private string _selectionSummary = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UndoCommand))]
    private QuarantineBatchRow? _lastBatch;

    public CleanerViewModel(IEngineClient engine)
    {
        _engine = engine;
    }

    public ObservableCollection<RuleGroup> Groups { get; } = [];

    public ObservableCollection<QuarantineBatchRow> BatchHistory { get; } = [];

    /// <summary>Set by the shell to show a modal confirm dialog; defaults to auto-confirm.</summary>
    public Func<string, string, Task<bool>> ConfirmInteraction { get; set; } =
        (_, _) => Task.FromResult(true);

    /// <summary>When true (settings), a confirm dialog is shown before every clean.</summary>
    public Func<bool> ConfirmBeforeClean { get; set; } = () => true;

    [RelayCommand]
    private Task RecommendAsync() => RunGuardedAsync(LoadRecommendationsAsync, ScanPhases);

    [RelayCommand(CanExecute = nameof(CanClean))]
    private Task CleanSelectedAsync() => RunGuardedAsync(ExecuteCleanAsync);

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private Task UndoAsync() => RunGuardedAsync(() => RestoreAsync(LastBatch!));

    [RelayCommand]
    private Task RestoreBatchAsync(QuarantineBatchRow batch) => RunGuardedAsync(() => RestoreAsync(batch));

    private bool CanClean() => SelectedCount > 0;

    private bool CanUndo() => LastBatch is not null;

    private async Task LoadRecommendationsAsync()
    {
        IReadOnlyList<Recommendation> recommendations = await _engine.RecommendAsync();

        Groups.Clear();
        var groups = recommendations
            .GroupBy(r => r.RuleId)
            .Select(g => new RuleGroup(g.Key, [.. g]))
            .OrderBy(g => g.Tier)
            .ThenByDescending(g => g.TotalBytes);
        foreach (RuleGroup group in groups)
        {
            group.SelectionChanged += (_, _) => RecomputeSelection();
            Groups.Add(group);
        }

        long cleanableBytes = recommendations.Where(r => r.Action != "report-only").Sum(r => r.SizeBytes);
        Summary = $"共 {recommendations.Count} 项推荐（{Groups.Count} 组），可清理约 {SizeFormatter.Format(cleanableBytes)}";
        HasScanned = true;
        Status = string.Empty;
        RecomputeSelection();
    }

    private void RecomputeSelection()
    {
        var selected = SelectedItems();
        SelectedCount = selected.Count;
        SelectionSummary = selected.Count == 0
            ? "未选择任何项"
            : $"已选 {selected.Count} 项 · 约 {SizeFormatter.Format(selected.Sum(i => i.SizeBytes))}";
    }

    private List<SelectableRecommendation> SelectedItems() =>
        Groups.SelectMany(g => g.Items).Where(i => i.IsSelected).ToList();

    private async Task ExecuteCleanAsync()
    {
        var selected = SelectedItems();
        if (selected.Count == 0)
        {
            Status = "请先勾选需要清理的项目。";
            return;
        }

        var t2Paths = selected.Where(i => i.Tier == Tier.T2).Select(i => i.Path).ToList();
        long totalBytes = selected.Sum(i => i.SizeBytes);
        if (ConfirmBeforeClean() || t2Paths.Count > 0)
        {
            string t2Hint = t2Paths.Count > 0 ? $"（含 {t2Paths.Count} 项 T2 需确认项）" : string.Empty;
            bool confirmed = await ConfirmInteraction(
                "确认清理所选项目？",
                $"将把 {selected.Count} 项（约 {SizeFormatter.Format(totalBytes)}）移入隔离区{t2Hint}。操作可在隔离区整批撤销。");
            if (!confirmed)
            {
                Status = "已取消清理。";
                return;
            }
        }

        var selectedPaths = selected.Select(i => i.Path).ToList();
        BatchPlan plan = await _engine.PlanCleanAsync(t2Paths, selectedPaths);
        if (plan.Items.Count == 0)
        {
            Status = "所选项目均不可清理（受保护或已消失）。";
            return;
        }

        IReadOnlyList<ItemResult> results = await _engine.ExecuteBatchAsync(plan.BatchId);
        int quarantined = results.Count(r => r.Outcome == ItemOutcome.Quarantined);

        var batch = new QuarantineBatchRow(
            plan.BatchId,
            Path.GetPathRoot(plan.Items[0].Path) ?? "C:\\",
            $"{quarantined}/{results.Count} 项已移入隔离区",
            DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
        BatchHistory.Insert(0, batch);
        LastBatch = batch;
        Status = $"批次 {plan.BatchId}：{quarantined}/{results.Count} 项已移入隔离区，可撤销。";
    }

    /// <summary>Records a batch executed elsewhere (e.g. large-file quarantine).</summary>
    public void RecordBatch(QuarantineBatchRow batch)
    {
        BatchHistory.Insert(0, batch);
        LastBatch = batch;
    }

    private async Task RestoreAsync(QuarantineBatchRow batch)
    {
        IReadOnlyList<ItemResult> results = await _engine.RestoreBatchAsync(batch.VolumeRoot, batch.BatchId);
        Status = $"批次 {batch.BatchId}：{results.Count} 项已还原。";
        BatchHistory.Remove(batch);
        if (LastBatch == batch)
        {
            LastBatch = null;
        }
    }
}
