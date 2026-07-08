using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CCZen.App.Controls;

public partial class StatusIndicator : UserControl
{
    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(nameof(Label), typeof(string), typeof(StatusIndicator),
            new PropertyMetadata("", OnStateChanged));

    public static readonly DependencyProperty StatusProperty =
        DependencyProperty.Register(nameof(Status), typeof(string), typeof(StatusIndicator),
            new PropertyMetadata("Unknown", OnStateChanged));

    public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }

    /// <summary>Online (green), Offline (red), Warning (orange), Busy (accent), else gray.</summary>
    public string Status { get => (string)GetValue(StatusProperty); set => SetValue(StatusProperty, value); }

    public StatusIndicator()
    {
        InitializeComponent();
        UpdateVisual();
    }

    private static void OnStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((StatusIndicator)d).UpdateVisual();

    private void UpdateVisual()
    {
        Brush color = Status switch
        {
            "Online" => FindBrush("SuccessBrush"),
            "Offline" => FindBrush("ErrorBrush"),
            "Warning" => FindBrush("WarningBrush"),
            "Busy" => FindBrush("AccentBrush"),
            _ => FindBrush("SecondaryTextBrush"),
        };
        Dot.Fill = color;
        LabelText.Text = Label;
        LabelText.Foreground = FindBrush("SecondaryTextBrush");
        LabelText.Visibility = string.IsNullOrEmpty(Label) ? Visibility.Collapsed : Visibility.Visible;
    }

    private Brush FindBrush(string key)
        => TryFindResource(key) as Brush ?? Brushes.Gray;
}
