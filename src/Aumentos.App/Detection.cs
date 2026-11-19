using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Tesseract;

namespace Aumentos;

internal record Region(double X, double Y, double Width, double Height)
{
    public Int32Rect Pixels(int width, int height)
    {
        int x = (int)Math.Round(X * width), y = (int)Math.Round(Y * height);
        return new Int32Rect(x, y, (int)Math.Round((X + Width) * width) - x,
            (int)Math.Round((Y + Height) * height) - y);
    }
}

internal record Calibration(int Width, int Height, Region Marker, Region[] Titles, byte[] Signature)
{
    public bool Fits(int width, int height) => Width == width && Height == height;
    public Int32Rect ScanArea()
    {
        var regions = Titles.Append(Marker).Select(region => region.Pixels(Width, Height)).ToArray();
        var x = regions.Min(rect => rect.X);
        var y = Math.Max(0, Math.Min(Marker.Pixels(Width, Height).Y,
            Titles.Select(title => title.Pixels(Width, Height)).Min(rect => rect.Y - rect.Height * 3 / 4)));
        return new Int32Rect(x, y, regions.Max(rect => rect.X + rect.Width) - x,
            regions.Max(rect => rect.Y + rect.Height) - y);
    }
}

public sealed class OfferState
{
    private readonly int?[] ids = new int?[4];
    internal readonly byte[]?[] Samples = new byte[4][];
    internal readonly byte[]?[] Descriptions = new byte[4][];
    internal readonly string[] Texts = new string[4];
    internal readonly bool[] ItemSlots = new bool[4];

    public int? this[int slot] => ids[slot];
    internal void Set(int slot, int? id, byte[]? sample, string text = "", bool item = false, byte[]? description = null)
    {
        ids[slot] = id;
        Samples[slot] = sample;
        Descriptions[slot] = description;
        Texts[slot] = text;
        ItemSlots[slot] = item;
    }

    public void Clear()
    {
        Array.Clear(ids);
        Array.Clear(Samples);
        Array.Clear(Descriptions);
        Array.Fill(Texts, "");
        Array.Clear(ItemSlots);
    }
}

internal sealed class Detection : IDisposable
{
    private readonly Ratings ratings;
    private readonly TesseractEngine engine;
    private readonly OfferState state = new();

    internal Detection(Ratings ratings, string tessdata)
    {
        this.ratings = ratings;
        engine = new TesseractEngine(tessdata, "spa", EngineMode.LstmOnly);
    }

    internal void Clear() => state.Clear();
    internal bool PanelVisible(BitmapSource image, Int32Rect scan, Calibration calibration)
    {
        var marker = Relative(calibration.Marker, scan, calibration);
        return BlueButtonVisible(image, marker) || Difference(calibration.Signature, Signature(image, marker)) < 14;
    }

    internal (Calibration Layout, DetectionResult Result)? ReadPanel(BitmapSource image, Int32Rect scan,
        Calibration[] layouts, Mode mode, Calibration? previous)
    {
        (Calibration Layout, DetectionResult Result)? best = null;
        foreach (var layout in layouts.OrderByDescending(item => item == previous))
        {
            bool marker = PanelVisible(image, scan, layout);
            if (!marker && layout.Titles.Where(title => LooksLikeTitle(image, Relative(title, scan, layout)))
                    .Take(2).Count() < 2)
                continue;
            if (layout != previous)
                state.Clear();
            var result = Scan(image, scan, layout, mode, panelConfirmed: true);
            if (!marker && result.Matched < 2)
                continue;
            if (layout == previous && result.CanShow(true))
                return (layout, result);
            if (best is null || result.Matched > best.Value.Result.Matched)
                best = (layout, result);
            if (result.Matched == layout.Titles.Length)
                return (layout, result);
        }
        state.Clear();
        return best;
    }

    private static bool LooksLikeTitle(BitmapSource image, Int32Rect rect)
    {
        var gray = GrayCrop(image, rect);
        var pixels = new byte[rect.Width * rect.Height];
        gray.CopyPixels(pixels, rect.Width, 0);
        int samples = 0, dark = 0, light = 0;
        for (int y = 0; y < rect.Height; y += 2)
        {
            for (int x = rect.Width / 16; x < rect.Width - rect.Width / 16; x += 3)
            {
                byte value = pixels[y * rect.Width + x];
                samples++;
                if (value < 52) dark++;
                if (value > 125) light++;
            }
        }
        return dark * 4 >= samples * 3 && light * 100 >= samples && light * 4 <= samples;
    }

