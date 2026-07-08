using System.Windows;
using CCZen.App.Services;
using CCZen.App.ViewModels;

namespace CCZen.App;

public partial class App : Application
{
    /// <summary>Shared engine connection reused by every page.</summary>
    public static IEngineClient Engine { get; } = new EngineClient();

    /// <summary>Shared view models so page state survives navigation.</summary>
    public static CleanerViewModel Cleaner { get; } = new(Engine);

    public static SearchViewModel Search { get; } = new(Engine);

    public static DashboardViewModel Dashboard { get; } = new();

    public static AdaptersViewModel Adapters { get; } = new();

    public static SettingsViewModel Settings { get; } = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        Cleaner.ConfirmBeforeClean = () => Settings.ConfirmBeforeClean;
        Cleaner.DirectDeleteMode = () => Settings.CleanModeIndex == 1;
        Search.BatchRecorded = Cleaner.RecordBatch;
        base.OnStartup(e);
    }

    /// <summary>Swaps the merged theme dictionary at runtime (dark is default).</summary>
    public static void ApplyTheme(bool dark)
    {
        var themeUri = dark
            ? new Uri("Resources/Themes/DarkTheme.xaml", UriKind.Relative)
            : new Uri("Resources/Themes/LightTheme.xaml", UriKind.Relative);
        Current.Resources.MergedDictionaries[0] = new ResourceDictionary { Source = themeUri };
    }
}
