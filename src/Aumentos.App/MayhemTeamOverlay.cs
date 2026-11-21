using System;
using System.Linq;
using System.Windows;

namespace Aumentos;

internal sealed record MayhemTeamDisplay(Int32Rect Bounds, nint Target, string Caption, bool HasRatings, bool PreviousPatch,
    ChampionRankingRow[] Rows, ChampionRankingRow? Selection, MayhemCompositionPick[] Composition, MayhemCompositionGrade? Grade,
    string EmptyMessage)
{
    internal static MayhemTeamDisplay Create(Int32Rect bounds, nint target, Catalog catalog,
        MayhemChampionTierView source, string? championKey, string[] available, string emptyMessage,
        MayhemCompositionPick[] composition, MayhemCompositionGrade? grade)
    {
        const string tiers = "SABCD";
        var data = source.Data;
        var eligible = available.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var rows = catalog.Champions.Where(champion => eligible.Contains(champion.Key)).Select(Row)
            .OrderBy(row => row.Tier is { Length: 1 } tier ? tiers.IndexOf(tier[0]) : tiers.Length)
            .ThenBy(row => row.Position ?? int.MaxValue)
            .ThenBy(row => row.Champion, StringComparer.OrdinalIgnoreCase).ToArray();
        ChampionRankingRow? selection = null;
        if (championKey is not null)
        {
            var champion = catalog.Champions.FirstOrDefault(champion =>
                string.Equals(champion.Key, championKey, StringComparison.OrdinalIgnoreCase));
            selection = champion is null ? new ChampionRankingRow(championKey, data?.Letter(championKey)) : Row(champion);
        }
        return new(bounds, target, data is null ? source.Status : MayhemChampionTiers.Caption(data, catalog.Patch),
            data is not null, data is not null && data.SourcePatch != catalog.Patch, rows, selection, composition, grade, emptyMessage);

        ChampionRankingRow Row(Champion champion) => new(champion.Name, data?.Letter(champion.Key), data?.Rank(champion.Key));
    }
}

internal sealed class MayhemTeamOverlay() : ChampionRankingOverlay(Mode.Caos)
{
    internal void Render(MayhemTeamDisplay? view) => RenderRanking(view is null ? null : new(
        view.Bounds, view.Target, view.Caption, view.HasRatings, view.PreviousPatch, view.Rows, view.Selection,
        view.Composition.Select(pick => new ChampionRankingPick(pick.Champion, pick.Tier, pick.Reason)).ToArray(),
        view.Grade is { } grade ? new(grade.Tier, grade.Reason, grade.Members, grade.TeamSize) : null, view.EmptyMessage));
}
