using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.RestartManager;

namespace CCZen.Engine.Safety;

/// <summary>
/// Restart Manager occupancy probe (spec: SAFE-FR-021):
/// RmStartSession → RmRegisterResources → RmGetList. Occupied items are
/// skipped with an explanation — never force-unlocked (SAFE-FR-022).
/// </summary>
[SupportedOSPlatform("windows6.0.6000")]
public static class LockProbe
{
    private const int SessionKeyLength = 33; // CCH_RM_SESSION_KEY + 1

    /// <summary>
    /// Returns names of processes holding the given files, or an empty list
    /// when unlocked. Directories should pass representative files inside.
    /// </summary>
    public static unsafe IReadOnlyList<string> GetLockingProcesses(IReadOnlyList<string> filePaths)
    {
        if (filePaths.Count == 0)
        {
            return [];
        }

        char* sessionKey = stackalloc char[SessionKeyLength];
        if (PInvoke.RmStartSession(out uint sessionHandle, new PWSTR(sessionKey)) != WIN32_ERROR.ERROR_SUCCESS)
        {
            return [];
        }

        var handles = new GCHandle[filePaths.Count];
        try
        {
            var pointers = new PCWSTR[filePaths.Count];
            for (int i = 0; i < filePaths.Count; i++)
            {
                handles[i] = GCHandle.Alloc(filePaths[i], GCHandleType.Pinned);
                pointers[i] = new PCWSTR((char*)handles[i].AddrOfPinnedObject());
            }

            if (PInvoke.RmRegisterResources(sessionHandle, pointers.AsSpan(), rgApplications: default, rgsServiceNames: default)
                != WIN32_ERROR.ERROR_SUCCESS)
            {
                return [];
            }

            uint needed = 0;
            uint count = 0;
            uint rebootReasons;
            WIN32_ERROR result = PInvoke.RmGetList(sessionHandle, &needed, &count, null, &rebootReasons);
            if (result == WIN32_ERROR.ERROR_SUCCESS || needed == 0)
            {
                return [];
            }

            if (result != WIN32_ERROR.ERROR_MORE_DATA)
            {
                return [];
            }

            var processes = new RM_PROCESS_INFO[needed];
            count = needed;
            fixed (RM_PROCESS_INFO* first = processes)
            {
                result = PInvoke.RmGetList(sessionHandle, &needed, &count, first, &rebootReasons);
            }

            if (result != WIN32_ERROR.ERROR_SUCCESS)
            {
                return [];
            }

            var names = new List<string>((int)count);
            for (int i = 0; i < count; i++)
            {
                names.Add(processes[i].strAppName.ToString());
            }

            return names;
        }
        finally
        {
            foreach (GCHandle handle in handles)
            {
                if (handle.IsAllocated)
                {
                    handle.Free();
                }
            }

            PInvoke.RmEndSession(sessionHandle);
        }
    }

    /// <summary>Convenience probe for a single file.</summary>
    public static bool IsLocked(string filePath) => GetLockingProcesses([filePath]).Count > 0;
}
