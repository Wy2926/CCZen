using CCZen.Engine.Rules;

namespace CCZen.Engine.Tests;

/// <summary>
/// Pre-merge directory-walk baseline for adapter parity tests.
/// </summary>
internal sealed class AdapterWalkBaseline
{
    private readonly EnvironmentModel _environment;
    private readonly AdapterPack _pack;

    public AdapterWalkBaseline(EnvironmentModel environment, AdapterPack pack)
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

            IReadOnlyDictionary<string, string> probedSymbols = RunConfigProbes(adapter);
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

                        (long size, _) = MeasureTree(path);
                        if (size == 0)
                        {
                            continue;
                        }

                        bool blocked = item.RequiresAppNotRunning && appRunning;
                        Tier tier = Enum.Parse<Tier>(item.Tier);
                        if (demote)
                        {
                            tier = tier switch { Tier.T0 => Tier.T1, Tier.T1 => Tier.T2, _ => Tier.T3 };
                        }

                        recommendations.Add(new Recommendation(
                            path,
                            IsDirectoryPath(path),
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

    private bool IsDetected(Adapter adapter)
    {
        if (adapter.Detect.UninstallNamePatterns is { Count: > 0 } patterns &&
            _environment.InstalledApps.Any(app => patterns.Any(p =>
                app.Name.Contains(p, StringComparison.OrdinalIgnoreCase))))
        {
            return true;
        }

        if (adapter.Detect.PathPatterns is { Count: > 0 } paths &&
            paths.Select(_environment.Expand).Any(p => p is not null && Directory.Exists(p)))
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

    private IEnumerable<string> ExpandItemTarget(string target, IReadOnlyDictionary<string, string> extraSymbols)
    {
        string normalized = ApplyExtraSymbols(target.Replace('/', '\\'), extraSymbols);
        bool perChild = normalized.EndsWith("\\*", StringComparison.Ordinal);
        int wildcard = normalized.IndexOf("\\*\\", StringComparison.Ordinal);

        if (wildcard >= 0)
        {
            string? prefix = _environment.Expand(normalized[..wildcard]);
            if (prefix is null)
            {
                yield break;
            }

            foreach (string path in ExpandGlob(prefix + normalized[wildcard..]))
            {
                yield return path;
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
            foreach (string path in ExpandGlob(expanded + "\\*"))
            {
                yield return path;
            }
        }
        else if (Directory.Exists(expanded) || File.Exists(expanded))
        {
            yield return expanded;
        }
    }

    private static string ApplyExtraSymbols(string target, IReadOnlyDictionary<string, string> extraSymbols)
    {
        foreach ((string symbol, string value) in extraSymbols)
        {
            target = target.Replace("${" + symbol + "}", value, StringComparison.Ordinal);
        }

        return target;
    }

    private IEnumerable<string> ExpandGlob(string pattern)
    {
        string normalized = pattern.Replace('/', '\\');
        int middle = normalized.IndexOf("\\*\\", StringComparison.Ordinal);
        if (middle >= 0)
        {
            string prefix = normalized[..middle];
            string suffix = normalized[(middle + 3)..];
            if (!Directory.Exists(prefix))
            {
                yield break;
            }

            foreach (string child in Directory.EnumerateDirectories(prefix))
            {
                string candidate = child + "\\" + suffix;
                if (Directory.Exists(candidate) || File.Exists(candidate))
                {
                    yield return candidate;
                }
            }

            yield break;
        }

        if (normalized.EndsWith("\\*", StringComparison.Ordinal))
        {
            string prefix = normalized[..^2];
            if (!Directory.Exists(prefix))
            {
                yield break;
            }

            foreach (string child in Directory.EnumerateDirectories(prefix))
            {
                yield return child;
            }

            yield break;
        }

        if (Directory.Exists(normalized) || File.Exists(normalized))
        {
            yield return normalized;
        }
    }

    private static (long Size, DateTime LastWriteUtc) MeasureTree(string path)
    {
        if (!Directory.Exists(path) && !File.Exists(path))
        {
            return (0, DateTime.MinValue);
        }

        if (File.Exists(path) && !Directory.Exists(path))
        {
            var info = new FileInfo(path);
            return (info.Length, info.LastWriteTimeUtc);
        }

        long size = 0;
        DateTime maxWrite = DateTime.MinValue;
        foreach (string file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
        {
            var info = new FileInfo(file);
            size += info.Length;
            if (info.LastWriteTimeUtc > maxWrite)
            {
                maxWrite = info.LastWriteTimeUtc;
            }
        }

        return (size, maxWrite);
    }

    private static bool IsDirectoryPath(string path) =>
        Directory.Exists(path) && !File.Exists(path);
}
