using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Aumentos;

public enum Mode { Caos, Arena }
public enum Tier { S, A, B, C }
public record Champion(string Key, string Name, string[] Roles);
public record Augment(string Mode, int Id, string Key, string Name, string? Description, string[]? Aliases,
    int? AnvilCount = null, int? MaxLevel = null, string[]? UpgradeEffects = null, int[]? AnvilUpgradeCounts = null)
{
    internal int? AnvilCountAt(int? level) => level is null or 1 ? AnvilCount :
        level >= 2 && AnvilUpgradeCounts is { } counts && level - 2 < counts.Length ? counts[level.Value - 2] : null;

    internal string? EffectAt(int? level) =>
        level is >= 2 && UpgradeEffects is not null && level - 2 < UpgradeEffects.Length
            ? UpgradeEffects[level.Value - 2] : null;
}
public record Catalog(string Patch, string Locale, Champion[] Champions, Augment[] Augments, string[] ArenaItems,
    ArenaItem[]? ArenaPrismatics = null, ArenaItem[]? ArenaLegendaries = null, ArenaItem[]? ArenaEquipment = null,
    Dictionary<string, string>? Sources = null);
public record Affinity(string Tag, string Keys, string? DescriptionPattern);
public record ExceptionRating(string Mode, string Champion, string Augment, string Tier);
public record ArenaDefaultRating(string Augment, [property: JsonConverter(typeof(JsonStringEnumConverter<Tier>))] Tier Tier,
    string Reason);
public record EditorialRules(
    Dictionary<string, string[]> Profiles,
    Dictionary<string, string> Traits,
    Affinity[] Affinities,
    ExceptionRating[] Exceptions,
    ArenaDefaultRating[]? ArenaDefaults = null);

