using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Tesseract;

namespace Aumentos;

internal sealed class ShopDetection
{
    private byte[] pixels = [];

    internal Int32Rect? Read(BitmapSource frame, Detection detector, Int32Rect? previous)
    {
        // A long gold edge over the blue shop background locates the tabs even after the panel moves.
        var image = UseBgr(frame);
        int width = image.PixelWidth;
        int height = image.PixelHeight;
        if (width < 320 || height < 240)
            return null;
        int step = Math.Max(2, width / 720);
        int below = Math.Max(3, height / 480);

        if (previous is { } shop && ProbeRegion(shop, width, height) is { } edge && MatchesProbe(image, edge))
            return shop;

        int rows = height * 2 / 3;
        Load(image, new Int32Rect(0, 0, width, rows));
        int minWidth = Math.Max(215, width / 5);
        int maxWidth = width - 2 * step;
        var tested = new List<Int32Rect>(4);
        foreach (var border in Borders(width, rows, step, below, minWidth, maxWidth))
        {
            if (tested.Exists(rect => Math.Abs(rect.Y - border.Y) <= 2 * below &&
                Math.Abs(rect.X - border.X) < minWidth / 8 && Math.Abs(rect.Width - border.Width) < minWidth / 8))
                continue;
            tested.Add(border);
            var tabs = new Int32Rect(border.X + border.Width / 50, border.Y + border.Width / 100,
                border.Width * 63 / 100, Math.Max(15, border.Width * 3 / 100));
            if (tabs.Y + tabs.Height <= height &&
                IsShopHeader(detector.ReadTitle(image, tabs, PageSegMode.SingleLine)))
                return new Int32Rect(border.X, border.Y, border.Width, Math.Min(height - border.Y, border.Width * 3 / 4));
            if (tested.Count >= 4)
                break;
        }
        return null;
    }

    internal static Int32Rect? ProbeRegion(Int32Rect shop, int width, int height)
    {
        int below = Math.Max(3, height / 480);
        return shop.X >= 0 && shop.Width >= 12 && shop.X + shop.Width <= width && shop.Y >= 0 && shop.Y + below < height
            ? new Int32Rect(shop.X, shop.Y, shop.Width, below + 1) : null;
    }

    internal bool MatchesProbe(BitmapSource frame, Int32Rect edge)
    {
        Load(UseBgr(frame), edge);
        int below = (edge.Height - 1) * edge.Width * 4;
        int matches = 0;
        const int samples = 12;
        for (int i = 1; i <= samples; i++)
        {
            int x = edge.Width * i / (samples + 1);
            if (IsEdge(x * 4, below))
                matches++;
        }
        return matches >= 10;
    }

    internal static bool IsShopHeader(string text)
    {
        var normalized = Ratings.Normalize(text).Replace(" ", "", StringComparison.Ordinal);
        return HasTwoTabs(normalized, "recomendados", "todoslosobjetos", "conjuntosdeobjetos") ||
            HasTwoTabs(normalized, "recommended", "allitems", "itemsets");
    }

    private static bool HasTwoTabs(string text, string recommended, string allItems, string itemSets)
    {
        bool all = text.Contains(allItems, StringComparison.Ordinal);
        bool sets = text.Contains(itemSets, StringComparison.Ordinal);
        return (all && sets) || ((all || sets) && text.Contains(recommended, StringComparison.Ordinal));
    }

    private IEnumerable<Int32Rect> Borders(int width, int rows, int step, int below, int minWidth, int maxWidth)
    {
        int stride = width * 4;
        for (int y = 0; y < rows - below; y++)
        {
            int start = -1, end = -1;
            for (int x = 0; x < width; x += step)
            {
                if (IsEdge(y * stride + 4 * x, below * stride))
                {
                    if (start < 0)
                        start = x;
                    end = x;
                    continue;
                }
                if (start < 0 || x - end <= 5 * step)
                    continue;
                if (end - start >= minWidth && end - start <= maxWidth)
                    yield return new Int32Rect(start, y, end - start, 1);
                start = -1;
            }
            if (start >= 0 && end - start >= minWidth && end - start <= maxWidth)
                yield return new Int32Rect(start, y, end - start, 1);
        }
    }

    private bool IsEdge(int i, int below)
    {
        int blue = pixels[i], green = pixels[i + 1], red = pixels[i + 2];
        return red is >= 64 and <= 205 && red >= green + 7 && green >= blue + 11 &&
            pixels[i + below] >= pixels[i + below + 2] + 8 &&
            pixels[i + below + 1] >= pixels[i + below + 2] + 5;
    }

    private static BitmapSource UseBgr(BitmapSource frame) => frame.Format == PixelFormats.Bgr32 ||
        frame.Format == PixelFormats.Bgra32 || frame.Format == PixelFormats.Pbgra32
        ? frame : new FormatConvertedBitmap(frame, PixelFormats.Bgr32, null, 0);

    private void Load(BitmapSource image, Int32Rect region)
    {
        int stride = region.Width * 4;
        int size = stride * region.Height;
        if (pixels.Length < size)
            pixels = new byte[size];
        image.CopyPixels(region, pixels, stride, 0);
    }
}
