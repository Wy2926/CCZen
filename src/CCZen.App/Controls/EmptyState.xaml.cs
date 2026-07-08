using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace CCZen.App.Controls;

public partial class EmptyState : UserControl
{
    public static readonly DependencyProperty IconProperty =
        DependencyProperty.Register(nameof(Icon), typeof(string), typeof(EmptyState),
            new PropertyMetadata("📋", OnChanged));

    public static readonly DependencyProperty MessageProperty =
        DependencyProperty.Register(nameof(Message), typeof(string), typeof(EmptyState),
            new PropertyMetadata("暂无数据", OnChanged));

    public static readonly DependencyProperty ActionTextProperty =
        DependencyProperty.Register(nameof(ActionText), typeof(string), typeof(EmptyState),
            new PropertyMetadata("", OnChanged));

    public static readonly DependencyProperty ActionCommandProperty =
        DependencyProperty.Register(nameof(ActionCommand), typeof(ICommand), typeof(EmptyState),
            new PropertyMetadata(null, OnChanged));

    public string Icon { get => (string)GetValue(IconProperty); set => SetValue(IconProperty, value); }
    public string Message { get => (string)GetValue(MessageProperty); set => SetValue(MessageProperty, value); }
    public string ActionText { get => (string)GetValue(ActionTextProperty); set => SetValue(ActionTextProperty, value); }
    public ICommand? ActionCommand { get => (ICommand?)GetValue(ActionCommandProperty); set => SetValue(ActionCommandProperty, value); }

    public EmptyState()
    {
        InitializeComponent();
        UpdateVisual();
    }

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((EmptyState)d).UpdateVisual();

    private void UpdateVisual()
    {
        IconText.Text = Icon;
        MessageText.Text = Message;
        ActionBtn.Content = ActionText;
        ActionBtn.Command = ActionCommand;
        ActionBtn.Visibility = string.IsNullOrEmpty(ActionText) ? Visibility.Collapsed : Visibility.Visible;
    }
}
