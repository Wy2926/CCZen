using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CCZen.Engine.Rules;

namespace CCZen.App.Controls;

/// <summary>Colored risk-tier pill: T0 安全 / T1 较安全 / T2 需确认 / T3 仅提示 (specs/02).</summary>
public partial class TierBadge : UserControl
{
    public static readonly DependencyProperty TierProperty =
        DependencyProperty.Register(nameof(Tier), typeof(Tier), typeof(TierBadge),
            new PropertyMetadata(Tier.T0, OnChanged));

    public Tier Tier { get => (Tier)GetValue(TierProperty); set => SetValue(TierProperty, value); }

    public TierBadge()
    {
        InitializeComponent();
        UpdateVisual();
    }

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((TierBadge)d).UpdateVisual();

    private void UpdateVisual()
    {
        (string label, string badgeKey, string textKey) = Tier switch
        {
            Tier.T0 => ("T0 安全", "Tier0BadgeBrush", "Tier0TextBrush"),
            Tier.T1 => ("T1 较安全", "Tier1BadgeBrush", "Tier1TextBrush"),
            Tier.T2 => ("T2 需确认", "Tier2BadgeBrush", "Tier2TextBrush"),
            _ => ("T3 仅提示", "Tier3BadgeBrush", "Tier3TextBrush"),
        };
        LabelText.Text = label;
        Pill.Background = FindBrush(badgeKey);
        LabelText.Foreground = FindBrush(textKey);
    }

    private Brush FindBrush(string key)
        => TryFindResource(key) as Brush ?? Brushes.Gray;
}
