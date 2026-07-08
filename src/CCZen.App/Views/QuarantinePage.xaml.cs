using System.Collections.Specialized;
using CCZen.App.Models;
using CCZen.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CCZen.App.Views;

public sealed partial class QuarantinePage : Page
{
    public QuarantinePage()
    {
        InitializeComponent();
        Loaded += (_, _) => ViewModel.BatchHistory.CollectionChanged += OnHistoryChanged;
        Unloaded += (_, _) => ViewModel.BatchHistory.CollectionChanged -= OnHistoryChanged;
    }

    public CleanerViewModel ViewModel => App.Cleaner;

    public Visibility IsEmptyVisible =>
        ViewModel.BatchHistory.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    private void OnHistoryChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        Bindings.Update();

    private void OnRestoreClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is QuarantineBatchRow batch)
        {
            ViewModel.RestoreBatchCommand.Execute(batch);
        }
    }
}
