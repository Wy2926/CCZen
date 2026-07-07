namespace CCZen.Engine.Rules;

/// <summary>
/// Local, explainable inference pipeline (spec: 02): environment discovery →
/// candidate generation → evidence → scoring → tiering. No LLM, no network;
/// every recommendation carries the rule id and explanation that produced it.
/// </summary>
public sealed class RuleEngine
{
    private readonly EnvironmentModel _environment;
    private readonly RulePack _pack;

    public RuleEngine(EnvironmentModel environment, RulePack pack)
    {
        _environment = environment;
        _pack = pack;
    }

    public IReadOnlyList<Recommendation> Evaluate()
    {
        var recommendations = new List<Recommendation>();
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Rule rule in _pack.Rules)
        {
            foreach (string target in rule.Targets)
            {
                (string? root, bool recursive) = ExpandTarget(target);
                if (root is null || !Directory.Exists(root))
                {
                    continue;
                }

                if (rule.Match?.DirNameLexicon is string lexiconName)
                {
                    EvaluateCacheDirectories(rule, root, lexiconName, recommendations, claimed);
                }
                else if (rule.Match?.FileExtensions is { Count: > 0 } extensions)
                {
                    EvaluateFilesByExtension(rule, root, recursive, extensions, recommendations, claimed);
                }
                else
                {
                    EvaluateWholeDirectory(rule, root, recommendations, claimed);
                }
            }
        }

        return recommendations;
    }

    private (string? Root, bool Recursive) ExpandTarget(string target)
    {
        bool recursive = target.EndsWith("/**", StringComparison.Ordinal);
        string trimmed = recursive ? target[..^3] : target;
        string? expanded = _environment.Expand(trimmed.Replace('/', '\\'));
        return (expanded, recursive);
    }

    /// <summary>RULE-FR-020: directories whose name hits the cache lexicon, outside user document areas.</summary>
    private void EvaluateCacheDirectories(
        Rule rule, string root, string lexiconName, List<Recommendation> recommendations, HashSet<string> claimed)
    {
        if (!_pack.Lexicons.TryGetValue(lexiconName, out string[]? lexicon))
        {
            return;
        }

        var words = new HashSet<string>(lexicon, StringComparer.OrdinalIgnoreCase);
        string[]? excludeExtensions = rule.Match?.ExcludeContentLexicon is string exclude
            ? _pack.Lexicons.GetValueOrDefault(exclude)
            : null;

        foreach (string directory in SafeEnumerateDirectories(root))
        {
            if (!words.Contains(Path.GetFileName(directory)))
            {
                continue;
            }

            if (!claimed.Add(directory))
            {
                continue;
            }

            (long size, DateTime lastWrite, bool hasUserAssets) = InspectTree(directory, excludeExtensions);
            AddRecommendation(rule, directory, isDirectory: true, size, lastWrite, hasUserAssets, recommendations);
        }
    }

    /// <summary>RULE-FR-021/022: files matching log/dump/installer extensions.</summary>
    private void EvaluateFilesByExtension(
        Rule rule, string root, bool recursive, List<string> extensions, List<Recommendation> recommendations, HashSet<string> claimed)
    {
        var extensionSet = new HashSet<string>(extensions, StringComparer.OrdinalIgnoreCase);
        var options = new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = recursive };
        foreach (string file in Directory.EnumerateFiles(root, "*", options))
        {
            if (!extensionSet.Contains(Path.GetExtension(file)) || !claimed.Add(file))
            {
                continue;
            }

            var info = new FileInfo(file);
            AddRecommendation(rule, file, isDirectory: false, info.Length, info.LastWriteTimeUtc, hasUserAssets: false, recommendations);
        }
    }

    /// <summary>RULE-FR-010: whole-directory categories such as ${TEMP}.</summary>
    private void EvaluateWholeDirectory(Rule rule, string root, List<Recommendation> recommendations, HashSet<string> claimed)
    {
        if (!claimed.Add(root))
        {
            return;
        }

        (long size, DateTime lastWrite, _) = InspectTree(root, excludeExtensions: null);
        AddRecommendation(rule, root, isDirectory: true, size, lastWrite, hasUserAssets: false, recommendations);
    }

    private void AddRecommendation(
        Rule rule, string path, bool isDirectory, long size, DateTime lastWriteUtc, bool hasUserAssets,
        List<Recommendation> recommendations)
    {
        if (size == 0)
        {
            return;
        }

        var signals = new Dictionary<string, double>(rule.Signals ?? new Dictionary<string, double>())
        {
            ["staleness"] = Staleness(lastWriteUtc),
        };

        // content_type: user assets inside a cache-shaped tree demote one tier (一票降级).
        Tier tier = Enum.Parse<Tier>(rule.TierCap);
        if (hasUserAssets)
        {
            signals["content_type"] = 0;
            tier = Demote(tier);
        }

        double confidence = signals.Count == 0 ? 0 : signals.Values.Average();
        recommendations.Add(new Recommendation(
            path, isDirectory, size, rule.Id, tier, confidence, rule.Action, rule.Explain, signals));
    }

    private static (long Size, DateTime LastWriteUtc, bool HasUserAssets) InspectTree(string root, string[]? excludeExtensions)
    {
        long size = 0;
        DateTime lastWrite = DateTime.MinValue;
        bool hasUserAssets = false;
        var options = new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = true };
        foreach (string file in Directory.EnumerateFiles(root, "*", options))
        {
            var info = new FileInfo(file);
            size += info.Length;
            if (info.LastWriteTimeUtc > lastWrite)
            {
                lastWrite = info.LastWriteTimeUtc;
            }

            if (excludeExtensions is not null &&
                excludeExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
            {
                hasUserAssets = true;
            }
        }

        return (size, lastWrite, hasUserAssets);
    }

    private static IEnumerable<string> SafeEnumerateDirectories(string root) =>
        Directory.EnumerateDirectories(root, "*", new EnumerationOptions
        {
            IgnoreInaccessible = true,
            RecurseSubdirectories = true,
            MaxRecursionDepth = 8,
        });

    /// <summary>Staleness decay: untouched for 90+ days → 1.0, modified today → 0.</summary>
    private static double Staleness(DateTime lastWriteUtc)
    {
        if (lastWriteUtc == DateTime.MinValue)
        {
            return 0;
        }

        double days = (DateTime.UtcNow - lastWriteUtc).TotalDays;
        return Math.Clamp(days / 90.0, 0, 1);
    }

    private static Tier Demote(Tier tier) => tier switch
    {
        Tier.T0 => Tier.T1,
        Tier.T1 => Tier.T2,
        _ => Tier.T3,
    };
}

/// <summary>Risk tiers (spec: 02 管线 4). Lower tiers are safer.</summary>
public enum Tier
{
    T0,
    T1,
    T2,
    T3,
}

/// <summary>One cleanup recommendation with full audit trail (spec: 02 输出契约).</summary>
public sealed record Recommendation(
    string Path,
    bool IsDirectory,
    long SizeBytes,
    string RuleId,
    Tier Tier,
    double Confidence,
    string Action,
    string Explain,
    IReadOnlyDictionary<string, double> Signals);
