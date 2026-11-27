using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Aumentos;

internal static class NativeItemSets
{
    private static string GameMode(Mode mode) => mode == Mode.Caos ? "KIWI" : "CHERRY";
    private static string Prefix(Mode mode) => $"AumentosLoL-{GameMode(mode)}-";

    internal static async Task Watch(Mode mode, Catalog catalog, string assets, string settings, HttpClient http,
        Action<string> status, CancellationToken token)
    {
        var name = ShopItemSetCatalog.FileName(mode);
        var bundled = Path.Combine(assets, name);
        var cache = Path.Combine(settings, "conjuntos", catalog.Patch, name);
        while (true)
        {
            token.ThrowIfCancellationRequested();
            var install = LeaguePatch.InstallDirectory();
            if (install is null || LeaguePatch.Installed() != catalog.Patch || PatchCatalog.MatchRunning())
            {
                await Task.Delay(TimeSpan.FromSeconds(15), token);
                continue;
            }
            try
            {
                var data = Load(bundled, catalog, mode) ?? Load(cache, catalog, mode);
                if (data is null)
                {
                    data = await ShopItemSetCatalog.Download(mode, catalog, http, token);
                    if (!StatsCache.Write(cache, data))
                        throw new IOException("No se pudo guardar el catálogo de conjuntos.");
                }
                if (PatchCatalog.MatchRunning() || LeaguePatch.Installed() != catalog.Patch)
                    continue;
                int count = Synchronize(install, catalog, data, token);
                status($"Conjuntos de objetos de {ShopItemSetCatalog.ModeName(mode)} · {count} builds de {ShopItemSetCatalog.Provider(mode)} · Parche {LeaguePatch.Public(data.Patch)}");
                return;
            }
            catch (Exception error) when (!token.IsCancellationRequested && error is
                IOException or UnauthorizedAccessException or HttpRequestException or OperationCanceledException or
                JsonException or InvalidDataException or RegexMatchTimeoutException)
            {
                status($"Conjuntos de objetos de {ShopItemSetCatalog.ModeName(mode)} pendientes · {error.Message}");
            }
            await Task.Delay(TimeSpan.FromMinutes(15), token);
        }
    }

    internal static ShopItemSetCatalog? Load(string path, Catalog catalog, Mode mode)
    {
        try
        {
            var data = Ratings.Read<ShopItemSetCatalog>(path);
            return data.Mode == mode && Valid(data, catalog) ? data : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            return null;
        }
    }

    private static bool Valid(ShopItemSetCatalog data, Catalog catalog)
    {
        string releasePrefix = data.Mode == Mode.Caos ? $"data/{catalog.Patch}-" : $"arena/{catalog.Patch}/";
        if (!Enum.IsDefined(data.Mode) || data.Patch != catalog.Patch || data.Champions is not { Length: > 0 } ||
            data.Release?.StartsWith(releasePrefix, StringComparison.Ordinal) != true)
            return false;
        var keys = catalog.Champions.Select(champion => champion.Key).ToHashSet(StringComparer.Ordinal);
        var ids = new HashSet<int>();
        var arenaShop = (catalog.ArenaLegendaries ?? []).Where(item => item.Buyable).Select(item => item.Id).ToHashSet();
        var arenaBoots = (catalog.ArenaEquipment ?? []).Where(item => item.Buyable && item.Categories?.Contains("Boots") == true)
            .Select(item => item.Id).ToHashSet();
        var prismatics = (catalog.ArenaPrismatics ?? []).Select(item => item.Id).ToHashSet();
        foreach (var entry in data.Champions)
        {
            if (entry is not { Id: > 0, Key: not null, Variants.Length: > 0 } || !keys.Remove(entry.Key) ||
                !Regex.IsMatch(entry.Key, @"\A[A-Za-z0-9]+\z") || !ids.Add(entry.Id))
                return false;
            var styles = new HashSet<string>();
            foreach (var variant in entry.Variants)
            {
                if (variant is not { Games: > 0, Start.Length: <= 10, Core.Length: >= 2 and <= 3, Boots.Length: <= 3, Later.Length: <= 5 } ||
                    ShopBuildVariant.StyleName(variant.Style) is null || !styles.Add(variant.Style) ||
                    (data.Mode == Mode.Caos && variant.Core.Length != 3) || variant.Core.Distinct().Count() != variant.Core.Length ||
                    variant.Prismatics is { Length: > 3 } ||
                    variant.Start.Concat(variant.Core).Concat(variant.Boots).Concat(variant.Later).Concat(variant.Prismatics ?? []).Any(id => id <= 0))
                    return false;
                if (data.Mode == Mode.Arena && (variant.Start.Length > 0 || !variant.Core.Concat(variant.Later).All(arenaShop.Contains) ||
                    !variant.Boots.All(arenaBoots.Contains) || !(variant.Prismatics ?? []).All(prismatics.Contains)))
                    return false;
            }
        }
        return true;
    }

