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
