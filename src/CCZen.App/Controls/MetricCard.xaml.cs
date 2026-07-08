using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CCZen.App.Controls;

public partial class MetricCard : UserControl
{
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(MetricCard),
            new PropertyMetadata("", OnChanged));

    public static readonly DependencyProperty IconProperty =
        DependencyProperty.Register(nameof(Icon), typeof(string), typeof(MetricCard),
            new PropertyMetadata("", OnChanged));

    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(string), typeof(MetricCard),
            new PropertyMetadata("", OnChanged));

    public static readonly DependencyProperty SubValueProperty =
        DependencyProperty.Register(nameof(SubValue), typeof(string), typeof(MetricCard),
            new PropertyMetadata("", OnChanged));

    public static readonly DependencyProperty ProgressProperty =
        DependencyProperty.Register(nameof(Progress), typeof(double), typeof(MetricCard),
            new PropertyMetadata(-1.0, OnChanged));

    public static readonly DependencyProperty ColorProperty =
        DependencyProperty.Register(nameof(Color), typeof(string), typeof(MetricCard),
            new PropertyMetadata("Accent", OnChanged));

    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string Icon { get => (string)GetValue(IconProperty); set => SetValue(IconProperty, value); }
    public string Value { get => (string)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public string SubValue { get => (string)GetValue(SubValueProperty); set => SetValue(SubValueProperty, value); }
    public double Progress { get => (double)GetValue(ProgressProperty); set => SetValue(ProgressProperty, value); }
    public string Color { get => (string)GetValue(ColorProperty); set => SetValue(ColorProperty, value); }

    public MetricCard()
    {
        InitializeComponent();
        UpdateVisual();
    }

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((MetricCard)d).UpdateVisual();

    private void UpdateVisual()
    {
        IconText.Text = Icon;
        TitleText.Text = Title;
        ValueText.Text = Value;
        SubValueText.Text = SubValue;
        SubValueText.Visibility = string.IsNullOrEmpty(SubValue) ? Visibility.Collapsed : Visibility.Visible;

        Brush accentBrush = Color switch
        {
            "Success" => FindBrush("SuccessBrush"),
            "Warning" => FindBrush("WarningBrush"),
            "Error" => FindBrush("ErrorBrush"),
            "Info" => FindBrush("InfoBrush"),
            _ => FindBrush("AccentBrush"),
        };
        IconText.Foreground = accentBrush;

        if (Progress >= 0)
        {
            ProgressArea.Visibility = Visibility.Visible;
            ProgressBar.Value = Progress;
            ProgressBar.Foreground = accentBrush;
            ProgressText.Text = $"{Progress:F0}%";
        }
        else
        {
            ProgressArea.Visibility = Visibility.Collapsed;
        }
    }

    private Brush FindBrush(string key)
        => TryFindResource(key) as Brush ?? Brushes.Gray;
}
