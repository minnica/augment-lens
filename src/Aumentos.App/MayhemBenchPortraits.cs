using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Aumentos;

internal sealed record MayhemBenchChoice(int Slot, Champion Champion);

internal sealed class MayhemBenchPortraits(Catalog catalog, string settings, HttpClient http)
{
    private const int GridSize = 8;
    private const int SignatureSize = GridSize * GridSize * 3;
    internal const int FirstPortrait = 550;
    internal const int PortraitSize = 76;
    internal static int PortraitLeft(int slot) => FirstPortrait + (int)Math.Round(slot * 91.7);

    private readonly Dictionary<string, Champion> champions = catalog.Champions.ToDictionary(champion => champion.Key);
    private Task<Dictionary<string, byte[]>?>? load;
    private long retryAt;

    internal void Prefetch(CancellationToken cancellation)
    {
        if (load is null || (load.IsCompletedSuccessfully && load.Result is null && Environment.TickCount64 >= retryAt))
        {
            retryAt = Environment.TickCount64 + 60000;
            load = Task.Run(() => Load(cancellation));
        }
    }

    internal MayhemBenchChoice[] Read(BitmapSource frame) =>
        load is { IsCompletedSuccessfully: true, Result: { } templates } ? Match(frame, templates) : [];

    internal MayhemBenchChoice[] Match(BitmapSource frame, IReadOnlyDictionary<string, byte[]> templates)
    {
        if (frame.PixelWidth < 800 || frame.PixelHeight < 450 ||
            Math.Abs((double)frame.PixelWidth / frame.PixelHeight - 16.0 / 9) > 0.08)
            return [];
        var choices = new List<MayhemBenchChoice>();
        for (int slot = 0; slot < 10; slot++)
        {
            var portrait = MayhemDraftReader.Scaled(PortraitLeft(slot), 17, PortraitSize, PortraitSize,
                frame.PixelWidth, frame.PixelHeight);
            var sample = Signature(frame, portrait);
            if (!IsAvailablePortrait(sample))
                continue;

            var brightness = sample.Average(value => (double)value);
            double best = double.PositiveInfinity, secondBest = double.PositiveInfinity;
            string? key = null;
            foreach (var (candidate, template) in templates)
            {
                var reference = template.Average(value => (double)value);
                var exposure = Math.Clamp(brightness / Math.Max(1, reference), 0.7, 1.3);
                double difference = 0;
                for (int i = 0; i < SignatureSize; i++)
                    difference += Math.Abs(sample[i] - template[i] * exposure);
                var distance = difference / SignatureSize;
                if (distance < best)
                {
                    secondBest = best;
                    best = distance;
                    key = candidate;
                }
                else
                    secondBest = Math.Min(secondBest, distance);
            }
            if (key is not null && best <= 19 && secondBest - best >= 6)
                choices.Add(new(slot, champions[key]));
        }
        return choices.GroupBy(choice => choice.Champion.Key).Where(group => group.Count() == 1)
            .Select(group => group.Single()).ToArray();
    }

    // An unavailable portrait is shown in grayscale. Empty slots have little luminance variation.
    private static bool IsAvailablePortrait(byte[] pixels)
    {
        int min = 765, max = 0, color = 0;
        for (int i = 0; i < pixels.Length; i += 3)
        {
            int luminance = pixels[i] + pixels[i + 1] + pixels[i + 2];
            min = Math.Min(min, luminance);
            max = Math.Max(max, luminance);
            color += Math.Max(pixels[i], Math.Max(pixels[i + 1], pixels[i + 2])) -
                Math.Min(pixels[i], Math.Min(pixels[i + 1], pixels[i + 2]));
        }
        return max - min >= 90 && color >= 3 * GridSize * GridSize;
    }