public sealed class Ratings
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true, WriteIndented = true
    };

    private readonly Dictionary<Mode, (Augment Card, string Name)[]> names;
    private readonly Dictionary<string, HashSet<string>> profiles;
    private readonly Dictionary<(Mode, int), string[]> affinities;
    private readonly Dictionary<(Mode, string, int), Tier> overrides;
    private readonly HashSet<string> arenaItems;
    private readonly Dictionary<int, string> arenaVariableChoices;
    private readonly Dictionary<int, ArenaDefaultRating> arenaDefaults;
    public Catalog Catalog { get; }
    internal ArenaPrismaticRatings Prismatics { get; }
    internal ArenaLegendaryRatings Legendaries { get; }
    internal StatShardRatings Shards { get; }
    internal ArenaWikiMechanics? Wiki { get; }
    internal ArenaAugmentAffinity AugmentAffinity { get; }
    private readonly Dictionary<int, Augment> arenaAugments;

    public Ratings(string assetPath)
    {
        Catalog = Read<Catalog>(Path.Combine(assetPath, "catalog.json"));
        var rules = Read<EditorialRules>(Path.Combine(assetPath, "ratings.json"));
        arenaItems = (Catalog.ArenaItems ?? []).Select(Normalize).ToHashSet(StringComparer.Ordinal);
        arenaVariableChoices = Catalog.Augments.Where(card => card.Mode == nameof(Mode.Arena))
            .Select(card => (card.Id, Reason: ArenaVariableReason(card.Key)))
            .Where(choice => choice.Reason is not null).ToDictionary(choice => choice.Id, choice => choice.Reason!);
        arenaDefaults = Catalog.Augments.Where(card => card.Mode == nameof(Mode.Arena))
            .Join(rules.ArenaDefaults ?? [], card => MechanicKey(card.Key), rule => MechanicKey(rule.Augment),
                (card, rule) => (card.Id, Rule: rule))
            .ToDictionary(choice => choice.Id, choice => choice.Rule);
        var groups = Catalog.Augments.GroupBy(card => Enum.Parse<Mode>(card.Mode)).ToArray();
        foreach (var group in groups)
        {
            if (group.Select(card => card.Id).Distinct().Count() != group.Count())
                throw new InvalidDataException($"IDs de aumentos duplicados en {group.Key}.");
        }
        arenaAugments = Catalog.Augments.Where(card => card.Mode == nameof(Mode.Arena)).ToDictionary(card => card.Id);
        Wiki = ArenaWikiMechanics.Load(assetPath, Catalog);
        names = groups.ToDictionary(group => group.Key, group => group.SelectMany(card =>
            new[] { card.Name }.Concat(card.Aliases ?? []).Select(name => (card, Normalize(name))))
            .Distinct().ToArray());

        profiles = Catalog.Champions.ToDictionary(champion => champion.Key, champion =>
        {
            if (!rules.Profiles.TryGetValue(champion.Roles[0], out var defaults))
                throw new InvalidDataException($"Falta el perfil de {champion.Key}.");
            var tags = new HashSet<string>(defaults);
            foreach (var (tag, keys) in rules.Traits)
            {
                if (keys.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(champion.Key))
                    tags.Add(tag);
            }
            return tags;
        }, StringComparer.OrdinalIgnoreCase);
        Prismatics = new ArenaPrismaticRatings(Catalog.ArenaPrismatics ?? [], profiles);
        Legendaries = new ArenaLegendaryRatings(Catalog.ArenaLegendaries ?? [], Catalog.Champions, profiles);
        Shards = new StatShardRatings(Catalog.Champions, profiles);

        var descriptions = Catalog.Augments.Where(card => card.Mode == "Arena")
            .GroupBy(card => MechanicKey(card.Key)).ToDictionary(group => group.Key, group => group.First().Description);
        var patterns = rules.Affinities.Select(rule => (
            rule.Tag,
            new Regex(rule.Keys, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            rule.DescriptionPattern is null ? null : new Regex(rule.DescriptionPattern, RegexOptions.CultureInvariant)
        )).ToArray();
        affinities = Catalog.Augments.ToDictionary(card => (Enum.Parse<Mode>(card.Mode), card.Id), card =>
        {
            var key = MechanicKey(card.Key);
            var description = Normalize(card.Description ?? descriptions.GetValueOrDefault(key) ?? "");
            return patterns.Where(rule => rule.Item2.IsMatch(key) || rule.Item3?.IsMatch(description) == true)
                .Select(rule => rule.Tag).Distinct().ToArray();
        });

        overrides = new Dictionary<(Mode, string, int), Tier>();
        foreach (var rule in rules.Exceptions)
        {
            if (!profiles.ContainsKey(rule.Champion))
                throw new InvalidDataException($"Campeón desconocido en reglas: {rule.Champion}.");
            var cards = Catalog.Augments.Where(card => MechanicKey(card.Key) == MechanicKey(rule.Augment)
                && (rule.Mode == "*" || rule.Mode == card.Mode)).ToArray();
            if (cards.Length == 0)
                throw new InvalidDataException($"Aumento desconocido en reglas: {rule.Augment}.");
            foreach (var card in cards)
                overrides.Add((Enum.Parse<Mode>(card.Mode), rule.Champion.ToLowerInvariant(), card.Id), Enum.Parse<Tier>(rule.Tier));
        }
        AugmentAffinity = new ArenaAugmentAffinity(this);
    }

    public static T Read<T>(string path) =>
        JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions)
        ?? throw new InvalidDataException(path);

    public Augment? Recognize(Mode mode, string text) =>
        FindTitle(mode, text) is [var only] ? only : null;

    internal Augment[] FindTitle(Mode mode, string text)
    {
        var query = Normalize(text);
        if (mode == Mode.Arena && arenaItems.Contains(query))
            return [];
        if (query.Length < 2 || !names.TryGetValue(mode, out var choices))
            return [];
        var exact = Exact(query);
        if (exact.Length != 0)
            return exact;
        const string missingTierBadge = "estimacion local sin dato ";
        if (mode == Mode.Arena && query.StartsWith(missingTierBadge, StringComparison.Ordinal))
            return Exact(query[missingTierBadge.Length..]);
        if (mode == Mode.Arena && Regex.Match(text, @"^\s*\(\d+\)\s+(.+)$") is { Success: true } prefix)
        {
            var title = Normalize(prefix.Groups[1].Value);
            return arenaItems.Contains(title) ? [] : Exact(title);
        }
        if (query.Length < 5)
            return [];

        var ranked = choices.Where(choice => Math.Abs(query.Length - choice.Name.Length) <= 4)
            .Select(choice => (choice.Card, choice.Name, Score: 1.0 - Distance(query, choice.Name) /
                (double)Math.Max(query.Length, choice.Name.Length)))
            .GroupBy(choice => choice.Card.Id)
            .Select(group => group.MaxBy(choice => choice.Score))
            .OrderByDescending(choice => choice.Score).ToArray();
        if (ranked.Length == 0 || ranked[0].Score < 0.82)
            return [];
        var best = ranked[0];
        var close = ranked.TakeWhile(choice => best.Score - choice.Score < 0.10).ToArray();
        return close.All(choice => choice.Name == best.Name) ? close.Select(choice => choice.Card).ToArray() : [];

        Augment[] Exact(string title) => choices.Where(candidate => candidate.Name == title)
            .Select(candidate => candidate.Card).Distinct().ToArray();
    }

    public bool IsArenaItem(string title) => arenaItems.Contains(Normalize(title));

    internal string? RecoverArenaItemTitle(string text)
    {
        var query = Normalize(text);
        return arenaItems.Where(name => name.Length >= 16 && name.Contains(' ') &&
            (query == name[1..] || query.EndsWith(" " + name[1..], StringComparison.Ordinal)))
            .Take(2).ToArray() is [var unique] ? unique : null;
    }

    internal bool HasTrait(string champion, string trait) =>
        profiles.TryGetValue(champion, out var profile) && profile.Contains(trait);

    internal string? VariableReason(Mode mode, int augment) =>
        mode == Mode.Arena ? arenaVariableChoices.GetValueOrDefault(augment) : null;

    internal string? DefaultReason(Mode mode, int augment) =>
        mode == Mode.Arena ? arenaDefaults.GetValueOrDefault(augment)?.Reason : null;

    internal Augment? ArenaAugment(int id) => arenaAugments.GetValueOrDefault(id);

    internal bool HasOverride(Mode mode, string champion, int augment) =>
        overrides.ContainsKey((mode, champion.ToLowerInvariant(), augment));

    public Tier? Rate(Mode mode, string champion, int augment)
    {
        var editorial = RateEditorial(mode, champion, augment);
        if (editorial.HasValue || mode != Mode.Arena || VariableReason(mode, augment) is not null || !profiles.ContainsKey(champion))
            return editorial;
        return AugmentAffinity.Evaluate(champion, augment)?.Tier;
    }

    internal Tier? RateEditorial(Mode mode, string champion, int augment)
    {
        if (VariableReason(mode, augment) is not null || !profiles.TryGetValue(champion, out var profile)
            || !affinities.TryGetValue((mode, augment), out var tags))
            return null;
        if (overrides.TryGetValue((mode, champion.ToLowerInvariant(), augment), out var tier))
            return tier;
        if (mode == Mode.Arena && arenaDefaults.TryGetValue(augment, out var common))
            return common.Tier;
        if (tags.Length == 0)
            return null;
        return tags.Any(profile.Contains) ? Tier.A : Tier.C;
    }

    private static string? ArenaVariableReason(string key) => key switch
    {
        "TrashToTreasure" => "Prismático al eliminar o reemplazar esta carta",
        "ParasiticMutation" => "Copia un aumento rival cada ronda",
        "SymbioticMutation" => "Copia un aumento aliado cada ronda",
        "AugmentedPower" => "Depende del daño de tus objetos y aumentos",
        "ReplaceAugment" => "Depende del aumento que reemplaces",
        "UnstableTransmutation" => "Depende del aumento que resulte",
        _ when key.StartsWith("Transmute", StringComparison.Ordinal) => "Depende del aumento que resulte",
        _ => null
    };

    private static string MechanicKey(string key) => key.StartsWith("ARAM_", StringComparison.OrdinalIgnoreCase)
        ? key[5..].ToLowerInvariant() : key.ToLowerInvariant();

    public static string Normalize(string value)
    {
        var output = new StringBuilder();
        foreach (var symbol in value.ToLowerInvariant().Normalize(NormalizationForm.FormD))
        {
            if (char.IsLetterOrDigit(symbol))
                output.Append(symbol);
            else if (char.GetUnicodeCategory(symbol) != UnicodeCategory.NonSpacingMark &&
                     output.Length != 0 && output[^1] != ' ')
                output.Append(' ');
        }
        return output.ToString().TrimEnd();
    }

    internal static int Distance(string left, string right)
    {
        Span<int> row = stackalloc int[right.Length + 1];
        for (int column = 0; column < row.Length; column++)
            row[column] = column;
        for (int index = 0; index < left.Length; index++)
        {
            var diagonal = row[0];
            row[0] = index + 1;
            for (int column = 1; column < row.Length; column++)
            {
                var above = row[column];
                row[column] = Math.Min(Math.Min(above, row[column - 1]) + 1,
                    diagonal + (left[index] == right[column - 1] ? 0 : 1));
                diagonal = above;
            }
        }
        return row[^1];
    }
}
