using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Aumentos;

internal sealed record MayhemPopularity(decimal Percent, string Patch, bool IsPrevious)
{
    internal string Text => $"{AramMayhemRatings.Attribution}   {Percent.ToString("0.##", CultureInfo.GetCultureInfo("es-MX"))} % global";
}

internal sealed record ArenaPopularity(decimal Percent, int GamesPicked, int SampleGames, string Patch, bool IsPrevious)
{
    private static readonly CultureInfo Spanish = CultureInfo.GetCultureInfo("es-MX");
    internal string Text => $"{GgezArenaPage.Attribution}   {Percent.ToString("0.##", Spanish)} % elección";
    internal string Details => $"{GamesPicked.ToString("N0", Spanish)}/{SampleGames.ToString("N0", Spanish)} partidas · " +
        (IsPrevious ? $"parche ant. {Patch}" : Patch);
}

internal sealed record CardComparison(string Local, OpGgCard? External = null, bool Loading = false,
    MayhemPopularity? Mayhem = null, ArenaPopularity? Arena = null)
{
    internal static readonly CardComparison Unread = new("?");
    internal const string Missing = "Sin dato";
    internal string ExternalText => External?.PickPercent is { } pick
        ? $"Elección {pick.ToString("0.##", CultureInfo.GetCultureInfo("es-MX"))} %"
        : External?.Tier ?? (Loading ? "Consultando…" : Missing);

    internal static CardComparison?[] ForOffer(Ratings ratings, GameContext game, int?[] ids,
        bool[] items, OpGgSnapshot? snapshot, bool loading = false, AramMayhemSnapshot? mayhem = null,
        GgezArenaSnapshot? ggez = null)
    {
        var external = snapshot?.Mode == game.Mode && snapshot.Patch == ratings.Catalog.Patch &&
            string.Equals(snapshot.Champion, game.Champion, StringComparison.OrdinalIgnoreCase) ? snapshot.Cards : null;
        var global = game.Mode == Mode.Caos && mayhem?.ClientPatch == ratings.Catalog.Patch ? mayhem : null;
        var previous = global?.IsPrevious == true;
        var championStats = game.Mode == Mode.Arena && ggez?.ClientPatch == ratings.Catalog.Patch &&
            string.Equals(ggez.Champion, game.Champion, StringComparison.OrdinalIgnoreCase) ? ggez : null;
        var arenaPatch = championStats is null ? "" : LeaguePatch.Public(championStats.SourcePatch);
        return ids.Select((id, i) =>
        {
            if (items[i]) return null;
            if (!id.HasValue) return Unread;
            var local = ratings.IsVariable(game.Mode, id.Value) ? "Variable" :
                ratings.Rate(game.Mode, game.Champion, id.Value)?.ToString() ?? Missing;
            var popularity = global is not null && global.Picks.TryGetValue(id.Value, out var percent)
                ? new MayhemPopularity(percent, global.Patch, previous) : null;
            var arena = championStats is not null && championStats.Picks.TryGetValue(id.Value, out var pick)
                ? new ArenaPopularity(pick.Percent, pick.GamesPicked, championStats.SampleGames, arenaPatch, championStats.IsPrevious)
                : null;
            return new CardComparison(local, external?.GetValueOrDefault(id.Value), loading, popularity, arena);
        }).ToArray();
    }
}
