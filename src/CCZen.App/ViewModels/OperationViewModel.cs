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

    public bool HasStatus => Status.Length > 0;

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
