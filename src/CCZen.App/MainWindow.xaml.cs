using CCZen.App.Services;
using CCZen.App.ViewModels;
using Microsoft.UI.Xaml;

namespace CCZen.App;

/// <summary>Main window shell; all logic lives in <see cref="MainViewModel"/>.</summary>
public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        var engine = new EngineClient();
        ViewModel = new MainViewModel(engine);
        SearchViewModel = new SearchViewModel(engine);
        InitializeComponent();
    }

    public MainViewModel ViewModel { get; }

    public SearchViewModel SearchViewModel { get; }
}
