using CCZen.App.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CCZen.App;

/// <summary>
/// Application shell: PowerToys-style left navigation with a content frame.
/// All page state lives in shared view models (see <see cref="App"/>).
/// </summary>
public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        ContentFrame.Navigate(typeof(DashboardPage));
    }

    /// <summary>Programmatic navigation used by dashboard quick actions.</summary>
    public void NavigateTo(string tag)
    {
        foreach (object item in Nav.MenuItems)
        {
            if (item is NavigationViewItem navItem && (string?)navItem.Tag == tag)
            {
                Nav.SelectedItem = navItem;
                return;
            }
        }
    }

    public void SetTheme(ElementTheme theme)
    {
        if (Content is FrameworkElement root)
        {
            root.RequestedTheme = theme;
        }
    }

    private void OnNavSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
        {
            ContentFrame.Navigate(typeof(SettingsPage));
            return;
        }

        string? tag = (args.SelectedItem as NavigationViewItem)?.Tag as string;
        Type? page = tag switch
        {
            "dashboard" => typeof(DashboardPage),
            "cleaner" => typeof(CleanerPage),
            "largefiles" => typeof(LargeFilesPage),
            "duplicates" => typeof(DuplicatesPage),
            "adapters" => typeof(AdaptersPage),
            "quarantine" => typeof(QuarantinePage),
            _ => null,
        };

        if (page is not null && ContentFrame.CurrentSourcePageType != page)
        {
            ContentFrame.Navigate(page);
        }
    }
}
