using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Tesseract;

namespace Aumentos;

internal sealed record ArenaVoteOption(string Guest, string Title, Int32Rect Bounds);
internal sealed record ArenaVoteOffer(int? Round, ArenaVoteOption[] Options, int? TeamHealth = null);

internal sealed class ArenaVoteReader(Detection ocr)
{
    // The es-MX client string table labels these as cherry_goh_name_* and cherry_goh_title_*.
    // https://raw.communitydragon.org/16.23/game/es_mx/data/menu/en_us/lol.stringtable.json
    private static readonly Guest[] Guests =
    [
        new("Alistar", "Alistar", "Inquebrantable"),
        new("Ambessa", "Ambessa", "Sacrificio Noxiano"),
        new("Annie", "Annie", "Muro de Fuego"),
        new("Atakhan", "Atakhan", "Matanza Absoluta"),
        new("Briar", "Briar", "Frenesí Sangriento"),
        new("Cassiopeia", "Cassiopeia", "Gracia Serpentina"),
        new("Darius", "Darius", "Poder Noxiano"),
        new("Draven", "Draven", "Espectáculo Arriesgado"),
        new("Elise", "Elise", "Festín o Hambruna"),
        new("Evelynn", "Evelynn", "Solo Algo Casual"),
        new("Fiddlesticks", "Fiddlesticks", "Espantapájaros Inocente"),
        new("Garen", "Garen", "Perseverancia"),
        new("Hwei", "Hwei", "Lienzo en Blanco"),
        new("Ivern", "Ivern", "Uno con el Bosque"),
        new("JarvanIV", "Jarvan IV", "Pesada es la Corona"),
        new("Katarina", "Katarina", "Baile de la Voracidad"),
        new("Kayle", "Kayle", "Ascenso Divino"),
        new("Kindred", "Kindred", "Muerte y Renacimiento"),
        new("Kled", "Kled", "¡¡¡POR NOXUS!!!"),
        new("Leblanc", "LeBlanc", "Astucia Noxiana"),
        new("Locke", "Locke", "Alma Desatada"),
        new("Lux", "Lux", "Luz de Demacia"),
        new("Mel", "Mel", "Influencia Hextech"),
        new("Mordekaiser", "Mordekaiser", "Muere en Soledad"),
        new("Morgana", "Morgana", "Sombra Maldita"),
        new("Nilah", "Nilah", "Deleite Dichoso"),
        new("Nocturne", "Nocturne", "Oscuridad"),
        new("Ornn", "Ornn", "El Dios de la Forja"),
        new("Poppy", "Poppy", "Portador Legítimo"),
        new("Rell", "Rell", "Reforjar"),
        new("Riven", "Riven", "Rebelión Noxiana"),
        new("SahnUzal", "Sahn Uzal", "Señor de la Muerte"),
        new("Samira", "Samira", "Carrera Salvaje"),
        new("Senna", "Senna", "Caminata entre Reinos"),
        new("Shaco", "Shaco", "Caos Absoluto"),
        new("Sion", "Sion", "¡¡¡COBARDES!!!"),
        new("Swain", "Swain", "Ascenso Demoniaco"),
        new("TahmKench", "Tahm Kench", "Negociación Oscura"),
        new("Talon", "Talon", "Contrato Concluido"),
        new("Taric", "Taric", "Vanguardia Valerosa"),
        new("Thresh", "Thresh", "Pasajero Oscuro"),
        new("Trundle", "Trundle", "Arma Improvisada"),
        new("Vayne", "Vayne", "Herramientas del Cazador"),
        new("Vladimir", "Vladimir", "Donante Universal"),
        new("XinZhao", "Xin Zhao", "Batalla Final"),
        new("Yone", "Yone", "Demonio interior")
    ];
    private static readonly Dictionary<string, Guest> ByName = Guests.ToDictionary(guest => TextKey(guest.Name));
    private static readonly Dictionary<string, Guest> ByTitle = Guests.ToDictionary(guest => TextKey(guest.Title));

    private readonly record struct Guest(string Key, string Name, string Title);
    private readonly record struct Edge(int Y, int Right, int Length);
    private readonly record struct Ballot(Edge First, Edge Middle, Edge Last);
    private byte[] pixels = [];
    private Ballot? readBallot;
    private (int Width, int Height) readSize;
    private ArenaVoteOffer? cached;
    private long retryAt;
    private long retryHealthAt;

