using System.Windows.Automation;
using System.Windows.Controls;

namespace Aumentos;

internal static class MayhemTierStyle
{
    internal static Border ChampionBadge(string? tier, string champion, double width, double height,
        string source = "OP.GG")
    {
        var badge = TierHexagon.Create(tier, width, height);
        AutomationProperties.SetName(badge, $"{champion} · {(tier is null ? "Sin tier publicado" : $"Tier {tier} de {source}")}");
        return badge;
    }
}
