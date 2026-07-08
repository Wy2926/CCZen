using CCZen.App.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace CCZen.App.Views;

public sealed partial class LargeFilesPage : Page
{
    public LargeFilesPage()
    {
        InitializeComponent();
    }

    public SearchViewModel ViewModel => App.Search;
}
