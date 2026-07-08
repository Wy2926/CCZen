using CCZen.Engine.Index;
using CCZen.Engine.Scanning;

namespace CCZen.Engine.Tests;

/// <summary>Builds an in-memory index from a fixture directory tree for rule tests.</summary>
internal static class TestIndexFactory
{
    public static IndexQuery FromDirectory(string root) =>
        new(new FallbackScanner().Scan(root));
}
