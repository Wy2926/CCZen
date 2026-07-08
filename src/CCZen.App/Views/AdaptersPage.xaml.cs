using System.Windows.Controls;

namespace CCZen.App.Views;

public partial class AdaptersPage : UserControl
{
    public AdaptersPage()
    {
        InitializeComponent();
        DataContext = App.Adapters;
    }
}
