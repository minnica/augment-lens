using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Aumentos;

internal sealed record MayhemPopularity(decimal Percent, string Patch, bool IsPrevious)
{
    internal string Text => $"{AramMayhemRatings.Attribution}   {CardComparison.FormatPercent(Percent)} global";
}

internal sealed record ArenaPopularity(decimal Percent, int GamesPicked, int SampleGames, string Patch, bool IsPrevious)
{
    private static readonly CultureInfo Spanish = CultureInfo.GetCultureInfo("es-MX");
    internal string Text => $"{GgezArenaPage.Attribution}   {CardComparison.FormatPercent(Percent)} elección";
    internal string SampleText => $"{GamesPicked.ToString("N0", Spanish)} de {SampleGames.ToString("N0", Spanish)} partidas";
    internal string Details => $"{GamesPicked.ToString("N0", Spanish)}/{SampleGames.ToString("N0", Spanish)} partidas · " +
        (IsPrevious ? $"parche ant. {Patch}" : Patch);
}

internal sealed record CardComparison(string Local, OpGgCard? External = null, bool Loading = false,
    MayhemPopularity? Mayhem = null, ArenaPopularity? Arena = null, string? ExternalPreviousPatch = null,
    ArenaChoiceRating? ArenaChoice = null, bool IsArenaAugment = false, string? LocalReason = null,
    bool RecommendedUpgrade = false, bool RecommendedAugment = false, bool UsesWiki = false,
    int? UpgradeLevel = null, string? UpgradeEffect = null)
{
    internal static readonly CardComparison Unread = new("?");
    internal const string Missing = "Sin dato";
    internal string LocalText => ArenaChoice?.TierText ?? (Local == "S" && IsArenaAugment ? "S+" : Local);
    internal string ArenaTitle => (Local == "Variable" ? "Valoración variable" : $"Tier local   {LocalText}") +
        (UpgradeLevel is { } level ? $" · Nv. {level}" : "") +
        (RecommendedUpgrade ? " · Mejora recomendada" : RecommendedAugment ? " · Recomendado" : "");
    internal string DetailText
    {
        get
        {
            if (string.IsNullOrEmpty(UpgradeEffect) || LocalReason?.Contains(UpgradeEffect, StringComparison.OrdinalIgnoreCase) == true)
                return LocalReason ?? "";
            if (string.IsNullOrEmpty(LocalReason) || UpgradeEffect.StartsWith(LocalReason, StringComparison.OrdinalIgnoreCase))
                return UpgradeEffect;
            return $"{LocalReason}\n{UpgradeEffect}";
        }
    }
    internal string ExternalText => External?.PickPercent is { } pick
        ? $"Elección {FormatPercent(pick)}" +
            (ExternalPreviousPatch is { } patch ? $" · parche ant. {patch}" : "")
        : External?.Tier ?? (Loading ? "Consultando…" : Missing);
    internal static string FormatPercent(decimal percent) => $"{percent.ToString("0.##", CultureInfo.GetCultureInfo("es-MX"))} %";

    internal static CardComparison?[] ForOffer(Ratings ratings, GameContext game, int?[] ids,
        bool[] items, OpGgSnapshot? snapshot, bool loading = false, AramMayhemSnapshot? mayhem = null,
        GgezArenaSnapshot? ggez = null, int?[]? upgradeLevels = null, IReadOnlyList<int>? arenaItems = null)
    {
        var external = snapshot?.Mode == game.Mode && snapshot.Patch == ratings.Catalog.Patch &&
            string.Equals(snapshot.Champion, game.Champion, StringComparison.OrdinalIgnoreCase) ? snapshot.Cards : null;
        var externalPrevious = external is not null && snapshot is { IsPrevious: true }
            ? LeaguePatch.Public(snapshot.DataPatch) : null;
        var global = game.Mode == Mode.Caos && mayhem?.ClientPatch == ratings.Catalog.Patch ? mayhem : null;
        var previous = global?.IsPrevious == true;
        var championStats = game.Mode == Mode.Arena && ggez?.ClientPatch == ratings.Catalog.Patch &&
            string.Equals(ggez.Champion, game.Champion, StringComparison.OrdinalIgnoreCase) ? ggez : null;
        var arenaPatch = championStats is null ? "" : LeaguePatch.Public(championStats.SourcePatch);
        int? LevelAt(int? id, int slot)
        {
            if (id is null || items[slot] || upgradeLevels is null || slot >= upgradeLevels.Length ||
                upgradeLevels[slot] is not (2 or 3) || ratings.ArenaAugment(id.Value) is not { } card)
                return null;
            var level = upgradeLevels[slot];
            return card.MaxLevel < level ? null : level;
        }
        var levels = game.Mode == Mode.Arena && upgradeLevels is not null ? ids.Select(LevelAt).ToArray() : null;
        var badges = ids.Select((id, i) =>
        {
            if (items[i]) return null;
            if (!id.HasValue) return Unread;
            var variable = ratings.VariableReason(game.Mode, id.Value);
            var local = variable is not null ? "Variable" :
                ratings.RateEditorial(game.Mode, game.Champion, id.Value)?.ToString() ?? Missing;
            var popularity = global is not null && global.Picks.TryGetValue(id.Value, out var percent)
                ? new MayhemPopularity(percent, global.Patch, previous) : null;
            var arena = championStats is not null && championStats.Picks.TryGetValue(id.Value, out var pick)
                ? new ArenaPopularity(pick.Percent, pick.GamesPicked, championStats.SampleGames, arenaPatch, championStats.IsPrevious)
                : null;
            var published = external?.GetValueOrDefault(id.Value);
            return new CardComparison(local, published, loading, popularity, arena,
                published is null ? null : externalPrevious, IsArenaAugment: game.Mode == Mode.Arena,
                LocalReason: variable ?? ratings.DefaultReason(game.Mode, id.Value), UpgradeLevel: levels?[i],
                UpgradeEffect: levels?[i] is { } level ? ratings.ArenaAugment(id.Value)?.EffectAt(level) : null);
        }).ToArray();
        if (game.Mode == Mode.Arena)
        {
            var isUpgrade = levels?.Any(level => level.HasValue) == true;
            var jax = game.Champion.Equals("Jax", StringComparison.OrdinalIgnoreCase);
            if (jax)
                DescribeJaxChoices(ids, levels, badges);
            if (isUpgrade)
                ArenaUpgradeRatings.Apply(ratings, game, ids, levels!, badges);
            else
                RecommendArenaAugment(jax, ids, badges);
            ratings.AugmentAffinity.Apply(game, ids, levels, arenaItems, badges);
        }
        return badges;
    }

    private const int Deft = 22;
    private const int Goliath = 41;
    private const int TapDancer = 81;
    private const int Typhoon = 87;

    private static void DescribeJaxChoices(int?[] ids, int?[]? levels, CardComparison?[] badges)
    {
        for (int i = 0; i < badges.Length; i++)
        {
            if (badges[i] is not { Local: "A" } badge)
                continue;
            badges[i] = ids[i] switch
            {
                Deft when levels?[i] == 2 => badge with { LocalReason = "Velocidad de ataque de 60 % a 80 %" },
                Deft when levels?[i] == 3 => badge with { LocalReason = "Velocidad de ataque de 80 % a 130 % y sin límite" },
                Deft when levels?[i] is null => badge with { LocalReason = "Velocidad de ataque desde el primer golpe" },
                Goliath => badge with { LocalReason = "Vida y fuerza adaptable para resistir el daño inicial" },
                ArenaUpgradeRatings.LightEmUp => badge with { LocalReason = "Daño mágico adicional cada cuarto ataque" },
                TapDancer => badge with { LocalReason = "Velocidad para sostener ataques y activar el tercer golpe de la R" },
                84 => badge with { LocalReason = "Penetración física y mágica contra rivales con resistencias" },
                Typhoon => badge with { LocalReason = "Aplica efectos al impacto a un segundo rival cercano" },
                217 => badge with { LocalReason = "Daño mágico acumulable al atacar campeones" },
                ArenaUpgradeRatings.Cerberus => badge with { LocalReason = "Acelera los primeros ataques y recompensa tres impactos" },
                _ => badge
            };
        }
    }

    private static void RecommendArenaAugment(bool jax, int?[] ids, CardComparison?[] badges)
    {
        if (badges.Length < 2 || badges.Any(badge => badge?.Local is not ("S" or "A" or "B" or "C")))
            return;
        var bestTier = badges.Select(badge => Enum.Parse<Tier>(badge!.Local)).Min();
        if (bestTier is not (Tier.S or Tier.A))
            return;
        var best = Enumerable.Range(0, badges.Length).Where(index => badges[index]!.Local == bestTier.ToString()).ToArray();
        var index = best.Length == 1 ? best[0] : -1;
        if (jax && bestTier == Tier.A && best.Length == 2)
        {
            if (best.Any(i => ids[i] == Goliath))
                index = best.FirstOrDefault(i => ids[i] == TapDancer, -1);
            else if (best.Any(i => ids[i] == Typhoon))
                index = best.FirstOrDefault(i => ids[i] == Deft, -1);
        }
        if (index >= 0)
            badges[index] = badges[index]! with { RecommendedAugment = true };
    }
}

internal sealed record ArenaChoiceRating(string Name, Tier Tier, string Reason, bool Recommended = false, int? TopPosition = null)
{
    internal string TierText => Tier == Tier.S ? "S+" : Tier.ToString();
    internal string TopLabel => TopPosition is { } position ? $" · Top #{position}" : "";

    internal static CardComparison?[]? Badges(ArenaChoiceRating?[] ratings)
    {
        if (ratings.All(rating => rating is null))
            return null;
        var best = ratings.OfType<ArenaChoiceRating>().Min(rating => rating.Tier);
        var canCompare = ratings.All(rating => rating is not null) && ratings.Any(rating => rating!.Tier != best);
        return ratings.Select(rating => rating is null ? null : new CardComparison(rating.Tier.ToString(),
            ArenaChoice: rating with { Recommended = canCompare && rating.Tier == best })).ToArray();
    }
}
