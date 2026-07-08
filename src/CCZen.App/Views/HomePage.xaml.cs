using System.Windows.Controls;

namespace CCZen.App.Views;

public partial class HomePage : UserControl
{
    public HomePage()
    {
        InitializeComponent();
        DataContext = App.Cleaner;
    }
}
