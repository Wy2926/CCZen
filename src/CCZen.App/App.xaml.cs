using CCZen.App.Services;
using CCZen.App.ViewModels;
using Microsoft.UI.Xaml;

namespace CCZen.App;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
    }

    /// <summary>Shared engine connection reused by every page.</summary>
    public static IEngineClient Engine { get; } = new EngineClient();

    /// <summary>Shared view models so page state survives navigation.</summary>
    public static CleanerViewModel Cleaner { get; } = new(Engine);

    public static SearchViewModel Search { get; } = new(Engine);

    public static DashboardViewModel Dashboard { get; } = new();

    public static AdaptersViewModel Adapters { get; } = new();

    public static SettingsViewModel Settings { get; } = new();

    public static MainWindow? Window { get; private set; }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Window = new MainWindow();
        Window.Activate();
    }
}
