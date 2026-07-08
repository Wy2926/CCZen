using Microsoft.UI;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace CCZen.App.Models;

/// <summary>Badge colors/labels for risk tiers (specs/02: T0 safest → T3 report-only).</summary>
public static class TierVisuals
{
    public static string Label(string tier) => tier switch
    {
        "T0" => "T0 · 安全",
        "T1" => "T1 · 低风险",
        "T2" => "T2 · 需确认",
        _ => "T3 · 仅报告",
    };

    public static SolidColorBrush Badge(string tier) => new(tier switch
    {
        "T0" => Color.FromArgb(0x33, 0x0F, 0x7B, 0x0F),
        "T1" => Color.FromArgb(0x33, 0x00, 0x5F, 0xB8),
        "T2" => Color.FromArgb(0x33, 0x9D, 0x5D, 0x00),
        _ => Color.FromArgb(0x33, 0x8A, 0x8A, 0x8A),
    });

    public static SolidColorBrush BadgeText(string tier) => new(tier switch
    {
        "T0" => Colors.SeaGreen,
        "T1" => Color.FromArgb(0xFF, 0x00, 0x78, 0xD4),
        "T2" => Color.FromArgb(0xFF, 0xC0, 0x72, 0x00),
        _ => Colors.Gray,
    });
}
