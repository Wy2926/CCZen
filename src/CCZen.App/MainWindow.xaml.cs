using System.Windows;
using System.Windows.Controls;
using CCZen.App.Controls;
using CCZen.App.Views;

namespace CCZen.App;

/// <summary>
/// Application shell: borderless window with custom title bar, left sidebar
/// navigation, status bar and a global modal dialog overlay. All page state
/// lives in shared view models (see <see cref="App"/>).
/// </summary>
public partial class MainWindow : Window
{
    private readonly Dictionary<string, UserControl> _pages = [];

    public MainWindow()
    {
        InitializeComponent();
        App.Cleaner.ConfirmInteraction = GlobalDialog.ShowConfirm;
        App.Cleaner.ConfirmDeleteInteraction = (title, message, defaultUseQuarantine) =>
            GlobalDeleteDialog.Show(title, message, defaultUseQuarantine);
        App.Search.ConfirmDeleteInteraction = (title, message, defaultUseQuarantine) =>
            GlobalDeleteDialog.Show(title, message, defaultUseQuarantine);
        App.Cleaner.PropertyChanged += (_, e) => OnOperationChanged(e.PropertyName);
        App.Search.PropertyChanged += (_, e) => OnOperationChanged(e.PropertyName);
        NavigateTo("home");
        Loaded += async (_, _) => await ProbeEngineAsync();
    }

    /// <summary>Programmatic navigation used by dashboard quick actions.</summary>
    public void NavigateTo(string tag)
    {
        RadioButton? button = tag switch
        {
            "home" => NavHome,
            "dashboard" => NavDashboard,
            "cleaner" => NavCleaner,
            "largefiles" => NavLargeFiles,
            "duplicates" => NavDuplicates,
            "adapters" => NavAdapters,
            "quarantine" => NavQuarantine,
            "settings" => NavSettings,
            _ => null,
        };
        if (button is null)
        {
            return;
        }

        button.IsChecked = true;
        ShowPage(tag);
    }

    private void OnNavChecked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string tag })
        {
            ShowPage(tag);
        }
    }

    private void ShowPage(string tag)
    {
        if (ContentHost is null)
        {
            return;
        }

        if (!_pages.TryGetValue(tag, out UserControl? page))
        {
            page = tag switch
            {
                "home" => new HomePage(),
                "cleaner" => new CleanerPage(),
                "largefiles" => new LargeFilesPage(),
                "duplicates" => new DuplicatesPage(),
                "adapters" => new AdaptersPage(),
                "quarantine" => new QuarantinePage(),
                "settings" => new SettingsPage(),
                "dashboard" => new DashboardPage(),
                _ => new HomePage(),
            };
            _pages[tag] = page;
        }

        ContentHost.Content = page;
    }

    private void OnOperationChanged(string? propertyName)
    {
        if (propertyName is not (nameof(App.Cleaner.IsBusy) or nameof(App.Cleaner.ProgressPhase)))
        {
            return;
        }

        bool busy = App.Cleaner.IsBusy || App.Search.IsBusy;
        StatusText.Text = busy
            ? (App.Cleaner.IsBusy ? App.Cleaner.ProgressPhase : App.Search.ProgressPhase)
            : "就绪";
    }

    private async Task ProbeEngineAsync()
    {
        try
        {
            await App.Engine.GetStatusAsync();
            EngineStatus.Status = "Online";
            EngineStatus.Label = "引擎已连接";
        }
        catch (Exception)
        {
            EngineStatus.Status = "Offline";
            EngineStatus.Label = "引擎不可用";
        }
    }

    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaxRestore(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        MaxRestoreBtn.Content = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
