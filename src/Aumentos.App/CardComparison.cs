using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Aumentos;

internal sealed record CardComparison(string Local, OpGgCard? External = null, bool Loading = false)
{
    internal static readonly CardComparison Unread = new("?");
    internal const string Missing = "Sin dato";
    internal string ExternalText => External?.PickPercent is { } pick
        ? $"Elección {pick.ToString("0.##", CultureInfo.GetCultureInfo("es-MX"))} %"
        : External?.Tier ?? (Loading ? "Consultando…" : Missing);

    internal static CardComparison?[] ForOffer(Ratings ratings, GameContext game, int?[] ids,
        bool[] items, OpGgSnapshot? snapshot, bool loading = false)
    {
        var external = snapshot?.Mode == game.Mode && snapshot.Patch == ratings.Catalog.Patch &&
            string.Equals(snapshot.Champion, game.Champion, StringComparison.OrdinalIgnoreCase) ? snapshot.Cards : null;
        return ids.Select((id, i) =>
        {
            if (items[i]) return null;
            if (!id.HasValue) return Unread;
            var local = ratings.IsVariable(game.Mode, id.Value) ? "Variable" :
                ratings.Rate(game.Mode, game.Champion, id.Value)?.ToString() ?? Missing;
            return new CardComparison(local, external?.GetValueOrDefault(id.Value), loading);
        }).ToArray();
    }
}
