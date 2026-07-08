using System.Windows;
using System.Windows.Controls;

namespace CCZen.App.Controls;

public partial class MessageDialog : UserControl
{
    private TaskCompletionSource<bool>? _tcs;

    public MessageDialog()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Show a confirmation dialog with OK/Cancel buttons. Returns true if confirmed.
    /// </summary>
    public Task<bool> ShowConfirm(string title, string message)
    {
        TitleText.Text = title;
        MessageText.Text = message;
        CancelBtn.Visibility = Visibility.Visible;
        _tcs = new TaskCompletionSource<bool>();
        Visibility = Visibility.Visible;
        return _tcs.Task;
    }

    /// <summary>
    /// Show an alert dialog with only an OK button.
    /// </summary>
    public Task ShowAlert(string title, string message)
    {
        TitleText.Text = title;
        MessageText.Text = message;
        CancelBtn.Visibility = Visibility.Collapsed;
        _tcs = new TaskCompletionSource<bool>();
        Visibility = Visibility.Visible;
        return _tcs.Task;
    }

    private void Close(bool result)
    {
        Visibility = Visibility.Collapsed;
        _tcs?.TrySetResult(result);
    }

    private void OnConfirmClick(object sender, RoutedEventArgs e) => Close(true);
    private void OnCancelClick(object sender, RoutedEventArgs e) => Close(false);
}