    internal DetectionResult Scan(BitmapSource image, Int32Rect scan, Calibration calibration, Mode mode, bool panelConfirmed = false)
    {
        if (!panelConfirmed && !PanelVisible(image, scan, calibration))
        {
            state.Clear();
            return new DetectionResult(false, new int?[calibration.Titles.Length], [], []);
        }

        var texts = new string[calibration.Titles.Length];
        var ids = new int?[calibration.Titles.Length];
        var items = new bool[calibration.Titles.Length];
        for (int index = 0; index < ids.Length; index++)
        {
            var rect = Relative(calibration.Titles[index], scan, calibration);
            var current = Signature(image, rect);
            var descriptions = DescriptionArea(image, rect);
            var previousDescription = state.Descriptions[index];
            var description = previousDescription is not null && descriptions is { } area ? Signature(image, area) : null;
            if (state.Samples[index] is not { } previous || Difference(previous, current) >= 2.2 ||
                (description is not null && Difference(previousDescription!, description) >= 0.5))
            {
                state.Set(index, null, null);
                var text = ReadTitle(image, rect, PageSegMode.SingleLine);
                var item = mode == Mode.Arena && ratings.IsArenaItem(text);
                var candidates = item ? [] : ratings.FindTitle(mode, text);
                if (candidates.Length != 1 && !item)
                {
                    var extra = Math.Min(rect.Y, rect.Height * 3 / 4);
                    var trim = rect.Width / 24;
                    var widerTitle = new Int32Rect(rect.X + trim, rect.Y - extra,
                        rect.Width - trim * 2, rect.Height + extra - rect.Height / 6);
                    var fallback = ReadTitle(image, widerTitle, PageSegMode.SingleBlock);
                    item = mode == Mode.Arena && ratings.IsArenaItem(fallback);
                    var alternate = item ? [] : ratings.FindTitle(mode, fallback);
                    if (item || alternate.Length > 0 || candidates.Length == 0)
                    {
                        if (fallback.Length != 0)
                            text = fallback;
                        candidates = alternate;
                    }
                }
                var card = candidates is [var unique] ? unique : null;
                byte[]? detail = null;
                if (mode == Mode.Caos && candidates.Length > 1 && descriptions is { } crop &&
                    candidates.All(candidate => candidate.AnvilCount.HasValue) &&
                    candidates.Select(candidate => candidate.AnvilCount).Distinct().Count() == candidates.Length)
                {
                    detail = description ?? Signature(image, crop);
                    card = ChooseByAnvils(candidates, ReadTitle(image, crop, PageSegMode.SingleBlock, minimumScale: 2));
                }
                state.Set(index, card?.Id, current, text, item, detail);
            }
            ids[index] = state[index];
            texts[index] = state.Texts[index];
            items[index] = state.ItemSlots[index];
        }
        return new DetectionResult(true, ids, texts, items);
    }

    private static Int32Rect? DescriptionArea(BitmapSource image, Int32Rect title)
    {
        int top = title.Y + title.Height * 8 / 5;
        int bottom = Math.Min(image.PixelHeight, title.Y + title.Height * 4);
        return top < bottom ? new Int32Rect(title.X, top, title.Width, bottom - top) : null;
    }

    internal static Augment? ChooseByAnvils(Augment[] candidates, string description)
    {
        var count = Regex.Matches(Ratings.Normalize(description), @"\b(\d+) yunques de\b");
        if (count.Count != 1 || !int.TryParse(count[0].Groups[1].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out int value))
            return null;
        return candidates.Where(candidate => candidate.AnvilCount == value).Take(2).ToArray() is [var match] ? match : null;
    }

    private static bool BlueButtonVisible(BitmapSource image, Int32Rect marker)
    {
        var crop = new CroppedBitmap(image, marker);
        var color = new FormatConvertedBitmap(crop, PixelFormats.Bgr24, null, 0);
        int stride = marker.Width * 3;
        var pixels = new byte[stride * marker.Height];
        color.CopyPixels(pixels, stride, 0);
        int samples = 0;
        int blue = 0;
        for (int y = marker.Height / 5; y < marker.Height * 4 / 5; y += 4)
        {
            for (int x = marker.Width / 8; x < marker.Width * 7 / 8; x += 5)
            {
                int offset = y * stride + x * 3;
                int b = pixels[offset], g = pixels[offset + 1], r = pixels[offset + 2];
                samples++;
                if (b >= 60 && g >= 45 && b > g + 12 && g > r + 25 && r <= 75)
                    blue++;
            }
        }
        return samples > 0 && blue >= samples * 0.65;
    }

