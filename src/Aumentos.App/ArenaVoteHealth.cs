using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Tesseract;

namespace Aumentos;

internal static class ArenaVoteHealth
{
    // Distances from the right edge and the plaque's center, in hundredths of the vote row spacing.
    private static readonly (int Inset, int Offset)[] Arc = [(88, 18), (79, 28), (70, 37), (59, 42)];

    internal static int? Read(BitmapSource frame, int pitch, Detection ocr)
    {
        int width = pitch * 3 / 2;
        int left = frame.PixelWidth - width;
        int top = frame.PixelHeight / 8;
        int height = frame.PixelHeight * 3 / 4;
        int stride = width * 4;
        var image = frame.Format == PixelFormats.Bgr32 || frame.Format == PixelFormats.Bgra32 || frame.Format == PixelFormats.Pbgra32
            ? frame : new FormatConvertedBitmap(frame, PixelFormats.Bgr32, null, 0);
        var pixels = new byte[stride * height];
        image.CopyPixels(new Int32Rect(left, top, width, height), pixels, stride, 0);

        int borderOffset = pitch * 16 / 100;
        int tolerance = Math.Max(1, pitch / 37);
        int? first = null;
        int last = 0;
        for (int y = pitch / 2; y < height - pitch / 2; y++)
        {
            if (!HasBorder(y - borderOffset) || !HasBorder(y + borderOffset) || ArcHits(y, 1) < 3)
                continue;
            if (first is { } prior && y - prior > tolerance * 3)
                return null;
            first ??= y;
            last = y;
        }
        if (!first.HasValue)
            return null;

        int center = (first.Value + last) / 2;
        int inkOffset = pitch * 11 / 100;
        if (!HasDarkInterior(center - inkOffset) || !HasDarkInterior(center + inkOffset))
            return null;

        int scoreLeft = frame.PixelWidth - pitch * 134 / 100;
        int scoreRight = frame.PixelWidth - pitch * 94 / 100;
        int halfHeight = pitch * 13 / 100;
        var area = new Int32Rect(scoreLeft, center + top - halfHeight, scoreRight - scoreLeft, halfHeight * 2 + 1);
        int? health = ReadNumber(area);
        if (health is null)
            return null;

        // Check a second crop and the visible fill direction so a clipped "100" cannot become "10".
        var tighter = new Int32Rect(area.X, area.Y + 1, area.Width, area.Height - 2);
        int upperArc = ArcHits(center, -1);
        if (ReadNumber(tighter) != health || (health < 50 && upperArc >= 3) || (health >= 90 && upperArc < 3))
            return null;
        return health;

        int? ReadNumber(Int32Rect bounds)
        {
            string value = ocr.ReadTitle(image, bounds, PageSegMode.SingleLine, minimumScale: 3);
            return value.Length is > 0 and <= 3 && value.All(char.IsAsciiDigit) && int.TryParse(value, out var number)
                && number is > 0 and <= 100 ? number : null;
        }

        bool HasBorder(int row)
        {
            for (int y = row - tolerance; y <= row + tolerance; y++)
            {
                int samples = 0;
                for (int inset = 109; inset <= 129; inset += 4)
                {
                    int i = y * stride + (width - pitch * inset / 100) * 4;
                    int blue = pixels[i], green = pixels[i + 1], red = pixels[i + 2];
                    if (red is >= 46 and <= 155 && red - green >= 4 && green - blue >= 10)
                        samples++;
                }
                if (samples >= 5)
                    return true;
            }
            return false;
        }

        bool HasDarkInterior(int y)
        {
            int dark = 0;
            for (int inset = 109; inset <= 129; inset += 4)
            {
                int i = y * stride + (width - pitch * inset / 100) * 4;
                int blue = pixels[i], green = pixels[i + 1], red = pixels[i + 2];
                if (red <= 45 && green <= 55 && blue <= 60 && blue >= red)
                    dark++;
            }
            return dark >= 4;
        }

        int ArcHits(int center, int direction)
        {
            int hits = 0;
            foreach (var (inset, offset) in Arc)
            {
                int y = center + direction * pitch * offset / 100;
                int x = width - pitch * inset / 100;
                for (int probe = x - tolerance; probe <= x + tolerance; probe++)
                {
                    int i = y * stride + probe * 4;
                    int blue = pixels[i], green = pixels[i + 1], red = pixels[i + 2];
                    if (red < 192 || green < 132 || blue > 85 || red - green < 17 || green - blue < 55)
                        continue;
                    hits++;
                    break;
                }
            }
            return hits;
        }
    }
}
