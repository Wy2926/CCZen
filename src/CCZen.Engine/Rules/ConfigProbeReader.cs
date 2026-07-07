using System.Runtime.Versioning;
using System.Text.Json;
using Microsoft.Win32;

namespace CCZen.Engine.Rules;

/// <summary>
/// Executes declarative config probes (spec: 03 ADPT-FR-002/003). Probes are
/// strictly read-only: a registry value, an INI key, or a JSON property is
/// read from the declared source and returned as a string. No code execution.
/// </summary>
public static class ConfigProbeReader
{
    /// <summary>Resolves one probe against this machine; null when the source or value is absent.</summary>
    public static string? Read(ConfigProbe probe, EnvironmentModel environment)
    {
        try
        {
            return probe.Kind switch
            {
                "registryValue" => ReadRegistry(probe),
                "iniValue" => ReadIni(probe, environment),
                "jsonValue" => ReadJson(probe, environment),
                _ => null,
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or JsonException)
        {
            return null;
        }
    }

    [SupportedOSPlatform("windows")]
    private static string? ReadRegistry(ConfigProbe probe)
    {
        if (!OperatingSystem.IsWindows() || probe.ValueName is null)
        {
            return null;
        }

        int separator = probe.Source.IndexOf('\\');
        if (separator < 0)
        {
            return null;
        }

        RegistryKey? hive = probe.Source[..separator].ToUpperInvariant() switch
        {
            "HKCU" or "HKEY_CURRENT_USER" => Registry.CurrentUser,
            "HKLM" or "HKEY_LOCAL_MACHINE" => Registry.LocalMachine,
            _ => null,
        };
        using RegistryKey? key = hive?.OpenSubKey(probe.Source[(separator + 1)..]);
        return key?.GetValue(probe.ValueName) as string;
    }

    private static string? ReadIni(ConfigProbe probe, EnvironmentModel environment)
    {
        string? path = environment.Expand(probe.Source.Replace('/', '\\'));
        if (path is null || !File.Exists(path) || probe.IniKey is null)
        {
            return null;
        }

        bool inSection = probe.IniSection is null;
        foreach (string raw in File.ReadLines(path))
        {
            string line = raw.Trim();
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                inSection = probe.IniSection is null ||
                    string.Equals(line[1..^1], probe.IniSection, StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inSection)
            {
                continue;
            }

            int equals = line.IndexOf('=');
            if (equals > 0 &&
                string.Equals(line[..equals].Trim(), probe.IniKey, StringComparison.OrdinalIgnoreCase))
            {
                string value = line[(equals + 1)..].Trim();
                return value.Length == 0 ? null : value;
            }
        }

        return null;
    }

    private static string? ReadJson(ConfigProbe probe, EnvironmentModel environment)
    {
        string? path = environment.Expand(probe.Source.Replace('/', '\\'));
        if (path is null || !File.Exists(path) || probe.JsonPath is null)
        {
            return null;
        }

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement element = document.RootElement;
        foreach (string segment in probe.JsonPath.Split('.'))
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(segment, out element))
            {
                return null;
            }
        }

        return element.ValueKind == JsonValueKind.String ? element.GetString() : null;
    }
}
