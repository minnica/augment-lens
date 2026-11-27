using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Aumentos;

internal sealed record ShopBuildVariant(string Style, int Games, int[] Start, int[] Core, int[] Boots, int[] Later, int[]? Prismatics = null)
{
    internal static string? StyleName(string key) => key switch
    {
        "ap" => "AP", "ad" => "AD", "tank" => "Tanque", "bruiser" => "Luchador", "lethality" => "Letalidad",
        "crit" => "Crítico", "on_hit" => "Al impacto", "utility" => "Utilidad", "hybrid" => "Híbrida",
        "ap_burn" => "AP de desgaste", "support" => "Soporte", "general" => "General", _ => null
    };
}

internal sealed record ChampionItemSets(string Key, int Id, ShopBuildVariant[] Variants);
internal sealed record DataDragonTable<T>(Dictionary<string, T> Data);

internal sealed record ShopItemSetCatalog(Mode Mode, string Patch, string Release, ChampionItemSets[] Champions)
{
    private sealed record DragonChampion(string Key);

    internal static string FileName(Mode mode) => mode == Mode.Caos ? "mayhem-item-sets.json" : "arena-item-sets.json";
    internal static string Provider(Mode mode) => mode == Mode.Caos ? "ARAMKit" : "LoLArena";
    internal static string ModeName(Mode mode) => mode == Mode.Caos ? "ARAM Caos" : "Arena";
    internal static Task<ShopItemSetCatalog> Download(Mode mode, Catalog catalog, HttpClient http, CancellationToken token) =>
        mode == Mode.Caos ? AramKitItemSets.Download(catalog, http, token) : LolArenaItemSets.Download(catalog, http, token);

    internal static async Task<Dictionary<string, int>> ChampionIds(HttpClient http, string patch, CancellationToken token)
    {
        var url = $"https://ddragon.leagueoflegends.com/cdn/{patch}.1/data/es_MX/champion.json";
        var index = await http.GetFromJsonAsync<DataDragonTable<DragonChampion>>(url, token);
        if (index?.Data is not { Count: > 0 }) throw new InvalidDataException("Falta el índice de campeones de Riot.");
        return index.Data.ToDictionary(entry => entry.Key, entry => int.Parse(entry.Value.Key, CultureInfo.InvariantCulture),
            StringComparer.OrdinalIgnoreCase);
    }
}
