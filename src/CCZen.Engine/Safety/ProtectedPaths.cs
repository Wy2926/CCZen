using System.Runtime.Versioning;

namespace CCZen.Engine.Safety;

/// <summary>
/// Hard-coded protected-path veto list (spec: SAFE-FR-001..004). Rule packs
/// cannot override or shrink this list. Paths are normalized to their final
/// real target before checking, so <c>..</c>, symbolic links, junctions and
/// 8.3 short names cannot bypass protection (SAFE-FR-003).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ProtectedPaths
{
    private readonly List<string> _protectedRoots;
    private readonly List<string> _allowedWindirSubtrees;
    private readonly string? _windir;

    public ProtectedPaths()
        : this(BuildDefaultRoots(), Environment.GetFolderPath(Environment.SpecialFolder.Windows))
    {
    }

    /// <summary>Test seam: explicit roots instead of live Known Folders.</summary>
    public ProtectedPaths(List<string> protectedRoots, string? windir)
    {
        _protectedRoots = protectedRoots.Select(NormalizeStored).ToList();
        _windir = string.IsNullOrEmpty(windir) ? null : NormalizeStored(windir);
        _allowedWindirSubtrees = _windir is null
            ? []
            : [
                // Whitelisted cache subtrees inside %WINDIR% (SAFE-FR-002).
                NormalizeStored(Path.Combine(_windir, "Temp")),
                NormalizeStored(Path.Combine(_windir, "SoftwareDistribution", "Download")),
              ];
    }

    private static List<string> BuildDefaultRoots()
    {
        var roots = new List<string>();
        void Add(string? path)
        {
            if (!string.IsNullOrEmpty(path))
            {
                roots.Add(path);
            }
        }

        Add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
        Add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
        Add(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        Add(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));
        Add(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos));
        Add(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic));
        Add(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));

        foreach (DriveInfo drive in DriveInfo.GetDrives())
        {
            if (drive.IsReady)
            {
                Add(Path.Combine(drive.Name, "System Volume Information"));
            }
        }

        return roots;
    }

    /// <summary>
    /// Returns true when <paramref name="path"/> must never be deleted.
    /// Volume roots, protected roots and %WINDIR% (minus whitelisted cache
    /// subtrees) are all vetoed; unresolvable paths are vetoed defensively.
    /// </summary>
    public bool IsProtected(string path)
    {
        string? real = PathNormalizer.ResolveRealPath(path);
        if (real is null)
        {
            return true;
        }

        // Volume roots are always protected (SAFE-FR-002).
        if (Path.GetPathRoot(real) is string root && PathsEqual(root, real))
        {
            return true;
        }

        if (_windir is not null && IsUnder(real, _windir) &&
            !_allowedWindirSubtrees.Any(allowed => IsUnder(real, allowed) && !PathsEqual(real, allowed)))
        {
            return true;
        }

        return _protectedRoots.Any(p => PathsEqual(real, p) || IsUnder(real, p));
    }

    private static string NormalizeStored(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static bool PathsEqual(string a, string b) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(a),
            Path.TrimEndingDirectorySeparator(b),
            StringComparison.OrdinalIgnoreCase);

    private static bool IsUnder(string path, string root) =>
        path.Length > root.Length &&
        path.StartsWith(root, StringComparison.OrdinalIgnoreCase) &&
        (path[root.Length] == '\\' || path[root.Length] == '/');
}
