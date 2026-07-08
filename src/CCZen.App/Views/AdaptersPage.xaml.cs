using CCZen.App.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace CCZen.App.Views;

public sealed partial class AdaptersPage : Page
{
    public AdaptersPage()
    {
        InitializeComponent();
    }

    public AdaptersViewModel ViewModel => App.Adapters;
}
