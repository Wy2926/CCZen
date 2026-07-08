using CCZen.Engine.Rules;

namespace CCZen.Engine.Tests;

/// <summary>
/// Pre-merge directory-walk baseline for parity tests (spec: 02 Parity 测试).
/// Lives in the test assembly only; production RuleEngine uses IIndexQuery.
/// </summary>
internal sealed class RuleWalkBaseline
{
    private const int CacheDirectoryMaxDepth = 8;

    private readonly EnvironmentModel _environment;
    private readonly RulePack _pack;

    public RuleWalkBaseline(EnvironmentModel environment, RulePack pack)
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

    private void EvaluateCacheDirectories(
        Rule rule, string root, string lexiconName, List<Recommendation> recommendations, HashSet<string> claimed)
    {
        if (!_pack.Lexicons.TryGetValue(lexiconName, out string[]? lexicon))
        {
            return;
        }

        var words = new HashSet<string>(lexicon, StringComparer.OrdinalIgnoreCase);
        HashSet<string>? excludeExtensions = rule.Match?.ExcludeContentLexicon is string exclude
            ? _pack.Lexicons.GetValueOrDefault(exclude)?.ToHashSet(StringComparer.OrdinalIgnoreCase)
            : null;

        foreach (string directory in FindDirectoriesByName(root, words, CacheDirectoryMaxDepth))
        {
            if (!claimed.Add(directory))
            {
                continue;
            }

            (long size, DateTime lastWrite) = MeasureTree(directory);
            bool hasUserAssets = excludeExtensions is not null && SubtreeContainsExtension(directory, excludeExtensions);
            AddRecommendation(rule, directory, isDirectory: true, size, lastWrite, hasUserAssets, recommendations);
        }
    }

    private void EvaluateFilesByExtension(
        Rule rule, string root, bool recursive, List<string> extensions, List<Recommendation> recommendations, HashSet<string> claimed)
    {
        var extensionSet = extensions.ToHashSet(StringComparer.OrdinalIgnoreCase);
        SearchOption option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        foreach (string file in SafeEnumerateFiles(root, "*", option))
        {
            if (!extensionSet.Contains(Path.GetExtension(file)))
            {
                continue;
            }

            if (!claimed.Add(file))
            {
                continue;
            }

            var info = new FileInfo(file);
            AddRecommendation(rule, file, isDirectory: false, info.Length, info.LastWriteTimeUtc, hasUserAssets: false, recommendations);
        }
    }

    private void EvaluateWholeDirectory(
        Rule rule, string root, List<Recommendation> recommendations, HashSet<string> claimed)
    {
        if (!claimed.Add(root))
        {
            return;
        }

        (long size, DateTime lastWrite) = MeasureTree(root);
        AddRecommendation(rule, root, isDirectory: true, size, lastWrite, hasUserAssets: false, recommendations);
    }

    private static IEnumerable<string> FindDirectoriesByName(string root, IReadOnlySet<string> dirNames, int maxDepth)
    {
        var queue = new Queue<(string Path, int Depth)>();
        queue.Enqueue((root, 0));

        while (queue.Count > 0)
        {
            (string path, int depth) = queue.Dequeue();
            if (depth > maxDepth)
            {
                continue;
            }

            IEnumerable<string> children;
            try
            {
                children = Directory.EnumerateDirectories(path);
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }
            catch (IOException)
            {
                continue;
            }

            foreach (string child in children)
            {
                int childDepth = depth + 1;
                if (childDepth > 0 && dirNames.Contains(Path.GetFileName(child)))
                {
                    yield return child;
                }

                if (childDepth < maxDepth)
                {
                    queue.Enqueue((child, childDepth));
                }
            }
        }
    }

    private static (long Size, DateTime LastWriteUtc) MeasureTree(string path)
    {
        long size = 0;
        DateTime maxWrite = DateTime.MinValue;
        foreach (string file in SafeEnumerateFiles(path, "*", SearchOption.AllDirectories))
        {
            try
            {
                var info = new FileInfo(file);
                size += info.Length;
                if (info.LastWriteTimeUtc > maxWrite)
                {
                    maxWrite = info.LastWriteTimeUtc;
                }
            }
            catch (IOException)
            {
            }
        }

        return (size, maxWrite);
    }

    private static bool SubtreeContainsExtension(string path, IReadOnlySet<string> extensions)
    {
        foreach (string file in SafeEnumerateFiles(path, "*", SearchOption.AllDirectories))
        {
            if (extensions.Contains(Path.GetExtension(file)))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<string> SafeEnumerateFiles(string root, string pattern, SearchOption option)
    {
        try
        {
            return Directory.EnumerateFiles(root, pattern, option);
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
        catch (IOException)
        {
            return [];
        }
    }

    private static void AddRecommendation(
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
