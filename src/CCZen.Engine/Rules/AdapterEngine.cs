namespace CCZen.Engine.Rules;

/// <summary>
/// Evaluates adapter manifests against the environment model
/// (spec: 03 ADPT-FR-002/004/005/006). Detected adapters emit
/// recommendations; adapter-claimed paths take priority over generic
/// heuristics — merge with <see cref="Merge"/>.
/// </summary>
public sealed class AdapterEngine
{
    private readonly EnvironmentModel _environment;
    private readonly AdapterPack _pack;

    public AdapterEngine(EnvironmentModel environment, AdapterPack pack)
    {
        _environment = environment;
        _pack = pack;
    }

    public IReadOnlyList<Recommendation> Evaluate()
    {
        var recommendations = new List<Recommendation>();
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Adapter adapter in _pack.Adapters)
        {
            if (!IsDetected(adapter))
            {
                continue;
            }

            // ADPT-FR-002: configProbe binds extra symbols (e.g. user-migrated data dirs).
            IReadOnlyDictionary<string, string> probedSymbols = RunConfigProbes(adapter);

            // ADPT-FR-005: installed version outside verifiedVersions → demote all items one tier.
            bool demote = IsVersionOutsideVerifiedRange(adapter);

            bool appRunning = adapter.Detect.ProcessNames?
                .Any(p => _environment.RunningProcesses.Contains(p)) == true;

            foreach (AdapterItem item in adapter.Items)
            {
                foreach (string target in item.Targets)
                {
                    foreach (string path in ExpandItemTarget(target, probedSymbols))
                    {
                        if (!claimed.Add(path))
                        {
                            continue;
                        }

                        (long size, DateTime lastWrite) = MeasureTree(path);
                        if (size == 0)
                        {
                            continue;
                        }

                        // ADPT-FR-004: app running → report-only, never delete.
                        bool blocked = item.RequiresAppNotRunning && appRunning;
                        Tier tier = Enum.Parse<Tier>(item.Tier);
                        if (demote)
                        {
                            tier = tier switch { Tier.T0 => Tier.T1, Tier.T1 => Tier.T2, _ => Tier.T3 };
                        }

                        recommendations.Add(new Recommendation(
                            path,
                            IsDirectory: Directory.Exists(path),
                            size,
                            RuleId: $"{adapter.Id}/{item.Id}",
                            tier,
                            Confidence: blocked ? 0 : 0.9,
                            Action: blocked ? "report-only" : "quarantine",
                            Explain: blocked ? $"{item.Explain}（{adapter.Name} 正在运行，仅提示）" : item.Explain,
                            Signals: new Dictionary<string, double> { ["adapter"] = 1.0 }));
                    }
                }
            }
        }

