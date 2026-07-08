using System.IO.Enumeration;
using CCZen.Engine.Index;

namespace CCZen.Engine.Scanning;

/// <summary>
/// Portable path (spec: SCAN-FR-040): parallel directory traversal built on
/// System.IO.Enumeration for non-NTFS volumes or when the process lacks
/// administrator rights. Allocated size is approximated by rounding the
/// logical size up to the cluster size.
/// </summary>
public sealed class FallbackScanner : IVolumeScanner
{
    private readonly int _clusterSize;

    public FallbackScanner(int clusterSize = 4096)
    {
        _clusterSize = clusterSize;
    }

    public FileSystemIndex Scan(string root, CancellationToken cancellationToken = default)
    {
        string rootLabel = root.EndsWith('\\') ? root : root + "\\";
        if (!Directory.Exists(rootLabel.TrimEnd('\\')))
        {
            return new IndexBuilder(rootFrn: 1).Build(rootLabel);
        }

        var builder = new IndexBuilder(rootFrn: 1);
        ulong nextId = 2;
        var pending = new Stack<(string Path, ulong Frn)>();
        pending.Push((rootLabel, 1));

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            (string directory, ulong parentId) = pending.Pop();

            IEnumerable<(string Name, bool IsDir, long Length)> entries;
            try
            {
                entries = new FileSystemEnumerable<(string, bool, long)>(
                    directory,
                    (ref FileSystemEntry entry) =>
                    {
                        // Never traverse reparse points (junctions/symlinks) to avoid double counting.
                        bool isDir = entry.IsDirectory && (entry.Attributes & FileAttributes.ReparsePoint) == 0;
                        return (entry.FileName.ToString(), isDir, entry.IsDirectory ? 0 : entry.Length);
                    },
                    new EnumerationOptions
                    {
                        IgnoreInaccessible = true,
                        AttributesToSkip = 0,
                        RecurseSubdirectories = false,
                    });

                foreach ((string name, bool isDir, long length) in entries)
                {
                    ulong id = nextId++;
                    builder.AddEntry(id, parentId, name, isDir);
                    int nodeIndex = builder.Count - 1;
                    if (isDir)
                    {
                        pending.Push((System.IO.Path.Combine(directory, name), id));
                    }
                    else
                    {
                        long allocated = (length + _clusterSize - 1) / _clusterSize * _clusterSize;
                        builder.SetSizes(nodeIndex, length, allocated);
                        try
                        {
                            builder.SetLastWriteUtc(nodeIndex, File.GetLastWriteTimeUtc(Path.Combine(directory, name)));
                        }
                        catch (IOException)
                        {
                        }
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
            }
            catch (IOException)
            {
            }
        }

        return builder.Build(rootLabel);
    }
}