    internal void Clear()
    {
        readBallot = null;
        cached = null;
        retryAt = 0;
        retryHealthAt = 0;
    }

    internal ArenaVoteOffer? Read(BitmapSource frame)
    {
        if (LocateBallot(frame) is not { } ballot)
        {
            Clear();
            return null;
        }

        var size = (frame.PixelWidth, frame.PixelHeight);
        var now = Environment.TickCount64;
        int pitch = (ballot.Last.Y - ballot.First.Y) / 2;
        if (readSize == size && readBallot is { } prior && Near(prior.First, ballot.First)
            && Near(prior.Middle, ballot.Middle) && Near(prior.Last, ballot.Last)
            && (cached is not null || now < retryAt))
        {
            if (cached is { TeamHealth: null } offer && retryHealthAt != 0 && now >= retryHealthAt)
            {
                retryHealthAt = 0;
                if (ArenaVoteHealth.Read(frame, pitch, ocr) is { } health)
                    cached = offer with { TeamHealth = health };
            }
            return cached;
        }

        readSize = size;
        readBallot = ballot;
        retryAt = now + 350;
        cached = null;
        var options = new List<ArenaVoteOption>(3);
        int recognizedTitles = 0;
        foreach (var edge in new[] { ballot.First, ballot.Middle, ballot.Last })
        {
            int textLeft = pitch * 86 / 100;
            int textWidth = edge.Right - textLeft - Math.Max(2, pitch / 12);
            var titleArea = new Int32Rect(textLeft, edge.Y - pitch * 36 / 100, textWidth, pitch * 29 / 100);
            var guest = FindTitle(ReadLine(frame, titleArea));
            if (guest.HasValue)
                recognizedTitles++;
            else
            {
                var nameArea = new Int32Rect(textLeft, edge.Y - pitch * 67 / 100, textWidth, pitch * 22 / 100);
                if (ByName.TryGetValue(TextKey(ReadLine(frame, nameArea)), out var named))
                    guest = named;
            }
            if (guest is not { } found || options.Exists(option => option.Guest == found.Key))
                return null;

            int left = pitch / 16;
            int top = edge.Y - pitch * 71 / 100;
            int right = Math.Min(frame.PixelWidth, edge.Right + pitch / 17);
            options.Add(new(found.Key, found.Title, new(left, top, right - left, edge.Y + pitch / 29 - top)));
        }
        if (recognizedTitles < 2)
            return null;

        var teamHealth = ArenaVoteHealth.Read(frame, pitch, ocr);
        retryHealthAt = teamHealth.HasValue ? 0 : Environment.TickCount64 + 350;
        return cached = new(ReadRound(frame, pitch), options.ToArray(), teamHealth);

        static bool Near(Edge left, Edge right) =>
            Math.Abs(left.Y - right.Y) <= 2 && Math.Abs(left.Right - right.Right) <= 4;
    }

    private Ballot? LocateBallot(BitmapSource frame)
    {
        int height = frame.PixelHeight;
        int width = Math.Min(frame.PixelWidth / 4, height * 45 / 100);
        int top = height / 5;
        int rows = height * 2 / 3 - top;
        if (height < 400 || width < 96)
            return null;

        var image = frame.Format == PixelFormats.Bgr32 || frame.Format == PixelFormats.Bgra32 || frame.Format == PixelFormats.Pbgra32
            ? frame : new FormatConvertedBitmap(frame, PixelFormats.Bgr32, null, 0);
        int stride = width * 4;
        if (pixels.Length < stride * rows)
            pixels = new byte[stride * rows];
        image.CopyPixels(new Int32Rect(0, top, width, rows), pixels, stride, 0);

        // Native vote labels share a flat ink-blue surface and a brass bottom edge.
        int step = Math.Max(1, height / 720);
        int above = Math.Max(1, height / 440);
        int minimumLength = Math.Max(60, height / 10);
        var edges = new List<Edge>(3);
        for (int y = above; y < rows; y++)
        {
            int start = -1, last = -1;
            Edge best = default;
            for (int x = 0; x < width; x += step)
            {
                int index = y * stride + x * 4;
                if (!IsBottomEdge(index, index - above * stride))
                    continue;
                if (last < 0 || x - last > 5 * step)
                    start = x;
                last = x;
                if (x - start > best.Length)
                    best = new(top + y, x, x - start);
            }
            if (best.Length < minimumLength)
                continue;
            if (edges.Count == 0 || best.Y - edges[^1].Y > above * 2)
                edges.Add(best);
            else if (best.Length > edges[^1].Length)
                edges[^1] = best;
        }

        for (int i = 0; i + 2 < edges.Count; i++)
        {
            var first = edges[i];
            for (int j = i + 1; j + 1 < edges.Count; j++)
            {
                var middle = edges[j];
                int pitch = middle.Y - first.Y;
                if (pitch > height / 7)
                    break;

                int tolerance = Math.Max(2, pitch / 15);
                if (pitch < height / 27 || first.Right < pitch * 2 || first.Right > pitch * 5
                    || Math.Abs(first.Right - middle.Right) > tolerance)
                    continue;

                int expected = middle.Y + pitch;
                for (int k = j + 1; k < edges.Count && edges[k].Y <= expected + tolerance; k++)
                {
                    var last = edges[k];
                    if (last.Y >= expected - tolerance && Math.Abs(first.Right - last.Right) <= tolerance)
                        return new(first, middle, last);
                }
            }
        }
        return null;
    }

