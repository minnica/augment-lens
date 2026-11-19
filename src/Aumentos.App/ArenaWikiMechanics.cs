using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Aumentos;

[Flags]
[JsonConverter(typeof(JsonStringEnumConverter<ArenaWikiSignal>))]
internal enum ArenaWikiSignal
{
    None = 0,
    AppliesOnHitViaAbilities = 1 << 0,
    AppliesOnHitToAdditionalTarget = 1 << 1,
    RepeatsOnHitOnAttacks = 1 << 2,
    MaxHealthDamageOnHit = 1 << 3,
    OwnMaxHealthDamageOnAttacks = 1 << 4,
    AttackSpeedThresholdDamage = 1 << 5,
    AttacksForBonusMagicDamage = 1 << 6,
    GrantsAttackSpeed = 1 << 7,
    RequiresHeartsteel = 1 << 8,
    AbilitiesCanCrit = 1 << 9
}

// Descriptions and levels preserve the source Wiki markup and revision, which may predate the game patch.
internal sealed record ArenaWikiAugment(string Name, string Rarity, string Description,
    string? Level1, string? Level2, string? Level3, string? Notes,
    ArenaWikiSignal Signals, bool? Disabled, bool? Removed);

internal sealed class ArenaWikiMechanics
{
    internal const string Attribution = "League of Legends Wiki";
    private const string AssetFile = "arena-wiki-mechanics.json";
    private readonly Snapshot snapshot;
    private readonly Dictionary<int, (string Key, ArenaWikiAugment Card)> byId;

    private ArenaWikiMechanics(Snapshot snapshot, Dictionary<int, (string Key, ArenaWikiAugment Card)> byId)
    {
        this.snapshot = snapshot;
        this.byId = byId;
    }

    internal string SourceName => snapshot.SourceName;
    internal string SourcePage => snapshot.SourcePage;
    internal string RevisionUrl => snapshot.RevisionUrl;
    internal long SourceRevision => snapshot.SourceRevision;
    internal DateTimeOffset SourceUpdatedAt => snapshot.SourceUpdatedAt;
    internal string License => snapshot.License;
    internal string LicenseUrl => snapshot.LicenseUrl;
    internal string CatalogPatch => snapshot.CatalogPatch;
    internal bool ValuesMatchPatch => snapshot.VerifiedGamePatch == CatalogPatch;
    internal int SourceCount => snapshot.Cards.Length;
    internal int MappedCount => byId.Count;

    internal ArenaWikiAugment? Find(int id) => byId.GetValueOrDefault(id).Card;

    internal ArenaWikiAugment? Find(Augment card) => card.Mode == nameof(Mode.Arena) &&
        byId.TryGetValue(card.Id, out var entry) && entry.Key == card.Key ? entry.Card : null;

    internal static ArenaWikiMechanics? Load(string assetPath, Catalog catalog)
    {
        var path = Path.Combine(assetPath, AssetFile);
        if (!File.Exists(path))
            return null;

        try
        {
            var data = Ratings.Read<Snapshot>(path);
            if (data.Schema != 1 || data.CatalogPatch != catalog.Patch || data.SourceRevision <= 0 ||
                data.SourceUpdatedAt == default || string.IsNullOrWhiteSpace(data.SourceName) ||
                string.IsNullOrWhiteSpace(data.SourcePage) || string.IsNullOrWhiteSpace(data.RevisionUrl) ||
                string.IsNullOrWhiteSpace(data.License) || string.IsNullOrWhiteSpace(data.LicenseUrl) ||
                data.Cards is not { Length: > 0 } || data.Bindings is not { Length: > 0 })
                return null;

            var cards = new Dictionary<string, ArenaWikiAugment>(StringComparer.Ordinal);
            foreach (var card in data.Cards)
            {
                if (card is null || string.IsNullOrWhiteSpace(card.Name) || string.IsNullOrWhiteSpace(card.Description) ||
                    card.Rarity is not ("Silver" or "Gold" or "Prismatic") || !cards.TryAdd(card.Name, card))
                    return null;
            }

            var arenaKeys = catalog.Augments.Where(card => card.Mode == nameof(Mode.Arena))
                .Select(card => (card.Id, card.Key)).ToHashSet();
            var bindings = new Dictionary<int, (string, ArenaWikiAugment)>();
            var boundNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var binding in data.Bindings)
            {
                if (binding is null || binding.Name is null || !arenaKeys.Contains((binding.Id, binding.Key)) ||
                    !cards.TryGetValue(binding.Name, out var card) || !boundNames.Add(binding.Name) ||
                    !bindings.TryAdd(binding.Id, (binding.Key, card)))
                    return null;
            }

            return new ArenaWikiMechanics(data, bindings);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private sealed record Binding(int Id, string Key, string Name);

    private sealed record Snapshot(int Schema, string SourceName, string SourcePage, long SourceRevision,
        DateTimeOffset SourceUpdatedAt, string RevisionUrl, string License, string LicenseUrl, string CatalogPatch,
        string? VerifiedGamePatch, ArenaWikiAugment[] Cards, Binding[] Bindings);
}
