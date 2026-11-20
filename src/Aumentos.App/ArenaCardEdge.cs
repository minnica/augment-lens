using System;
using System.Buffers;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Aumentos;

internal static class ArenaCardEdge
{
    private const int SamplesPerShoulder = 12;
    private const int MinimumBrightSamples = 9;
    private const int MinimumBrightness = 160;

    internal static int? Read(BitmapSource image, Calibration layout)
    {
        int width = image.PixelWidth, height = image.PixelHeight;
        if (!layout.Fits(width, height) || layout.Titles.Length < 2)
            return null;

        var titles = layout.Titles.Select(title => title.Pixels(width, height)).ToArray();
        int left = titles.Min(title => title.X);
        int right = titles.Max(title => title.X + title.Width);
        // This band covers the top edge on regular, mixed, and dedicated upgrade offers.
        int top = (int)Math.Floor(height * 0.14);
        int bottom = (int)Math.Ceiling(height * 0.185);
        if (left < 0 || right > width || titles.Any(title => title.Width < 48))
            return null;

        var band = new Int32Rect(left, top, right - left, bottom - top);
        int stride = band.Width * 4;
        var pixels = ArrayPool<byte>.Shared.Rent(stride * band.Height);
        try
        {
            if (image.Format == PixelFormats.Bgr32 || image.Format == PixelFormats.Bgra32 || image.Format == PixelFormats.Pbgra32)
                image.CopyPixels(band, pixels, stride, 0);
            else
                new FormatConvertedBitmap(new CroppedBitmap(image, band), PixelFormats.Bgr32, null, 0)
                    .CopyPixels(pixels, stride, 0);

            // Sample the flat shoulders, away from the star notch and rounded outer corners.
            var shoulders = titles.Select(title => new[] { Sample(title, -1), Sample(title, 1) }).ToArray();
            int quorum = titles.Length / 2 + 1;
            int persistence = Math.Max(2, (int)Math.Ceiling(height * 0.002));
            int streak = 0;
            bool seenBackground = false;
            for (int row = 0; row < band.Height; row++)
            {
                int rowOffset = row * stride;
                int frames = 0;
                foreach (var pair in shoulders)
                {
                    if (IsBright(pair[0], rowOffset) || IsBright(pair[1], rowOffset))
                        frames++;
                }
                if (frames < quorum)
                {
                    streak = 0;
                    seenBackground = true;
                }
                else if (seenBackground && ++streak == persistence)
                    return top + row - persistence + 1;
            }
            return null;

            int[] Sample(Int32Rect title, int side)
            {
                double center = title.X - left + title.Width * 0.5;
                return Enumerable.Range(0, SamplesPerShoulder).Select(sample =>
                    (int)Math.Round(center + side * title.Width * (0.26 + sample * 0.02)) * 4).ToArray();
            }

            bool IsBright(int[] samples, int rowOffset)
            {
                int bright = 0;
                foreach (int sample in samples)
                {
                    int offset = rowOffset + sample;
                    int luma = 7 * pixels[offset] + 72 * pixels[offset + 1] + 21 * pixels[offset + 2];
                    if (luma >= MinimumBrightness * 100 && ++bright == MinimumBrightSamples)
                        return true;
                }
                return false;
            }
        }
        finally { ArrayPool<byte>.Shared.Return(pixels); }
    }
}
