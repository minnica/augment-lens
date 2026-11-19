using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media.Imaging;
using Tesseract;

namespace Aumentos;

internal sealed record ArenaDraftOffer(Champion Champion, Int32Rect Tile);

internal sealed class ArenaDraftReader(Catalog catalog)
{
    private const double ReferenceWidth = 2000;
    private const double ReferenceHeight = 1125;
    private const int FirstTileX = 551;
    private const int TileY = 205;
    private const int TileSize = 104;
    private const int TileSpacing = 158;
    private const int ChampionSlots = 4;

    private readonly Dictionary<string, Champion> champions = catalog.Champions
        .SelectMany(champion => new[] { NameKey(champion.Name), NameKey(champion.Key) }.Distinct()
            .Select(name => (Name: name, Champion: champion)))
        .GroupBy(entry => entry.Name, StringComparer.Ordinal)
        .Where(group => group.Count() == 1)
        .ToDictionary(group => group.Key, group => group.Single().Champion, StringComparer.Ordinal);

    internal ArenaDraftOffer[]? Read(BitmapSource image, Detection ocr)
    {
        if (image.PixelWidth < 800 || image.PixelHeight < 450 ||
            Math.Abs((double)image.PixelWidth / image.PixelHeight - ReferenceWidth / ReferenceHeight) > 0.08)
            return null;

        Int32Rect Scale(int x, int y, int width, int height) => new Region(x / ReferenceWidth, y / ReferenceHeight,
            width / ReferenceWidth, height / ReferenceHeight).Pixels(image.PixelWidth, image.PixelHeight);

        Int32Rect NameArea(int slot) => Scale(FirstTileX - 20 + slot * TileSpacing, 315, TileSize + 40, 27);

        var random = ocr.ReadTitle(image, Scale(FirstTileX + 7, 315, TileSize - 14, 27), PageSegMode.SingleLine);
        if (Ratings.Normalize(random) != "valentia")
            return null;
        var heading = ocr.ReadTitle(image, Scale(634, 19, 732, 54), PageSegMode.SingleLine);
        if (Ratings.Normalize(heading) != "selecciona a tu campeon")
            return null;

        var offers = new List<ArenaDraftOffer>(ChampionSlots);
        for (int slot = 1; slot <= ChampionSlots; slot++)
        {
            var nameArea = NameArea(slot);
            var title = ocr.ReadTitle(image, nameArea, PageSegMode.SingleLine);
            var champion = champions.GetValueOrDefault(NameKey(title));
            if (champion is null)
            {
                title = ocr.ReadTitle(image, nameArea, PageSegMode.RawLine, minimumScale: 3);
                champion = champions.GetValueOrDefault(NameKey(title));
            }
            if (champion is not null)
                offers.Add(new ArenaDraftOffer(champion, Scale(FirstTileX + slot * TileSpacing, TileY, TileSize, TileSize)));
        }

        return offers.Count >= 3 && offers.Select(offer => offer.Champion.Key).Distinct().Count() == offers.Count
            ? offers.ToArray() : null;
    }

    private static string NameKey(string text) => Ratings.Normalize(text).Replace(" ", "", StringComparison.Ordinal);
}