    internal async Task<Dictionary<string, byte[]>?> Load(CancellationToken cancellation)
    {
        var configured = catalog.Sources?.GetValueOrDefault("champions");
        const string prefix = "Data Dragon ";
        var version = configured?.StartsWith(prefix, StringComparison.Ordinal) == true &&
            Version.TryParse(configured[prefix.Length..], out var release) &&
            release.ToString(2) == catalog.Patch ? release.ToString() : catalog.Patch + ".1";
        var cache = Path.Combine(settings, "mayhem-portraits", version + ".json");
        try
        {
            var existing = Ratings.Read<Dictionary<string, byte[]>>(cache);
            if (existing.Count >= champions.Count / 2 && existing.All(pair =>
                champions.ContainsKey(pair.Key) && pair.Value?.Length == SignatureSize))
                return existing;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException) { }

        try
        {
            var root = $"https://ddragon.leagueoflegends.com/cdn/{version}";
            var metadata = await http.GetFromJsonAsync<ChampionImages>(root + "/data/en_US/champion.json",
                Ratings.JsonOptions, cancellation).ConfigureAwait(false);
            var images = metadata?.Data?.Where(pair => champions.ContainsKey(pair.Key) &&
                pair.Value?.Image is { W: 48, H: 48, X: >= 0, Y: >= 0, Sprite: { } file } &&
                file.StartsWith("champion", StringComparison.Ordinal) && file.EndsWith(".png", StringComparison.Ordinal) &&
                int.TryParse(file.AsSpan(8, file.Length - 12), NumberStyles.None, CultureInfo.InvariantCulture, out _))
                .GroupBy(pair => pair.Value.Image.Sprite).ToArray() ?? [];
            var sprites = await Task.WhenAll(images.Select(async group =>
            {
                var bytes = await http.GetByteArrayAsync(root + "/img/sprite/" + group.Key, cancellation).ConfigureAwait(false);
                return (Regions: group, Bytes: bytes);
            })).ConfigureAwait(false);
            var templates = new Dictionary<string, byte[]>();
            foreach (var (regions, bytes) in sprites)
            {
                using var stream = new MemoryStream(bytes, writable: false);
                var image = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                foreach (var entry in regions)
                {
                    var box = entry.Value.Image;
                    if (box.X <= image.PixelWidth - box.W && box.Y <= image.PixelHeight - box.H)
                        templates.Add(entry.Key, Signature(image, new(box.X, box.Y, box.W, box.H)));
                }
            }
            if (templates.Count < champions.Count / 2)
                return null;
            StatsCache.Write(cache, templates);
            return templates;
        }
        catch (Exception error) when (error is HttpRequestException or IOException or JsonException or OperationCanceledException or
            ArgumentException or NotSupportedException or FileFormatException or InvalidOperationException)
        {
            return null;
        }
    }

    private static byte[] Signature(BitmapSource image, Int32Rect area)
    {
        var cropped = new CroppedBitmap(image, area);
        var rgb = new FormatConvertedBitmap(cropped, PixelFormats.Rgb24, null, 0);
        int stride = area.Width * 3;
        var source = new byte[stride * area.Height];
        rgb.CopyPixels(source, stride, 0);
        var result = new byte[SignatureSize];
        // Keep the central portion of the portrait to discard the border and lock icon.
        for (int y = 0; y < GridSize; y++)
        for (int x = 0; x < GridSize; x++)
        {
            int left = Cell(area.Width, x), right = Cell(area.Width, x + 1);
            int top = Cell(area.Height, y), bottom = Cell(area.Height, y + 1);
            for (int channel = 0; channel < 3; channel++)
            {
                int sum = 0;
                for (int row = top; row < bottom; row++)
                for (int col = left; col < right; col++)
                    sum += source[row * stride + col * 3 + channel];
                result[(y * GridSize + x) * 3 + channel] = (byte)(sum / ((right - left) * (bottom - top)));
            }
        }
        return result;

        static int Cell(int extent, int index) => (int)(extent * (0.18 + index * 0.08));
    }

    private sealed record ChampionImages(Dictionary<string, ChampionImage>? Data);
    private sealed record ChampionImage(SpriteRegion Image);
    private sealed record SpriteRegion(string Sprite, int X, int Y, int W, int H);
}
