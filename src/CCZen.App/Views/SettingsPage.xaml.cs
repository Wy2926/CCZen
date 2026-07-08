using CCZen.App.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace CCZen.App.Views;

public sealed partial class SettingsPage : Page
{
    public SettingsPage()
    {
        InitializeComponent();
    }

    public SettingsViewModel ViewModel => App.Settings;
}
