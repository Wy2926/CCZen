using System.Windows;
using System.Windows.Controls;
using CCZen.App.Models;

namespace CCZen.App.Controls;

public partial class DeleteConfirmDialog : UserControl
{
    private TaskCompletionSource<DeleteConfirmResult?>? _tcs;

    public DeleteConfirmDialog()
    {
        InitializeComponent();
    }

    /// <summary>Show delete confirmation; returns null when cancelled.</summary>
    public Task<DeleteConfirmResult?> Show(string title, string message, bool defaultUseQuarantine)
    {
        TitleText.Text = title;
        MessageText.Text = message;
        QuarantineCheckBox.IsChecked = defaultUseQuarantine;
        _tcs = new TaskCompletionSource<DeleteConfirmResult?>();
        Visibility = Visibility.Visible;
        return _tcs.Task;
    }

    private void Close(DeleteConfirmResult? result)
    {
        Visibility = Visibility.Collapsed;
        _tcs?.TrySetResult(result);
    }

    private void OnConfirmClick(object sender, RoutedEventArgs e) =>
        Close(new DeleteConfirmResult(Confirmed: true, UseQuarantine: QuarantineCheckBox.IsChecked == true));

    private void OnCancelClick(object sender, RoutedEventArgs e) => Close(null);
}
