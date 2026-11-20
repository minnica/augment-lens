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

internal sealed record MayhemObservedTier(Tier Tier, int Games, string Patch, bool IsPrevious)
{
    internal string Sample => $"{Games.ToString("N0", CultureInfo.GetCultureInfo("es-MX"))} partidas";
    internal string PatchText => $"{(IsPrevious ? "Parche anterior" : "Parche")} {Patch}";
}

internal sealed record CardComparison(string Local, OpGgCard? External = null, bool Loading = false,
    MayhemPopularity? Mayhem = null, ArenaPopularity? Arena = null, string? ExternalPreviousPatch = null,
    ArenaChoiceRating? ArenaChoice = null, bool IsArenaAugment = false, string? LocalReason = null,
    bool RecommendedUpgrade = false, bool RecommendedAugment = false, bool UsesWiki = false,
    int? UpgradeLevel = null, string? UpgradeEffect = null,
    ArenaPlacementRating? ArenaPlacement = null, bool PreferLocalArenaTier = false, MayhemObservedTier? MayhemObserved = null)
{
    internal static readonly CardComparison Unread = new("?");
    internal const string Missing = "Sin dato";
    internal bool IsArenaVariable => IsArenaAugment && Local == "Variable";
    internal string LocalText => ArenaChoice?.TierText ?? (Local == "S" && IsArenaAugment ? "S+" : Local);
    internal ArenaPlacementRating? DisplayedArenaPlacement => PreferLocalArenaTier ? null : ArenaPlacement;
    internal MayhemObservedTier? DisplayedMayhemObserved => External?.Tier is null &&
        MayhemObserved is { } observed && (!observed.IsPrevious || Local == Missing) ? observed : null;
    internal string VisibleTierText => DisplayedArenaPlacement?.Tier.ToString() ?? External?.Tier ??
        DisplayedMayhemObserved?.Tier.ToString() ?? LocalText;
    internal string ArenaTitle => (DisplayedArenaPlacement is { } observed
        ? $"Tier {observed.Source} {observed.Tier} · {observed.Sample} · {observed.Patch}"
        : Local == "Variable" ? "Valoración variable" : Local == Missing ? "Sin muestra suficiente" : $"Tier local   {LocalText}") +
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
        GgezArenaSnapshot? ggez = null, int?[]? upgradeLevels = null, IReadOnlyList<int>? arenaItems = null,
        GgezArenaSnapshot? ggezGlobal = null, AramHexSnapshot? aramHex = null)
    {
        var external = snapshot?.Mode == game.Mode && snapshot.Patch == ratings.Catalog.Patch &&
            string.Equals(snapshot.Champion, game.Champion, StringComparison.OrdinalIgnoreCase) ? snapshot.Cards : null;
        var externalPrevious = external is not null && snapshot is { IsPrevious: true }
            ? LeaguePatch.Public(snapshot.DataPatch) : null;
        var global = game.Mode == Mode.Caos && mayhem?.ClientPatch == ratings.Catalog.Patch ? mayhem : null;
        var hex = game.Mode == Mode.Caos && aramHex?.ClientPatch == ratings.Catalog.Patch ? aramHex : null;
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
            var observed = hex is not null && variable is null && hex.Find(game.Champion, id.Value) is { } sample
                ? new MayhemObservedTier(sample.Tier, sample.Games, LeaguePatch.Public(hex.SourcePatch),
                    hex.SourcePatch != hex.ClientPatch)
                : null;
            return new CardComparison(local, published, loading, popularity, arena,
                published is null ? null : externalPrevious, IsArenaAugment: game.Mode == Mode.Arena,
                LocalReason: variable ?? ratings.DefaultReason(game.Mode, id.Value), UpgradeLevel: levels?[i],
                UpgradeEffect: levels?[i] is { } level ? ratings.ArenaAugment(id.Value)?.EffectAt(level) : null,
                PreferLocalArenaTier: game.Mode == Mode.Arena && ratings.HasOverride(Mode.Arena, game.Champion, id.Value),
                MayhemObserved: observed);
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
            if (!isUpgrade)
                RecommendArenaControlTieBreak(ratings, game, ids, badges);
            if (!isUpgrade)
                ApplyArenaPlacement(ids, badges, championStats,
                    ggezGlobal is { Champion: "" } && ggezGlobal.ClientPatch == ratings.Catalog.Patch ? ggezGlobal : null);
        }
        return badges;
    }

    private static void ApplyArenaPlacement(int?[] ids, CardComparison?[] badges,
        GgezArenaSnapshot? championStats, GgezArenaSnapshot? globalStats)
    {
        if (championStats is null && globalStats is null)
            return;

        var championRatings = ArenaPlacementRatings.Calculate(championStats);
        var globalRatings = ArenaPlacementRatings.Calculate(globalStats);
        bool localComparison = badges.Any(badge => badge is { RecommendedAugment: true, PreferLocalArenaTier: true });
        for (int index = 0; index < badges.Length; index++)
        {
            if (ids[index] is not { } id || badges[index] is not { IsArenaAugment: true, Local: not "Variable" } badge)
                continue;
            var observed = championRatings.GetValueOrDefault(id);
            if (observed is null && badge.Local == Missing)
                observed = globalRatings.GetValueOrDefault(id);
            if (observed is not null)
                badges[index] = badge with { ArenaPlacement = observed, PreferLocalArenaTier = localComparison || badge.PreferLocalArenaTier };
        }

        if (!badges.Any(badge => badge?.DisplayedArenaPlacement is not null))
            return;
        int? winner = ArenaPlacementRatings.Recommend(badges);
        for (int index = 0; index < badges.Length; index++)
        {
            if (badges[index] is { IsArenaAugment: true } badge)
                badges[index] = badge with { RecommendedAugment = winner == index };
        }
    }

    private const int Deft = 22;
    private const int Goliath = 41;
    private const int TapDancer = 81;
    private const int Tormentor = 85;
    private const int Typhoon = 87;
    private const int SlapAround = 136;

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
        var best = ArenaLeaders(badges);
        var index = best.Length == 1 ? best[0] : -1;
        if (jax && best.Length == 2 && badges[best[0]]?.Local == "A")
        {
            if (best.Any(i => ids[i] == Goliath))
                index = best.FirstOrDefault(i => ids[i] == TapDancer, -1);
            else if (best.Any(i => ids[i] == Typhoon))
                index = best.FirstOrDefault(i => ids[i] == Deft, -1);
        }
        if (index >= 0)
            badges[index] = badges[index]! with { RecommendedAugment = true };
    }

    private static void RecommendArenaControlTieBreak(Ratings ratings, GameContext game, int?[] ids, CardComparison?[] badges)
    {
        if (badges.Any(badge => badge?.RecommendedAugment == true) || ArenaLeaders(badges) is not [var first, var second])
            return;
        var winner = (ids[first], ids[second]) switch
        {
            (Tormentor, SlapAround) => first,
            (SlapAround, Tormentor) => second,
            _ => -1
        };
        if (winner < 0 || ratings.Wiki?.Find(Tormentor) is not { Disabled: not true, Removed: not true } ||
            ratings.Wiki.Find(SlapAround) is not { Disabled: not true, Removed: not true })
            return;

        var champion = ratings.Catalog.Champions.FirstOrDefault(champion =>
            string.Equals(champion.Key, game.Champion, StringComparison.OrdinalIgnoreCase));
        if (champion is not { Roles: ["Tank", ..] } || !champion.Roles.Contains("Support") ||
            !ratings.HasTrait(game.Champion, "control") || ratings.HasTrait(game.Champion, "ap") ||
            ratings.HasTrait(game.Champion, "attacks"))
            return;

        // Both cards trigger from the same control. A tank support can secure the burn with the engage itself.
        badges[winner] = badges[winner]! with
        {
            RecommendedAugment = true,
            PreferLocalArenaTier = true,
            LocalReason = "Tus inmovilizaciones aplican una quemadura acumulable que escala con la vida máxima del rival",
            UsesWiki = true
        };
    }

    private static int[] ArenaLeaders(CardComparison?[] badges)
    {
        if (badges.Length < 2 || badges.Any(badge => badge?.Local is not ("S" or "A" or "B" or "C")))
            return [];
        var tier = badges.Min(badge => Enum.Parse<Tier>(badge!.Local));
        if (tier is not (Tier.S or Tier.A))
            return [];
        return Enumerable.Range(0, badges.Length).Where(index => badges[index]!.Local == tier.ToString()).ToArray();
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
