using System.Runtime.Versioning;
using System.Security.Principal;

namespace CCZen.Engine.Scanning;

[SupportedOSPlatform("windows5.1.2600")]
public static class VolumeScannerFactory
{
    /// <summary>
    /// Chooses the NTFS fast path when the volume is NTFS and the process is
    /// elevated; otherwise falls back to directory traversal (SCAN-FR-040).
    /// </summary>
    public static IVolumeScanner Create(string root)
    {
        // The USN fast path always enumerates the whole volume, so it only
        // applies when the requested root is the volume root itself.
        bool isVolumeRoot = root.Length <= 3 && root.Length >= 2 && root[1] == ':';
        if (!isVolumeRoot)
        {
            return new FallbackScanner();
        }

        var drive = new DriveInfo(root);
        bool isNtfs = string.Equals(drive.DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase);
        return isNtfs && IsElevated() ? new UsnJournalScanner() : new FallbackScanner();
    }

    public static bool IsElevated()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }
}
