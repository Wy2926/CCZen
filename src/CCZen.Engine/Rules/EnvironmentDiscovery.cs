using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace CCZen.Engine.Rules;

/// <summary>
/// Builds the <see cref="EnvironmentModel"/> from the live machine
/// (spec: RULE-FR-001..006). All sources are public APIs: Known Folders via
/// <see cref="Environment.GetFolderPath(Environment.SpecialFolder)"/>, the
/// registry Uninstall convention, process snapshots, and DriveInfo.
/// </summary>
[SupportedOSPlatform("windows")]
public static class EnvironmentDiscovery
{
    public static EnvironmentModel Discover()
    {
        var symbols = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        void Bind(string symbol, string? path)
        {
            if (!string.IsNullOrEmpty(path))
            {
                symbols[symbol] = Path.TrimEndingDirectorySeparator(path);
            }
        }

        // RULE-FR-001: Known Folders handle folder redirection.
        Bind("TEMP", Path.GetTempPath());
        Bind("LOCALAPPDATA", Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        Bind("APPDATA", Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
        Bind("USERPROFILE", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        Bind("DOCUMENTS", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        Bind("PROGRAMDATA", Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData));
        Bind("WINDIR", Environment.GetFolderPath(Environment.SpecialFolder.Windows));
        Bind("PROGRAMFILES", Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
        Bind("PROGRAMFILES_X86", Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
        string? profile = symbols.GetValueOrDefault("USERPROFILE");
        if (profile is not null)
        {
            Bind("DOWNLOADS", Path.Combine(profile, "Downloads"));
        }

        // RULE-FR-004: package manager cache redirections (official configuration knobs).
        Bind("NUGET_PACKAGES", Environment.GetEnvironmentVariable("NUGET_PACKAGES")
            ?? (profile is null ? null : Path.Combine(profile, ".nuget", "packages")));
        string? localAppData = symbols.GetValueOrDefault("LOCALAPPDATA");
        Bind("PIP_CACHE_DIR", Environment.GetEnvironmentVariable("PIP_CACHE_DIR")
            ?? (localAppData is null ? null : Path.Combine(localAppData, "pip", "cache")));
        Bind("GRADLE_USER_HOME", Environment.GetEnvironmentVariable("GRADLE_USER_HOME")
            ?? (profile is null ? null : Path.Combine(profile, ".gradle")));
        Bind("CARGO_HOME", Environment.GetEnvironmentVariable("CARGO_HOME")
            ?? (profile is null ? null : Path.Combine(profile, ".cargo")));

        return new EnvironmentModel
        {
            Symbols = symbols,
            InstalledApps = ReadInstalledApps(),
            RunningProcesses = SnapshotProcesses(),
            Volumes = ReadVolumes(),
        };
    }

    /// <summary>RULE-FR-002: HKLM/HKCU × 64/32-bit registry views of the Uninstall convention.</summary>
    private static List<InstalledApp> ReadInstalledApps()
    {
        var apps = new List<InstalledApp>();
        foreach (RegistryHive hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        {
            foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                using RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, view);
                using RegistryKey? uninstall = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                if (uninstall is null)
                {
                    continue;
                }

                foreach (string subKeyName in uninstall.GetSubKeyNames())
                {
                    using RegistryKey? key = uninstall.OpenSubKey(subKeyName);
                    if (key?.GetValue("DisplayName") is not string name || string.IsNullOrWhiteSpace(name))
                    {
                        continue;
                    }

                    apps.Add(new InstalledApp(
                        name,
                        key.GetValue("DisplayVersion") as string,
                        key.GetValue("InstallLocation") as string,
                        key.GetValue("Publisher") as string));
                }
            }
        }

        return apps;
    }

    /// <summary>RULE-FR-005: lower-cased running process names.</summary>
    private static HashSet<string> SnapshotProcesses()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Process process in Process.GetProcesses())
        {
            using (process)
            {
                names.Add(process.ProcessName);
            }
        }

        return names;
    }

    /// <summary>RULE-FR-006: ready volumes only; free-space never relaxes safety thresholds.</summary>
    private static List<VolumeInfo> ReadVolumes()
    {
        var volumes = new List<VolumeInfo>();
        foreach (DriveInfo drive in DriveInfo.GetDrives())
        {
            if (drive.IsReady && drive.DriveType == DriveType.Fixed)
            {
                volumes.Add(new VolumeInfo(drive.Name, drive.DriveFormat, drive.TotalSize, drive.AvailableFreeSpace));
            }
        }

        return volumes;
    }
}
