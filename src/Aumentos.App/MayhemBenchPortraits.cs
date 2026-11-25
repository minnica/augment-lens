using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Aumentos;

internal sealed record MayhemBenchChoice(int Slot, Champion Champion);

internal sealed class MayhemBenchPortraits(Catalog catalog, string settings, HttpClient http,
    ChampionSelectPortraits? portraits = null)
{
    private const int GridSize = 8;
    private const int SignatureSize = GridSize * GridSize * 3;
    // Ignore the lower-right quadrant where the client can draw a lock over the portrait.
    private static readonly int[] PortraitChannels = Enumerable.Range(0, SignatureSize)
        .Where(channel => channel / (GridSize * 3) < GridSize / 2 || channel / 3 % GridSize < GridSize / 2).ToArray();
    internal const int FirstPortrait = 550;
    internal const int PortraitTop = 17;
    internal const int PortraitSize = 76;
    internal static int PortraitLeft(int slot) => FirstPortrait + (int)Math.Round(slot * 91.7);

    private readonly Dictionary<string, Champion> champions = catalog.Champions.ToDictionary(champion => champion.Key);
    private readonly ChampionSelectPortraits source = portraits ?? new(catalog, settings, http);
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
        var references = templates.Select(pair => (pair.Key, pair.Value, Brightness: Brightness(pair.Value))).ToArray();
        var choices = new List<MayhemBenchChoice>();
        for (int slot = 0; slot < 10; slot++)
        {
            var portrait = MayhemDraftReader.Scaled(PortraitLeft(slot), PortraitTop, PortraitSize, PortraitSize,
                frame.PixelWidth, frame.PixelHeight);
            var sample = Signature(new CroppedBitmap(frame, portrait));
            if (!IsAvailablePortrait(sample))
                continue;

            var brightness = Brightness(sample);
            double best = double.PositiveInfinity, secondBest = double.PositiveInfinity;
            string? key = null;
            foreach (var (candidate, template, reference) in references)
            {
                var exposure = Math.Clamp(brightness / Math.Max(1, reference), 0.7, 1.3);
                double difference = 0;
                foreach (int i in PortraitChannels)
                    difference += Math.Abs(sample[i] - template[i] * exposure);
                var distance = difference / PortraitChannels.Length;
                if (distance < best)
                {
                    secondBest = best;
                    best = distance;
                    key = candidate;
                }
                else
                    secondBest = Math.Min(secondBest, distance);
            }
            var margin = secondBest - best;
            if (key is not null && ((best <= 19 && margin >= 6) || (best <= 24 && margin >= 12)))
                choices.Add(new(slot, champions[key]));
        }
        return choices.GroupBy(choice => choice.Champion.Key).Where(group => group.Count() == 1)
            .Select(group => group.Single()).ToArray();
    }

    private static double Brightness(byte[] pixels) => PortraitChannels.Average(index => (double)pixels[index]);

    // An unavailable portrait is shown in grayscale. Empty slots have little luminance variation.
    private static bool IsAvailablePortrait(byte[] pixels)
    {
        int min = 765, max = 0, color = 0;
        for (int channel = 0; channel < PortraitChannels.Length; channel += 3)
        {
            int i = PortraitChannels[channel];
            int luminance = pixels[i] + pixels[i + 1] + pixels[i + 2];
            min = Math.Min(min, luminance);
            max = Math.Max(max, luminance);
            color += Math.Max(pixels[i], Math.Max(pixels[i + 1], pixels[i + 2])) -
                Math.Min(pixels[i], Math.Min(pixels[i + 1], pixels[i + 2]));
        }
        return max - min >= 90 && color >= PortraitChannels.Length;
    }

    internal async Task<Dictionary<string, byte[]>?> Load(CancellationToken cancellation)
    {
        var cache = Path.Combine(settings, "mayhem-portraits", source.Version + ".json");
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
            var images = await source.ReadAll().WaitAsync(cancellation).ConfigureAwait(false);
            if (images is null)
                return null;
            var templates = images.ToDictionary(pair => pair.Key, pair => Signature(pair.Value));
            StatsCache.Write(cache, templates);
            return templates;
        }
        catch (Exception error) when (error is OperationCanceledException or ArgumentException or NotSupportedException or
            FileFormatException or InvalidOperationException)
        {
            return null;
        }
    }

    private static byte[] Signature(BitmapSource image)
    {
        var rgb = new FormatConvertedBitmap(image, PixelFormats.Rgb24, null, 0);
        int width = image.PixelWidth, height = image.PixelHeight;
        int stride = width * 3;
        var source = new byte[stride * height];
        rgb.CopyPixels(source, stride, 0);
        var result = new byte[SignatureSize];
        // Keep the central portion of the portrait to discard the border and lock icon.
        for (int y = 0; y < GridSize; y++)
        for (int x = 0; x < GridSize; x++)
        {
            int left = Cell(width, x), right = Cell(width, x + 1);
            int top = Cell(height, y), bottom = Cell(height, y + 1);
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
}
