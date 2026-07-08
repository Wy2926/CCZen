using CCZen.Engine.Index;

namespace CCZen.Engine.Rules;

/// <summary>
/// Local, explainable inference pipeline (spec: 02): environment discovery →
/// candidate generation → evidence → scoring → tiering. No LLM, no network;
/// every recommendation carries the rule id and explanation that produced it.
/// Candidate generation uses <see cref="IIndexQuery"/> (RULE-FR-026).
/// </summary>
public sealed class RuleEngine
{
    private const int CacheDirectoryMaxDepth = 8;

    private readonly EnvironmentModel _environment;
    private readonly RulePack _pack;
    private readonly IIndexQuery _index;

    public RuleEngine(EnvironmentModel environment, RulePack pack, IIndexQuery index)
    {
        _environment = environment;
        _pack = pack;
        _index = index;
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
                if (root is null || !_index.TryResolvePrefix(root, out int rootNode))
                {
                    continue;
                }

                if (rule.Match?.DirNameLexicon is string lexiconName)
                {
                    EvaluateCacheDirectories(rule, rootNode, lexiconName, recommendations, claimed);
                }
                else if (rule.Match?.FileExtensions is { Count: > 0 } extensions)
                {
                    EvaluateFilesByExtension(rule, rootNode, recursive, extensions, recommendations, claimed);
                }
                else
                {
                    EvaluateWholeDirectory(rule, root, rootNode, recommendations, claimed);
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
        Rule rule, int rootNode, string lexiconName, List<Recommendation> recommendations, HashSet<string> claimed)
    {
        if (!_pack.Lexicons.TryGetValue(lexiconName, out string[]? lexicon))
        {
            return;
        }

        var words = new HashSet<string>(lexicon, StringComparer.OrdinalIgnoreCase);
        HashSet<string>? excludeExtensions = rule.Match?.ExcludeContentLexicon is string exclude
            ? _pack.Lexicons.GetValueOrDefault(exclude)?.ToHashSet(StringComparer.OrdinalIgnoreCase)
            : null;

        foreach (string directory in _index.FindDirectoriesByName(rootNode, words, CacheDirectoryMaxDepth))
        {
            if (!claimed.Add(directory))
            {
                continue;
            }

            if (!_index.TryResolvePrefix(directory, out int dirNode))
            {
                continue;
            }

            SubtreeStats stats = _index.GetSubtreeStats(dirNode);
            bool hasUserAssets = excludeExtensions is not null &&
                                 _index.SubtreeContainsExtension(dirNode, excludeExtensions);
            AddRecommendation(rule, directory, isDirectory: true, stats.LogicalSize, stats.MaxLastWriteUtc, hasUserAssets, recommendations);
        }
    }

    /// <summary>RULE-FR-021/022: files matching log/dump/installer extensions.</summary>
    private void EvaluateFilesByExtension(
        Rule rule, int rootNode, bool recursive, List<string> extensions, List<Recommendation> recommendations, HashSet<string> claimed)
    {
        var extensionSet = extensions.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (FileEntry file in _index.FindFilesByExtension(rootNode, extensionSet, recursive))
        {
            if (!claimed.Add(file.Path))
            {
                continue;
            }

            if (!_index.TryResolvePrefix(file.Path, out int fileNode))
            {
                continue;
            }

            SubtreeStats stats = _index.GetSubtreeStats(fileNode);
            AddRecommendation(rule, file.Path, isDirectory: false, stats.LogicalSize, stats.MaxLastWriteUtc, hasUserAssets: false, recommendations);
        }
    }

    /// <summary>RULE-FR-010: whole-directory categories such as ${TEMP}.</summary>
    private void EvaluateWholeDirectory(
        Rule rule, string root, int rootNode, List<Recommendation> recommendations, HashSet<string> claimed)
    {
        if (!claimed.Add(root))
        {
            return;
        }

        SubtreeStats stats = _index.GetSubtreeStats(rootNode);
        AddRecommendation(rule, root, isDirectory: true, stats.LogicalSize, stats.MaxLastWriteUtc, hasUserAssets: false, recommendations);
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
