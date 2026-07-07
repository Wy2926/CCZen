using CCZen.Engine.Safety;

namespace CCZen.Engine.Tests;

public class LockProbeTests : IDisposable
{
    private readonly string _root;

    public LockProbeTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cczen-lock-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void GetLockingProcesses_ReportsHolder_ForOpenFile()
    {
        string path = Path.Combine(_root, "held.bin");
        using FileStream stream = File.Open(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None);

        IReadOnlyList<string> holders = LockProbe.GetLockingProcesses([path]);

        Assert.NotEmpty(holders);
    }

    [Fact]
    public void GetLockingProcesses_Empty_ForUnlockedFile()
    {
        string path = Path.Combine(_root, "free.bin");
        File.WriteAllBytes(path, new byte[8]);

        Assert.Empty(LockProbe.GetLockingProcesses([path]));
    }

    [Fact]
    public void Execute_LockedFile_IsSkippedWithHolderName()
    {
        string path = Path.Combine(_root, "locked.bin");
        using FileStream stream = File.Open(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        stream.Write(new byte[16]);
        stream.Flush();

        var store = new QuarantineStore(new ProtectedPaths([], windir: null));
        IReadOnlyList<ItemResult> results = store.Execute(BatchPlan.Create([PlanItem.FromPath(path, "r1")!]));

        ItemResult result = Assert.Single(results);
        Assert.Equal(ItemOutcome.SkippedLocked, result.Outcome);
        Assert.True(File.Exists(path));
    }
}
