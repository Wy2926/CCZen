using CCZen.Engine.Rules;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CCZen.App.Models;

/// <summary>
/// One recommendation row with a selection checkbox. T0/T1 default to
/// selected; T2 requires an explicit opt-in; T3/report-only rows can never
/// be selected (specs/02 red line: report-only is informational).
/// </summary>
public sealed partial class SelectableRecommendation : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected;

    public SelectableRecommendation(Recommendation recommendation)
    {
        Tier = recommendation.Tier;
        Path = recommendation.Path;
        SizeBytes = recommendation.SizeBytes;
        SizeText = SizeFormatter.Format(recommendation.SizeBytes);
        IsReportOnly = recommendation.Action == "report-only";
        Explain = IsReportOnly ? $"[仅提示] {recommendation.Explain}" : recommendation.Explain;
        IsSelectable = !IsReportOnly && recommendation.Tier != Tier.T3;
        _isSelected = IsSelectable && recommendation.Tier is Tier.T0 or Tier.T1;
    }

    public Tier Tier { get; }

    public string Path { get; }

    public long SizeBytes { get; }

    public string SizeText { get; }

    public string Explain { get; }

    public bool IsReportOnly { get; }

    public bool IsSelectable { get; }

    /// <summary>Raised by the owning group to recompute tri-state + totals.</summary>
    public event EventHandler? SelectionChanged;

    partial void OnIsSelectedChanged(bool value) => SelectionChanged?.Invoke(this, EventArgs.Empty);
}
