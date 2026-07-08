using CommunityToolkit.Mvvm.ComponentModel;

namespace CCZen.App.ViewModels;

/// <summary>
/// App settings. Most options are UI placeholders until the corresponding
/// engine features land (tray residency: specs/05 D7; retention: specs/04);
/// theme switching is applied immediately. Values are in-memory only for now.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    [ObservableProperty]
    private int _themeIndex;

    [ObservableProperty]
    private bool _lowPriorityIo = true;

    [ObservableProperty]
    private bool _trayResident;

    [ObservableProperty]
    private bool _confirmBeforeClean = true;

    /// <summary>0 = 移入隔离区（可撤销）, 1 = 直接永久删除.</summary>
    [ObservableProperty]
    private int _cleanModeIndex;

    [ObservableProperty]
    private int _retentionIndex = 1;

    public string Version { get; } = "0.1.0（预览）";

    partial void OnThemeIndexChanged(int value) => App.ApplyTheme(dark: value == 0);
}
