namespace CCZen.Engine.Rules;

/// <summary>
/// Symbol-binding table produced by environment discovery (spec: RULE-FR-001..006).
/// Rules reference locations as symbols (e.g. <c>${TEMP}</c>, <c>${LOCALAPPDATA}</c>)
/// instead of literal absolute paths.
/// </summary>
public sealed class EnvironmentModel
{
    /// <summary>Symbol name (without ${}) to absolute path, e.g. "TEMP" → "C:\Users\x\AppData\Local\Temp".</summary>
    public required IReadOnlyDictionary<string, string> Symbols { get; init; }

    /// <summary>Installed applications from the registry Uninstall keys (RULE-FR-002).</summary>
    public required IReadOnlyList<InstalledApp> InstalledApps { get; init; }

    /// <summary>Names (lower-case, no extension) of running processes (RULE-FR-005).</summary>
    public required IReadOnlySet<string> RunningProcesses { get; init; }

    /// <summary>Ready volumes with capacity data (RULE-FR-006).</summary>
    public required IReadOnlyList<VolumeInfo> Volumes { get; init; }

    /// <summary>Expands ${SYMBOL} references in a rule target to an absolute path; null when a symbol is unbound.</summary>
    public string? Expand(string target)
    {
        int start = target.IndexOf("${", StringComparison.Ordinal);
        if (start < 0)
        {
            return target;
        }

        var result = new System.Text.StringBuilder();
        int position = 0;
        while (start >= 0)
        {
            int end = target.IndexOf('}', start);
            if (end < 0)
            {
                return null;
            }

            string symbol = target[(start + 2)..end];
            if (!Symbols.TryGetValue(symbol, out string? value))
            {
                return null;
            }

            result.Append(target, position, start - position).Append(value);
            position = end + 1;
            start = target.IndexOf("${", position, StringComparison.Ordinal);
        }

        return result.Append(target, position, target.Length - position).ToString();
    }
}

/// <summary>An installed application discovered from the registry (RULE-FR-002).</summary>
public sealed record InstalledApp(string Name, string? Version, string? InstallLocation, string? Publisher);

/// <summary>Volume capacity snapshot (RULE-FR-006). Free-space ratio affects display ordering only.</summary>
public sealed record VolumeInfo(string Root, string Format, long TotalSize, long AvailableFreeSpace);
