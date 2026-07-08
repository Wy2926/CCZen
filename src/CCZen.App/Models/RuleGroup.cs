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
