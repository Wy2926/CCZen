using System.Collections.ObjectModel;
using CCZen.App.Models;
using CCZen.App.Services;
using CCZen.Engine.Rules;
using CCZen.Engine.Safety;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CCZen.App.ViewModels;

/// <summary>
/// Smart-clean pipeline: recommend → one-click clean → undo (specs/05 M4).
/// Talks to the engine only via <see cref="IEngineClient"/>; executed batches
/// are kept as history rows for the quarantine/undo center.
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
    [NotifyCanExecuteChangedFor(nameof(CleanCommand))]
    private bool _canClean;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UndoCommand))]
    private QuarantineBatchRow? _lastBatch;

    public CleanerViewModel(IEngineClient engine)
    {
        _engine = engine;
    }

    public ObservableCollection<RecommendationGroup> Groups { get; } = [];

    public ObservableCollection<QuarantineBatchRow> BatchHistory { get; } = [];

    [RelayCommand]
    private Task RecommendAsync() => RunGuardedAsync(LoadRecommendationsAsync, ScanPhases);

    [RelayCommand(CanExecute = nameof(CanClean))]
    private Task CleanAsync() => RunGuardedAsync(ExecuteCleanAsync);

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private Task UndoAsync() => RunGuardedAsync(() => RestoreAsync(LastBatch!));

    [RelayCommand]
    private Task RestoreBatchAsync(QuarantineBatchRow batch) => RunGuardedAsync(() => RestoreAsync(batch));

    private bool CanUndo() => LastBatch is not null;

    private async Task LoadRecommendationsAsync()
    {
        IReadOnlyList<Recommendation> recommendations = await _engine.RecommendAsync();

        Groups.Clear();
        var groups = recommendations
            .GroupBy(r => r.RuleId)
            .Select(g => RecommendationGroup.From([.. g]))
            .OrderBy(g => g.Tier)
            .ThenByDescending(g => g.TotalBytes);
        foreach (RecommendationGroup group in groups)
        {
            Groups.Add(group);
        }

        long cleanableBytes = recommendations.Where(r => r.Action != "report-only").Sum(r => r.SizeBytes);
        Summary = $"共 {recommendations.Count} 项推荐（{Groups.Count} 组），可自动清理约 {SizeFormatter.Format(cleanableBytes)}";
        CanClean = recommendations.Any(r => r.Action != "report-only");
        HasScanned = true;
        Status = string.Empty;
    }

    private async Task ExecuteCleanAsync()
    {
        BatchPlan plan = await _engine.PlanCleanAsync();
        if (plan.Items.Count == 0)
        {
            Status = "没有可自动清理的 T0/T1 项。";
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
