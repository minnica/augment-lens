using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;

namespace Aumentos;

internal sealed class ChampionSelectPortraits(Catalog catalog, string settings, HttpClient http)
{
    internal string Version { get; } = DataDragonVersion(catalog);

    private readonly HashSet<string> keys = catalog.Champions.Select(champion => champion.Key).ToHashSet(StringComparer.Ordinal);
    private readonly object loadGate = new();
    private Task<Dictionary<string, BitmapSource>?>? load;
    private long retryAt;

    internal async Task<BitmapSource?> Read(string nameOrKey)
    {
        var champion = catalog.Champions.FirstOrDefault(champion =>
            string.Equals(champion.Key, nameOrKey, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(champion.Name, nameOrKey, StringComparison.OrdinalIgnoreCase));
        return champion is null ? null : (await ReadAll().ConfigureAwait(false))?.GetValueOrDefault(champion.Key);
    }

    internal Task<Dictionary<string, BitmapSource>?> ReadAll()
    {
        lock (loadGate)
        {
            if (load is null || (load.IsCompletedSuccessfully && load.Result is null && Environment.TickCount64 >= retryAt))
            {
                retryAt = Environment.TickCount64 + 60000;
                load = Task.Run(Load);
            }
            return load;
        }
    }

    private async Task<Dictionary<string, BitmapSource>?> Load()
    {
        var path = Path.Combine(settings, "champion-portraits", Version + ".json");
        try
        {
            if (Decode(Ratings.Read<SpriteCache>(path)) is { } cached)
                return cached;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or
            ArgumentException or NotSupportedException or InvalidOperationException or FileFormatException or InvalidDataException) { }

        try
        {
            var root = $"https://ddragon.leagueoflegends.com/cdn/{Version}";
            var metadata = await http.GetFromJsonAsync<ChampionImages>(root + "/data/en_US/champion.json", Ratings.JsonOptions)
                .ConfigureAwait(false);
            var regions = metadata?.Data?.Where(pair => keys.Contains(pair.Key) && IsSpriteRegion(pair.Value?.Image))
                .ToDictionary(pair => pair.Key, pair => pair.Value.Image);
            if (regions is null || regions.Count < MinimumPortraits)
                return null;

            var downloads = await Task.WhenAll(regions.Values.Select(region => region.Sprite).Distinct().Select(async file =>
                (File: file, Bytes: await http.GetByteArrayAsync(root + "/img/sprite/" + file).ConfigureAwait(false))))
                .ConfigureAwait(false);
            var bundle = new SpriteCache(regions, downloads.ToDictionary(sprite => sprite.File, sprite => sprite.Bytes));
            var portraits = Decode(bundle);
            if (portraits is not null)
                StatsCache.Write(path, bundle);
            return portraits;
        }
        catch (Exception error) when (error is HttpRequestException or IOException or JsonException or OperationCanceledException or
            ArgumentException or NotSupportedException or InvalidOperationException or FileFormatException or InvalidDataException)
        {
            return null;
        }
    }

    private int MinimumPortraits => Math.Max(1, keys.Count / 2);

    private Dictionary<string, BitmapSource>? Decode(SpriteCache cache)
    {
        if (cache.Regions is null || cache.Sprites is null)
            return null;

        var portraits = new Dictionary<string, BitmapSource>();
        var groups = cache.Regions.Where(pair => keys.Contains(pair.Key) && IsSpriteRegion(pair.Value))
            .GroupBy(pair => pair.Value.Sprite);
        foreach (var group in groups)
        {
            if (cache.Sprites.GetValueOrDefault(group.Key) is not { } bytes)
                return null;

            using var stream = new MemoryStream(bytes, writable: false);
            var sprite = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            foreach (var (key, box) in group)
            {
                if (box.X > sprite.PixelWidth - box.W || box.Y > sprite.PixelHeight - box.H)
                    continue;

                var portrait = new CroppedBitmap(sprite, new Int32Rect(box.X, box.Y, box.W, box.H));
                portrait.Freeze();
                portraits.Add(key, portrait);
            }
        }
        return portraits.Count >= MinimumPortraits ? portraits : null;
    }

    private static bool IsSpriteRegion(SpriteRegion? region) =>
        region is { W: 48, H: 48, X: >= 0, Y: >= 0, Sprite: { } file } &&
        file.StartsWith("champion", StringComparison.Ordinal) && file.EndsWith(".png", StringComparison.Ordinal) &&
        int.TryParse(file.AsSpan(8, file.Length - 12), NumberStyles.None, CultureInfo.InvariantCulture, out _);

    private static string DataDragonVersion(Catalog catalog)
    {
        const string prefix = "Data Dragon ";
        var configured = catalog.Sources?.GetValueOrDefault("champions");
        return configured?.StartsWith(prefix, StringComparison.Ordinal) == true &&
            System.Version.TryParse(configured[prefix.Length..], out var release) &&
            release.ToString(2) == catalog.Patch ? release.ToString() : catalog.Patch + ".1";
    }

    private sealed record SpriteCache(Dictionary<string, SpriteRegion> Regions, Dictionary<string, byte[]> Sprites);
    private sealed record ChampionImages(Dictionary<string, ChampionImage>? Data);
    private sealed record ChampionImage(SpriteRegion Image);
    private sealed record SpriteRegion(string Sprite, int X, int Y, int W, int H);
}
