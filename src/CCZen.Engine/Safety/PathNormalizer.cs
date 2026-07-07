using System.Runtime.Versioning;

namespace CCZen.Engine.Safety;

/// <summary>
/// Resolves a path to its final on-disk target (spec: SAFE-FR-003): full-path
/// normalization plus symlink/junction resolution via the OS final-path query
/// (.NET wraps GetFinalPathNameByHandle in <see cref="FileSystemInfo.ResolveLinkTarget"/> /
/// <see cref="File.ResolveLinkTarget"/>).
/// </summary>
[SupportedOSPlatform("windows")]
public static class PathNormalizer
{
    /// <summary>
    /// Returns the fully-resolved real path, following every reparse point in
    /// the chain, or null when the path does not exist or cannot be resolved.
    /// </summary>
    public static string? ResolveRealPath(string path)
    {
        string full;
        try
        {
            full = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
        {
            return null;
        }

        if (!File.Exists(full) && !Directory.Exists(full))
        {
            return null;
        }

        try
        {
            FileSystemInfo info = Directory.Exists(full) ? new DirectoryInfo(full) : new FileInfo(full);
            FileSystemInfo? target = info.LinkTarget is null ? null : info.ResolveLinkTarget(returnFinalTarget: true);
            string resolved = target?.FullName ?? full;

            // Also resolve links in parent components (e.g. C:\link\sub\file).
            string? parent = Path.GetDirectoryName(resolved);
            if (parent is not null && Directory.Exists(parent))
            {
                var parentInfo = new DirectoryInfo(parent);
                if (parentInfo.LinkTarget is not null &&
                    parentInfo.ResolveLinkTarget(returnFinalTarget: true) is { } parentTarget)
                {
                    resolved = Path.Combine(parentTarget.FullName, Path.GetFileName(resolved));
                }
            }

            return Path.TrimEndingDirectorySeparator(resolved);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>True when the entry itself is a reparse point (SAFE-FR-004).</summary>
    public static bool IsReparsePoint(string path)
    {
        try
        {
            FileAttributes attributes = File.GetAttributes(path);
            return attributes.HasFlag(FileAttributes.ReparsePoint);
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            return false;
        }
    }
}