        return recommendations;
    }

    /// <summary>ADPT-FR-006: adapter-claimed paths take over generic heuristic hits on the same path or inside it.</summary>
    public static IReadOnlyList<Recommendation> Merge(
        IReadOnlyList<Recommendation> adapterHits, IReadOnlyList<Recommendation> genericHits)
    {
        var merged = new List<Recommendation>(adapterHits);
        foreach (Recommendation generic in genericHits)
        {
            bool covered = adapterHits.Any(a =>
                string.Equals(a.Path, generic.Path, StringComparison.OrdinalIgnoreCase) ||
                generic.Path.StartsWith(a.Path + "\\", StringComparison.OrdinalIgnoreCase));
            if (!covered)
            {
                merged.Add(generic);
            }
        }

        return merged;
    }

    private bool IsDetected(Adapter adapter)
    {
        if (adapter.Detect.UninstallNamePatterns is { Count: > 0 } patterns &&
            _environment.InstalledApps.Any(app => patterns.Any(p =>
                app.Name.Contains(p, StringComparison.OrdinalIgnoreCase))))
        {
            return true;
        }

        if (adapter.Detect.PathPatterns is { Count: > 0 } paths &&
            paths.Select(_environment.Expand).Any(p => p is not null && (Directory.Exists(p) || File.Exists(p))))
        {
            return true;
        }

        if (adapter.Detect.ProcessNames is { Count: > 0 } processes &&
            processes.Any(p => _environment.RunningProcesses.Contains(p)))
        {
            return true;
        }

        return false;
    }

    private IReadOnlyDictionary<string, string> RunConfigProbes(Adapter adapter)
    {
        if (adapter.Detect.ConfigProbes is not { Count: > 0 } probes)
        {
            return new Dictionary<string, string>();
        }

        var bound = new Dictionary<string, string>();
        foreach (ConfigProbe probe in probes)
        {
            if (ConfigProbeReader.Read(probe, _environment) is { Length: > 0 } value)
            {
                bound[probe.Symbol] = Path.TrimEndingDirectorySeparator(value);
            }
        }

        return bound;
    }

    /// <summary>ADPT-FR-005: true when the installed version falls outside "min-max" (inclusive).</summary>
    private bool IsVersionOutsideVerifiedRange(Adapter adapter)
    {
        if (adapter.VerifiedVersions is null ||
            adapter.Detect.UninstallNamePatterns is not { Count: > 0 } patterns)
        {
            return false;
        }

        InstalledApp? app = _environment.InstalledApps.FirstOrDefault(a =>
            patterns.Any(p => a.Name.Contains(p, StringComparison.OrdinalIgnoreCase)));
        if (app?.Version is null || !Version.TryParse(Pad(app.Version), out Version? installed))
        {
            return false;
        }

        string[] range = adapter.VerifiedVersions.Split('-', 2);
        bool belowMin = Version.TryParse(Pad(range[0]), out Version? min) && installed < min;
        bool aboveMax = range.Length == 2 && Version.TryParse(Pad(range[1]), out Version? max) && installed > max;
        return belowMin || aboveMax;

        static string Pad(string v) => v.Contains('.') ? v : v + ".0";
    }

    /// <summary>Expands a symbolized target; a trailing "\*" enumerates immediate children (per-profile items).</summary>
    private IEnumerable<string> ExpandItemTarget(string target, IReadOnlyDictionary<string, string> extraSymbols)
    {
        string normalized = ApplyExtraSymbols(target.Replace('/', '\\'), extraSymbols);
        bool perChild = normalized.EndsWith("\\*", StringComparison.Ordinal);
        int wildcard = normalized.IndexOf("\\*\\", StringComparison.Ordinal);

        if (wildcard >= 0)
        {
            // Middle wildcard, e.g. "${LOCALAPPDATA}\Google\Chrome\User Data\*\Cache".
            string? prefix = _environment.Expand(normalized[..wildcard]);
            string suffix = normalized[(wildcard + 3)..];
            if (prefix is null || !Directory.Exists(prefix))
            {
                yield break;
            }

            foreach (string child in Directory.EnumerateDirectories(prefix))
            {
                string candidate = Path.Combine(child, suffix);
                if (Directory.Exists(candidate) || File.Exists(candidate))
                {
                    yield return candidate;
                }
            }

            yield break;
        }

        string? expanded = _environment.Expand(perChild ? normalized[..^2] : normalized);
        if (expanded is null)
        {
            yield break;
        }

        if (perChild)
        {
            if (Directory.Exists(expanded))
            {
                foreach (string child in Directory.EnumerateDirectories(expanded))
                {
                    yield return child;
                }
            }
        }
        else if (Directory.Exists(expanded) || File.Exists(expanded))
        {
            yield return expanded;
        }
    }

    /// <summary>Substitutes probe-bound symbols before the environment expands the rest.</summary>
    private static string ApplyExtraSymbols(string target, IReadOnlyDictionary<string, string> extraSymbols)
    {
        foreach ((string symbol, string value) in extraSymbols)
        {
            target = target.Replace("${" + symbol + "}", value, StringComparison.Ordinal);
        }

        return target;
    }

    private static (long Size, DateTime LastWriteUtc) MeasureTree(string path)
    {
        if (File.Exists(path))
        {
            var info = new FileInfo(path);
            return (info.Length, info.LastWriteTimeUtc);
        }

        long size = 0;
        DateTime lastWrite = DateTime.MinValue;
        var options = new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = true };
        foreach (string file in Directory.EnumerateFiles(path, "*", options))
        {
            var info = new FileInfo(file);
            size += info.Length;
            if (info.LastWriteTimeUtc > lastWrite)
            {
                lastWrite = info.LastWriteTimeUtc;
            }
        }

        return (size, lastWrite);
    }
}
