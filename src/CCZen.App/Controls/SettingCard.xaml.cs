using System.Windows;
using System.Windows.Controls;

namespace CCZen.App.Controls;

/// <summary>PowerToys-style settings row: icon + header/description + trailing action.</summary>
public partial class SettingCard : UserControl
{
    public static readonly DependencyProperty IconProperty =
        DependencyProperty.Register(nameof(Icon), typeof(string), typeof(SettingCard),
            new PropertyMetadata("", OnChanged));

    public static readonly DependencyProperty HeaderProperty =
        DependencyProperty.Register(nameof(Header), typeof(string), typeof(SettingCard),
            new PropertyMetadata("", OnChanged));

    public static readonly DependencyProperty DescriptionProperty =
        DependencyProperty.Register(nameof(Description), typeof(string), typeof(SettingCard),
            new PropertyMetadata("", OnChanged));

    public static readonly DependencyProperty ActionContentProperty =
        DependencyProperty.Register(nameof(ActionContent), typeof(object), typeof(SettingCard),
            new PropertyMetadata(null));

    public string Icon { get => (string)GetValue(IconProperty); set => SetValue(IconProperty, value); }
    public string Header { get => (string)GetValue(HeaderProperty); set => SetValue(HeaderProperty, value); }
    public string Description { get => (string)GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }
    public object? ActionContent { get => GetValue(ActionContentProperty); set => SetValue(ActionContentProperty, value); }

    public SettingCard()
    {
        InitializeComponent();
        UpdateVisual();
    }

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((SettingCard)d).UpdateVisual();

    private void UpdateVisual()
    {
        IconText.Text = Icon;
        IconText.Visibility = string.IsNullOrEmpty(Icon) ? Visibility.Collapsed : Visibility.Visible;
        HeaderText.Text = Header;
        DescriptionText.Text = Description;
        DescriptionText.Visibility = string.IsNullOrEmpty(Description) ? Visibility.Collapsed : Visibility.Visible;
    }
}
