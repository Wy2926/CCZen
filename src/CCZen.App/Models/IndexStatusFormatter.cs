using CCZen.Engine.Service;

namespace CCZen.App.Models;

/// <summary>Formats shared index status strings for Cleaner and Search pages.</summary>
public static class IndexStatusFormatter
{
    public const string NotBuilt = "索引未构建 — 首次操作时自动扫描系统卷";

    public static string From(ScanSummary summary)
    {
        string mode = summary.Incremental ? "增量刷新" : "完整扫描";
        return $"已索引 {summary.Root} — {summary.FileCount:N0} 个文件（{mode}，{summary.ElapsedSeconds:0.00} s）";
    }
}
