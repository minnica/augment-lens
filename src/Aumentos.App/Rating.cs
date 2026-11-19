using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Aumentos;

public enum Mode { Caos, Arena }
public enum Tier { S, A, B, C }
public record Champion(string Key, string Name, string[] Roles);
public record Augment(string Mode, int Id, string Key, string Name, string? Description, string[]? Aliases);
public record Catalog(string Patch, string Locale, Champion[] Champions, Augment[] Augments, string[] ArenaItems);
public record Affinity(string Tag, string Keys, string? DescriptionPattern);
public record ExceptionRating(string Mode, string Champion, string Augment, string Tier);
public record EditorialRules(
    Dictionary<string, string[]> Profiles,
    Dictionary<string, string> Traits,
    Affinity[] Affinities,
    ExceptionRating[] Exceptions);

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
    private readonly HashSet<(Mode, int)> variableChoices;
    public Catalog Catalog { get; }

    public Ratings(string assetPath)
    {
        Catalog = Read<Catalog>(Path.Combine(assetPath, "catalog.json"));
        var rules = Read<EditorialRules>(Path.Combine(assetPath, "ratings.json"));
        arenaItems = (Catalog.ArenaItems ?? []).Select(Normalize).ToHashSet(StringComparer.Ordinal);
        variableChoices = Catalog.Augments.Where(card => card.Mode == "Arena" &&
            (card.Key is "ReplaceAugment" or "UnstableTransmutation" || card.Key.StartsWith("Transmute", StringComparison.Ordinal)))
            .Select(card => (Mode.Arena, card.Id)).ToHashSet();
        var groups = Catalog.Augments.GroupBy(card => Enum.Parse<Mode>(card.Mode)).ToArray();
        foreach (var group in groups)
        {
            if (group.Select(card => card.Id).Distinct().Count() != group.Count())
                throw new InvalidDataException($"IDs de aumentos duplicados en {group.Key}.");
        }
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
                overrides.Add((Enum.Parse<Mode>(card.Mode), rule.Champion, card.Id), Enum.Parse<Tier>(rule.Tier));
        }
    }

    public static T Read<T>(string path) =>
        JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions)
        ?? throw new InvalidDataException(path);

    public Augment? Recognize(Mode mode, string text)
    {
        var query = Normalize(text);
        if (mode == Mode.Arena && arenaItems.Contains(query))
            return null;
        if (query.Length < 2 || !names.TryGetValue(mode, out var choices))
            return null;
        var exact = choices.Where(candidate => candidate.Name == query).Select(candidate => candidate.Card).Distinct().ToArray();
        if (exact.Length != 0)
            return exact.Length == 1 ? exact[0] : null;
        if (query.Length < 5)
            return null;

        var ranked = choices.Where(choice => Math.Abs(query.Length - choice.Name.Length) <= 4)
            .Select(choice => (choice.Card, Score: 1.0 - Distance(query, choice.Name) /
                (double)Math.Max(query.Length, choice.Name.Length)))
            .GroupBy(choice => choice.Card.Id)
            .Select(group => group.MaxBy(choice => choice.Score))
            .OrderByDescending(choice => choice.Score).Take(2).ToArray();
        return ranked.Length > 0 && ranked[0].Score >= 0.82
            && (ranked.Length == 1 || ranked[0].Score - ranked[1].Score >= 0.10) ? ranked[0].Card : null;
    }

    public bool IsArenaItem(string title) => arenaItems.Contains(Normalize(title));

    public Tier? Rate(Mode mode, string champion, int augment)
    {
        if (variableChoices.Contains((mode, augment)) || !profiles.TryGetValue(champion, out var profile)
            || !affinities.TryGetValue((mode, augment), out var tags))
            return null;
        if (overrides.TryGetValue((mode, champion, augment), out var tier))
            return tier;
        if (tags.Length == 0)
            return Tier.B;
        return tags.Any(profile.Contains) ? Tier.A : Tier.C;
    }

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

    private static int Distance(string left, string right)
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
