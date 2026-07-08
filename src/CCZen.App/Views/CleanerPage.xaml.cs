using CCZen.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CCZen.App.Views;

public sealed partial class CleanerPage : Page
{
    public CleanerPage()
    {
        InitializeComponent();
    }

    public CleanerViewModel ViewModel => App.Cleaner;

    public Visibility IsEmptyStateVisible(bool hasScanned, bool isBusy) =>
        !hasScanned && !isBusy ? Visibility.Visible : Visibility.Collapsed;
}
