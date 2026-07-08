using System.Windows;
using System.Windows.Controls;

namespace CCZen.App.Views;

public partial class DashboardPage : UserControl
{
    public DashboardPage()
    {
        InitializeComponent();
        DataContext = App.Dashboard;
    }

    private void OnQuickActionClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag } && Window.GetWindow(this) is MainWindow main)
        {
            main.NavigateTo(tag);
        }
    }
}