    internal string ReadTitle(BitmapSource image, Int32Rect rect, PageSegMode segmentation, int minimumScale = 1)
    {
        var gray = GrayCrop(image, rect);
        int scale = Math.Clamp(64 / gray.PixelHeight, minimumScale, 4);
        var scaled = new TransformedBitmap(gray, new ScaleTransform(scale, scale));
        var width = scaled.PixelWidth;
        var height = scaled.PixelHeight;
        var original = new byte[width * height];
        scaled.CopyPixels(original, width, 0);
        var histogram = new int[256];
        foreach (var value in original)
            histogram[value]++;
        int dark = Percentile(histogram, original.Length / 2);
        int bright = Percentile(histogram, (int)(original.Length * 0.98));
        int span = Math.Max(32, bright - dark);

        const int padding = 10;
        int stride = width + padding * 2;
        var pixels = new byte[stride * (height + padding * 2)];
        Array.Fill(pixels, (byte)255);
        for (int line = 0; line < height; line++)
        {
            for (int column = 0; column < width; column++)
            {
                int value = original[line * width + column] - dark;
                pixels[(line + padding) * stride + column + padding] =
                    (byte)(255 - Math.Clamp(value * 255 / span, 0, 255));
            }
        }

        var processed = BitmapSource.Create(stride, height + padding * 2, 96, 96, PixelFormats.Gray8, null, pixels, stride);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(processed));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        using var pix = Pix.LoadFromMemory(stream.ToArray());
        using var page = engine.Process(pix, segmentation);
        return (page.GetText() ?? "").Trim().ReplaceLineEndings(" ");
    }

    private static int Percentile(int[] histogram, int target)
    {
        var found = 0;
        for (int value = 0; value < histogram.Length; value++)
        {
            found += histogram[value];
            if (found > target)
                return value;
        }
        return 255;
    }

    internal static byte[] Signature(BitmapSource image, Int32Rect rect)
    {
        const int columns = 40, rows = 18;
        var gray = GrayCrop(image, rect);
        var pixels = new byte[rect.Width * rect.Height];
        gray.CopyPixels(pixels, rect.Width, 0);
        var samples = new byte[columns * rows];
        for (int row = 0; row < rows; row++)
        {
            int top = row * rect.Height / rows, bottom = Math.Max(top + 1, (row + 1) * rect.Height / rows);
            for (int column = 0; column < columns; column++)
            {
                int left = column * rect.Width / columns, right = Math.Max(left + 1, (column + 1) * rect.Width / columns);
                int sum = 0;
                for (int y = top; y < bottom; y++)
                    for (int x = left; x < right; x++)
                        sum += pixels[y * rect.Width + x];
                samples[row * columns + column] = (byte)(sum / ((bottom - top) * (right - left)));
            }
        }
        return samples;
    }

    private static double Difference(byte[] left, byte[] right)
    {
        if (left.Length != right.Length)
            return double.PositiveInfinity;
        int sum = 0;
        for (int i = 0; i < left.Length; i++)
            sum += Math.Abs(left[i] - right[i]);
        return sum / (double)left.Length;
    }

    private static FormatConvertedBitmap GrayCrop(BitmapSource image, Int32Rect rect) =>
        new(new CroppedBitmap(image, rect), PixelFormats.Gray8, null, 0);

    private static Int32Rect Relative(Region region, Int32Rect scan, Calibration calibration)
    {
        var rect = region.Pixels(calibration.Width, calibration.Height);
        return new Int32Rect(rect.X - scan.X, rect.Y - scan.Y, rect.Width, rect.Height);
    }

    public void Dispose() => engine.Dispose();
}

internal record DetectionResult(bool PanelVisible, int?[] Cards, string[] Texts, bool[] Items)
{
    internal int Matched => Cards.Count(id => id.HasValue) + Items.Count(item => item);
    internal bool CanShow(bool continuing) => Cards.Count(id => id.HasValue) >= (continuing ? 1 : 2);
}