    private bool IsBottomEdge(int lower, int upper)
    {
        int blue = pixels[lower], green = pixels[lower + 1], red = pixels[lower + 2];
        if (red < 45 || red - green < 4 || green - blue < 10)
            return false;
        int bodyRed = pixels[upper + 2], bodyGreen = pixels[upper + 1], bodyBlue = pixels[upper];
        return bodyRed is >= 9 and <= 35 && bodyGreen - bodyRed is >= 5 and <= 17 && bodyBlue - bodyGreen is >= 3 and <= 14;
    }

    private int? ReadRound(BitmapSource frame, int pitch)
    {
        int halfWidth = pitch * 3 / 5;
        var region = new Int32Rect(frame.PixelWidth / 2 - halfWidth, pitch / 24, halfWidth * 2, pitch / 4);
        var text = TextKey(ReadLine(frame, region));
        int prefixLength = text.StartsWith("round", StringComparison.Ordinal) || text.StartsWith("ronda", StringComparison.Ordinal)
            ? 5 : 0;
        if (prefixLength > 0 && int.TryParse(text.AsSpan(prefixLength), out var round) && round is >= 1 and <= 25)
            return round;

        // At a smaller HUD size, the final 8 may be read as a letter in the full label.
        int narrowHalfWidth = pitch * 36 / 100;
        int tightTop = pitch * 8 / 100;
        var tight = new Int32Rect(frame.PixelWidth / 2 - narrowHalfWidth, tightTop,
            narrowHalfWidth * 2, pitch * 24 / 100 - tightTop);
        string label = TextKey(ReadLine(frame, tight));
        if (label.Length != 6 || !(label.StartsWith("round", StringComparison.Ordinal) ||
            label.StartsWith("ronda", StringComparison.Ordinal)))
            return null;
        int digitLeft = pitch * 17 / 100;
        var digit = new Int32Rect(frame.PixelWidth / 2 + digitLeft, pitch * 6 / 100,
            pitch * 30 / 100 - digitLeft, pitch * 19 / 100);
        return ocr.ReadTitle(frame, digit, PageSegMode.SingleChar, minimumScale: 4) == "8" ? 8 : null;
    }

    private string ReadLine(BitmapSource frame, Int32Rect region) =>
        ocr.ReadTitle(frame, region, PageSegMode.SingleLine, minimumScale: 2);

    private static Guest? FindTitle(string text)
    {
        string key = TextKey(text);
        if (ByTitle.TryGetValue(key, out var exact))
            return exact;
        if (key.Length < 6 || key.Length > 40)
            return null;

        Guest? match = null;
        int distance = Math.Min(2, key.Length / 8) + 1;
        foreach (var (title, guest) in ByTitle)
        {
            if (Math.Abs(key.Length - title.Length) > distance)
                continue;
            int difference = Ratings.Distance(key, title);
            if (difference < distance)
            {
                match = guest;
                distance = difference;
            }
            else if (difference == distance)
                match = null;
        }
        return match;
    }

    private static string TextKey(string value) => string.Concat(Ratings.Normalize(value).Where(char.IsAsciiLetterOrDigit));
}