    internal static int Synchronize(string install, Catalog catalog, ShopItemSetCatalog data, CancellationToken token)
    {
        if (!Valid(data, catalog))
            throw new InvalidDataException("El catálogo de conjuntos no corresponde al parche o contiene una ruta incompleta.");
        var names = catalog.Champions.ToDictionary(champion => champion.Key, champion => champion.Name);
        var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var root = ChampionFolder(install);
        bool mayhem = data.Mode == Mode.Caos;
        foreach (var entry in data.Champions)
        {
            token.ThrowIfCancellationRequested();
            var folder = Path.Combine(root, entry.Key, "Recommended");
            Directory.CreateDirectory(folder);
            foreach (var (variant, rank) in entry.Variants.Select((variant, index) => (variant, index)))
            {
                string path = Path.Combine(folder, Prefix(data.Mode) + variant.Style + ".json");
                var uid = Uid(data.Mode, entry.Key, variant.Style);
                var blocks = new List<object>();
                AddBlock("Inicio", variant.Start);
                AddBlock($"{(mayhem ? "Orden de compra" : "Compras principales")} · {LeaguePatch.Public(data.Patch)}", variant.Core);
                AddBlock(mayhem ? "Botas" : "Botas del campeón", variant.Boots);
                AddBlock(mayhem || variant.Style == "general" ? "Opciones para completar" : "Alternativas por afinidad", variant.Later);
                AddBlock("Prismáticos del estilo", variant.Prismatics ?? []);
                var set = new
                {
                    title = $"{ShopBuildVariant.StyleName(variant.Style)} · {names[entry.Key]} · Aumentos LoL",
                    type = "custom", map = mayhem ? "HA" : "any", mode = GameMode(data.Mode), associatedMaps = new[] { mayhem ? 12 : 30 },
                    associatedChampions = new[] { entry.Id }, preferredItemSlots = Array.Empty<object>(),
                    sortrank = rank, startedFrom = "blank", uid, blocks
                };
                keep.Add(path);
                if (File.Exists(path))
                {
                    var previous = File.ReadAllText(path);
                    if (previous == JsonSerializer.Serialize(set)) continue;
                    if (!HasUid(previous, uid))
                        throw new IOException($"Ya existe un conjunto ajeno en {path}.");
                }
                if (!StatsCache.Write(path, set))
                    throw new IOException($"No se pudo guardar el conjunto de {names[entry.Key]} en {folder}.");

                void AddBlock(string label, int[] ids)
                {
                    if (ids.Length == 0) return;
                    blocks.Add(new
                    {
                        type = label, showIfSummonerSpell = "", hideIfSummonerSpell = "",
                        items = ids.GroupBy(id => id).Select(group => new
                        {
                            id = group.Key.ToString(System.Globalization.CultureInfo.InvariantCulture), count = group.Count()
                        }).ToArray()
                    });
                }
            }
        }
        RemoveObsolete(install, data.Mode, keep);
        return keep.Count;
    }

    // These are local shop files. The account's ItemSets.json and files exported by the League client have other owners.
    internal static void RemoveObsolete(string install, Mode mode, HashSet<string>? keep = null)
    {
        var root = ChampionFolder(install);
        if (!Directory.Exists(root)) return;
        foreach (var champion in Directory.EnumerateDirectories(root))
        {
            string folder = Path.Combine(champion, "Recommended");
            if (!Directory.Exists(folder)) continue;
            foreach (var path in Directory.EnumerateFiles(folder, Prefix(mode) + "*.json"))
            {
                if (keep?.Contains(path) == true) continue;
                var style = Path.GetFileNameWithoutExtension(path)[Prefix(mode).Length..];
                if (HasUid(File.ReadAllText(path), Uid(mode, Path.GetFileName(champion), style)))
                    File.Delete(path);
            }
        }
    }

    private static string ChampionFolder(string install) => Path.Combine(install, "Config", "Champions");
    private static string Uid(Mode mode, string champion, string style) =>
        new Guid(SHA256.HashData(Encoding.UTF8.GetBytes($"AumentosLoL/{GameMode(mode)}/{champion}/{style}")).AsSpan(0, 16)).ToString();

    private static bool HasUid(string json, string uid)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("uid", out var value)
                && value.ValueKind == JsonValueKind.String && value.ValueEquals(uid);
        }
        catch (JsonException) { return false; }
    }
}
