using System.Windows.Controls;

namespace CCZen.App.Views;

public partial class QuarantinePage : UserControl
{
    public QuarantinePage()
    {
        InitializeComponent();
        DataContext = App.Cleaner;
    }
}
