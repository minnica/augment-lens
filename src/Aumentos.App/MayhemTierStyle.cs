using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace Aumentos;

internal static class MayhemTierStyle
{
    internal static Border SelectionBadge(string? tier, double width, double height) =>
        TierHexagon.Create(tier, width, height, Math.Min(width * .55, height * .49), centerGlyph: true);

    internal static Border ChampionBadge(string? tier, string champion, double width, double height,
        string source = "OP.GG")
    {
        var badge = SelectionBadge(tier, width, height);
        AutomationProperties.SetName(badge, $"{champion} · {(tier is null ? "Sin tier publicado" : $"Tier {tier} de {source}")}");
        return badge;
    }
}
