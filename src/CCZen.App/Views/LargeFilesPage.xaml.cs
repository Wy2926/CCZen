using System.Windows.Controls;

namespace CCZen.App.Views;

public partial class LargeFilesPage : UserControl
{
    public LargeFilesPage()
    {
        InitializeComponent();
        DataContext = App.Search;
    }
}
