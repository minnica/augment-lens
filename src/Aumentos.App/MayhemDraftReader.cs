using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Tesseract;

namespace Aumentos;

internal sealed record MayhemDraftChoice(int Center, Champion Champion, int Bottom = 752);

internal sealed class MayhemDraftReader(Catalog catalog)
{
    internal const int ReferenceWidth = 2000;
    internal const int ReferenceHeight = 1125;
    private static readonly int[] ThreeCards = [662, 1000, 1338];
    private static readonly int[] TwoCards = [831, 1169];

    private readonly Dictionary<string, Champion> names = catalog.Champions
        .SelectMany(champion => new[] { NameKey(champion.Key), NameKey(champion.Name) }.Distinct()
            .Select(name => (Name: name, Champion: champion)))
        .GroupBy(entry => entry.Name)
        .Where(group => group.Count() == 1)
        .ToDictionary(group => group.Key, group => group.Single().Champion);

    // Null means the offer is closed. An empty array means it is visible but still unreadable.
    internal MayhemDraftChoice[]? Read(BitmapSource image, Detection ocr)
    {
        if (image.PixelWidth < 800 || image.PixelHeight < 450 ||
            Math.Abs((double)image.PixelWidth / image.PixelHeight - (double)ReferenceWidth / ReferenceHeight) > 0.08)
            return null;

        var heading = Scaled(630, 164, 740, 62, image.PixelWidth, image.PixelHeight);
        if (!IsOffer(ocr.ReadTitle(image, heading, PageSegMode.SingleLine)) &&
            !IsOffer(ocr.ReadTitle(image, heading, PageSegMode.RawLine, minimumScale: 2)))
            return null;

        var pair = TwoCards.Select(center => (Center: center, Top: CardTop(image, center))).ToArray();
        var frames = pair.All(card => card.Top > 0) ? pair :
            ThreeCards.Select(center => (Center: center, Top: CardTop(image, center))).ToArray();
        var choices = new List<MayhemDraftChoice>(frames.Length);
        foreach (var card in frames)
        {
            bool expanded = card.Top is > 0 and < 271;
            var champion = ReadName(card.Center, expanded ? 667 : 655, 34, PageSegMode.SingleLine) ??
                ReadName(card.Center, expanded ? 655 : 667, 34, PageSegMode.SingleLine) ??
                ReadName(card.Center, expanded ? 661 : 649, 47, PageSegMode.RawLine);
            if (champion is not null)
                choices.Add(new(card.Center, champion, expanded ? 780 : 752));
        }
        return choices.GroupBy(choice => choice.Champion.Key).Where(group => group.Count() == 1)
            .Select(group => group.Single()).ToArray();

        Champion? ReadName(int center, int y, int height, PageSegMode mode)
        {
            var area = Scaled(center - 132, y, 264, height, image.PixelWidth, image.PixelHeight);
            return names.GetValueOrDefault(NameKey(ocr.ReadTitle(image, area, mode,
                minimumScale: mode == PageSegMode.RawLine ? 3 : 1)));
        }
    }

    private static int CardTop(BitmapSource image, int center)
    {
        var area = Scaled(center - 58, 233, 116, 66, image.PixelWidth, image.PixelHeight);
        var gray = new FormatConvertedBitmap(new CroppedBitmap(image, area), PixelFormats.Gray8, null, 0);
        var pixels = new byte[area.Width * area.Height];
        gray.CopyPixels(pixels, area.Width, 0);
        int step = Math.Max(1, (int)Math.Round(image.PixelHeight * 3.0 / ReferenceHeight));
        int bestContrast = 35, top = 0;
        for (int y = step; y < area.Height - step; y++)
        {
            // A card frame has the same horizontal edge across all three sections of this strip.
            int contrast = int.MaxValue;
            for (int section = 0; section < 3; section++)
            {
                int left = section * area.Width / 3, right = (section + 1) * area.Width / 3;
                int difference = 0;
                for (int x = left; x < right; x++)
                    difference += pixels[(y + step) * area.Width + x] - pixels[(y - step) * area.Width + x];
                contrast = Math.Min(contrast, difference / (right - left));
            }
            if (contrast > bestContrast)
            {
                bestContrast = contrast;
                top = (int)Math.Round((double)(y + area.Y) * ReferenceHeight / image.PixelHeight);
            }
        }
        return top;
    }

    internal static Int32Rect Scaled(int x, int y, int width, int height, int frameWidth, int frameHeight) =>
        new Region((double)x / ReferenceWidth, (double)y / ReferenceHeight,
            (double)width / ReferenceWidth, (double)height / ReferenceHeight).Pixels(frameWidth, frameHeight);

    private static bool IsOffer(string text) => NameKey(text) is "seleccionaauncampeon" or "selectachampion";
    private static string NameKey(string text) => string.Concat(Ratings.Normalize(text).Where(char.IsAsciiLetterOrDigit));
}
