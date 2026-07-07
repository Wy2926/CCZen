using System.Collections.ObjectModel;
using CCZen.App.Models;
using CCZen.App.Services;
using CCZen.Engine.Rules;
using CCZen.Engine.Safety;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CCZen.App.ViewModels;

/// <summary>
/// View model for the main window: recommend → one-click clean → undo
/// (specs/05 M4). Talks to the engine only via <see cref="IEngineClient"/>.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly IEngineClient _engine;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _summary = string.Empty;

    [ObservableProperty]
    private string _status = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CleanCommand))]
    private bool _canClean;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UndoCommand))]
    private string? _lastBatchId;

    private string? _lastVolumeRoot;

    public MainViewModel(IEngineClient engine)
    {
        _engine = engine;
    }

    public ObservableCollection<RecommendationGroup> Groups { get; } = [];

    [RelayCommand]
    private Task RecommendAsync() => RunGuardedAsync(LoadRecommendationsAsync);

    [RelayCommand(CanExecute = nameof(CanClean))]
    private Task CleanAsync() => RunGuardedAsync(ExecuteCleanAsync);

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private Task UndoAsync() => RunGuardedAsync(RestoreLastBatchAsync);

    private bool CanUndo() => LastBatchId is not null;

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

        _lastVolumeRoot = Path.GetPathRoot(plan.Items[0].Path);
        LastBatchId = plan.BatchId;
        Status = $"批次 {plan.BatchId}：{quarantined}/{results.Count} 项已移入隔离区，可撤销。";
    }

    private async Task RestoreLastBatchAsync()
    {
        if (LastBatchId is not { } batchId || _lastVolumeRoot is not { } volumeRoot)
        {
            return;
        }

        IReadOnlyList<ItemResult> results = await _engine.RestoreBatchAsync(volumeRoot, batchId);
        Status = $"批次 {batchId}：{results.Count} 项已还原。";
        LastBatchId = null;
        _lastVolumeRoot = null;
    }

    private async Task RunGuardedAsync(Func<Task> action)
    {
        IsBusy = true;
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            Status = $"出错：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
