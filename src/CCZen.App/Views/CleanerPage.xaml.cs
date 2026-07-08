using System.Windows.Controls;

namespace CCZen.App.Views;

public partial class CleanerPage : UserControl
{
    public CleanerPage()
    {
        InitializeComponent();
        DataContext = App.Cleaner;
    }
}
