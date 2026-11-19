using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace Aumentos;

internal sealed class BuildItemIcons(HttpClient http)
{
    private readonly ConcurrentDictionary<(string Patch, int Id),
        Lazy<Task<BitmapSource?>>> icons = new();

    internal async Task<BitmapSource?> Read(string patch, int id)
    {
        var entry = icons.GetOrAdd((patch, id), key => new(() => Task.Run(() => Download(key.Patch, key.Id))));
        var image = await entry.Value.ConfigureAwait(false);
        if (image is null)
            icons.TryRemove(new KeyValuePair<(string, int), Lazy<Task<BitmapSource?>>>((patch, id), entry));
        return image;
    }

    private async Task<BitmapSource?> Download(string patch, int id)
    {
        try
        {
            var address = new Uri($"https://opgg-static.akamaized.net/meta/images/lol/{patch}.1/item/{id}.png?w=64");
            using var response = await http.GetAsync(address).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentType?.MediaType != "image/png")
                return null;
            var bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            if (bytes.Length > 64_000)
                return null;
            using var stream = new MemoryStream(bytes);
            var frame = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
            frame.Freeze();
            return frame;
        }
        catch (Exception error) when (error is HttpRequestException or IOException or FormatException or
            NotSupportedException or InvalidOperationException or OperationCanceledException)
        {
            return null;
        }
    }
}
