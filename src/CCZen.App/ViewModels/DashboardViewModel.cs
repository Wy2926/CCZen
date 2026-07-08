using System.Collections.ObjectModel;
using CCZen.App.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CCZen.App.ViewModels;

/// <summary>Dashboard: fixed-drive usage overview and quick actions.</summary>
public sealed partial class DashboardViewModel : ObservableObject
{
    public DashboardViewModel()
    {
        Refresh();
    }

    public ObservableCollection<DriveRow> Drives { get; } = [];

    [RelayCommand]
    private void Refresh()
    {
        Drives.Clear();
        foreach (DriveInfo drive in DriveInfo.GetDrives())
        {
            if (drive is { IsReady: true, DriveType: DriveType.Fixed })
            {
                Drives.Add(DriveRow.From(drive));
            }
        }
    }
}
