using System.Collections.ObjectModel;
using System.IO;
using CCZen.App.Models;
using CCZen.Engine.Safety;
using CCZen.Engine.Service;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CCZen.App.ViewModels;

/// <summary>
/// Base class for view models that run long engine operations and surface
/// staged progress (value + phase text) to a determinate progress bar.
/// The engine does not stream progress yet, so phases advance on a smooth
/// eased curve and snap to 100% on completion.
/// </summary>
public abstract partial class OperationViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private double _progressValue;

    [ObservableProperty]
    private string _progressPhase = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string _status = string.Empty;

    [ObservableProperty]
    private bool _isExecutingBatch;

    public bool HasStatus => Status.Length > 0;

    public ObservableCollection<ExecuteProgressRow> ExecuteProgressItems { get; } = [];

    protected void PrepareExecuteProgress(IEnumerable<string> paths)
    {
        ExecuteProgressItems.Clear();
        foreach (string path in paths)
        {
            ExecuteProgressItems.Add(new ExecuteProgressRow(path));
        }

        IsExecutingBatch = ExecuteProgressItems.Count > 0;
    }

    protected Progress<ExecuteProgress> CreateBatchProgressReporter(string verb)
    {
        return new Progress<ExecuteProgress>(p =>
        {
            ProgressValue = p.Total == 0 ? 100 : 100.0 * p.Done / p.Total;
            ProgressPhase = $"{verb} {p.Done}/{p.Total}：{Path.GetFileName(p.CurrentPath)}";
            UpdateExecuteProgressRows(p);
        });
    }

    protected void FinalizeExecuteProgress(IReadOnlyList<ItemResult> results)
    {
        foreach (ItemResult result in results)
        {
            ExecuteProgressRow? row = ExecuteProgressItems.FirstOrDefault(r =>
                string.Equals(r.Path, result.Path, StringComparison.OrdinalIgnoreCase));
            if (row is null)
            {
                continue;
            }

            if (result.Outcome is ItemOutcome.Quarantined or ItemOutcome.Deleted)
            {
                row.Status = ExecuteItemStatus.Completed;
                row.Detail = result.Outcome == ItemOutcome.Deleted ? "已永久删除" : "已移入隔离区";
            }
            else
            {
                row.Status = ExecuteItemStatus.Skipped;
                row.Detail = new SkippedItemRow(result).Reason;
            }
        }

        foreach (ExecuteProgressRow row in ExecuteProgressItems.Where(r => r.Status == ExecuteItemStatus.Pending))
        {
            row.Status = ExecuteItemStatus.Skipped;
            row.Detail = "未处理";
        }

        IsExecutingBatch = false;
    }

    private void UpdateExecuteProgressRows(ExecuteProgress progress)
    {
        ExecuteProgressRow? current = ExecuteProgressItems.FirstOrDefault(r =>
            string.Equals(r.Path, progress.CurrentPath, StringComparison.OrdinalIgnoreCase));
        if (current is not null)
        {
            current.Status = ExecuteItemStatus.Running;
        }

        for (int i = 0; i < ExecuteProgressItems.Count && i < progress.Done; i++)
        {
            ExecuteProgressRow row = ExecuteProgressItems[i];
            if (row.Status is ExecuteItemStatus.Pending or ExecuteItemStatus.Running &&
                !string.Equals(row.Path, progress.CurrentPath, StringComparison.OrdinalIgnoreCase))
            {
                row.Status = ExecuteItemStatus.Completed;
            }
        }
    }

    protected async Task RunGuardedAsync(Func<Task> action, IReadOnlyList<string>? phases = null)
    {
        IsBusy = true;
        ProgressValue = 0;
        using var cts = new CancellationTokenSource();
        Task ticker = phases is { Count: > 0 }
            ? AnimateProgressAsync(phases, cts.Token)
            : Task.CompletedTask;
        try
        {
            await action();
            ProgressValue = 100;
        }
        catch (Exception ex)
        {
            Status = $"出错：{ex.Message}";
        }
        finally
        {
            await cts.CancelAsync();
            await ticker;
            IsBusy = false;
            ProgressPhase = string.Empty;
        }
    }

    private async Task AnimateProgressAsync(IReadOnlyList<string> phases, CancellationToken cancellationToken)
    {
        ProgressPhase = phases[0];
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(150, cancellationToken);
                ProgressValue += (95 - ProgressValue) * 0.05;
                int index = Math.Min((int)(ProgressValue / 95 * phases.Count), phases.Count - 1);
                ProgressPhase = phases[index];
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
}
