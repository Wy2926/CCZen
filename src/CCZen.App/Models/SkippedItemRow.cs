using CCZen.Engine.Safety;

namespace CCZen.App.Models;

/// <summary>
/// One item that a batch execution could not process, with a user-readable
/// Chinese reason (specs/04: every veto/skip must be reported, not silent).
/// </summary>
public sealed class SkippedItemRow
{
    public SkippedItemRow(ItemResult result)
    {
        Path = result.Path;
        Reason = result.Outcome switch
        {
            ItemOutcome.SkippedProtected => "受保护路径（安全红线，禁止清理）",
            ItemOutcome.SkippedLocked => $"文件被占用{FormatDetail(result.Detail)}",
            ItemOutcome.SkippedFingerprintMismatch => "文件在计划后发生变化，已跳过",
            ItemOutcome.SkippedMissing => "文件已不存在",
            ItemOutcome.SkippedReparsePoint => "重解析点/符号链接，已跳过",
            _ when IsAccessDenied(result.Detail) => "系统拒绝访问（权限不足），请以管理员身份运行后重试",
            _ => $"执行失败{FormatDetail(result.Detail)}",
        };
    }

    public string Path { get; }

    public string Reason { get; }

    private static bool IsAccessDenied(string? detail) =>
        detail is not null
        && (detail.Contains("denied", StringComparison.OrdinalIgnoreCase) || detail.Contains("拒绝"));

    private static string FormatDetail(string? detail) =>
        string.IsNullOrEmpty(detail) ? string.Empty : $"：{detail}";
}
