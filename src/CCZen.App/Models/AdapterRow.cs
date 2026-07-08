using CCZen.Engine.Rules;

namespace CCZen.App.Models;

/// <summary>Display row for one declarative application adapter (specs/03).</summary>
public sealed record AdapterRow(
    string Name,
    string CategoryLabel,
    string Detail,
    IReadOnlyList<AdapterItemRow> Items)
{
    public static AdapterRow From(Adapter adapter) =>
        new(
            adapter.Name,
            CategoryToLabel(adapter.Category),
            $"{adapter.Items.Count} 个清理项",
            adapter.Items.Select(AdapterItemRow.From).ToList());

    private static string CategoryToLabel(string category) => category switch
    {
        "browser" => "浏览器",
        "im" => "即时通讯",
        "devtool" => "开发工具",
        "game" => "游戏平台",
        "creative" => "创意工具",
        "system" => "系统",
        _ => "其他",
    };
}

/// <summary>Display row for one cleanable item inside an adapter.</summary>
public sealed record AdapterItemRow(string Tier, string Explain, string Detail)
{
    public static AdapterItemRow From(AdapterItem item) =>
        new(item.Tier, item.Explain, $"{item.Targets.Count} 个目标路径");
}
