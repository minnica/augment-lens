using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Aumentos;

internal static class StatShardTests
{
    internal static void Run(Ratings ratings, string assets, string? screenshot)
    {
        var rell = new GameContext(Mode.Arena, "Rell");
        string[] titles = ["Frasmento de noder", "Fragmento de aceleración de habilidad", "Fraomento de Vida"];
        var offer = new DetectionResult(true, new int?[3], titles, new bool[3], titles.Select(StatShardRatings.Recognize).ToArray());
        CheckRell(ratings, rell, offer);
        Check(ratings.Shards.ForOffer(rell, offer with { Items = [false, false, true] }) is null &&
            ratings.Shards.ForOffer(rell, offer with { Cards = [null, null, 22] }) is null &&
            StatShardRatings.Recognize("Fragmento de aspecto") is null,
            "los fragmentos se distinguen de objetos y aumentos");
        var partial = ratings.Shards.ForOffer(rell, offer with { Shards = [null, StatShard.Haste, StatShard.Health] });
        Check(partial?[1]?.ArenaChoice is { Tier: Tier.S, Recommended: false },
            "la lectura incompleta no destaca un ganador");

        if (screenshot is not null)
        {
            var image = Diagnostics.OpenBitmap(screenshot);
            var layouts = new LayoutStore(Path.Combine(assets, "layouts.json"), "")
                .Find(Mode.Arena, image.PixelWidth, image.PixelHeight);
            using var detector = new Detection(ratings, Path.Combine(assets, "tessdata"));
            var captured = detector.ReadPanel(image, new Int32Rect(0, 0, image.PixelWidth, image.PixelHeight),
                layouts, Mode.Arena, previous: null);
            Check(captured is not null, "el yunque de la captura se detectó");
            CheckRell(ratings, rell, captured!.Value.Result);
            Console.WriteLine("Yunque de Rell: Poder C · Aceleración S recomendada · Vida A.");
        }
        CheckCaos(ratings, assets);
        Console.WriteLine("Fragmentos de ARAM Caos y Arena verificados con afinidad local y lectura parcial.");
    }

    private static void CheckCaos(Ratings ratings, string assets)
    {
        const int width = 2560, height = 1440;
        var layouts = new LayoutStore(Path.Combine(assets, "layouts.json"), "").Find(Mode.Caos, width, height);
        var bounds = new Int32Rect(0, 0, width, height);
        using var detector = new Detection(ratings, Path.Combine(assets, "tessdata"));
        string attackSpeed = "Fragmento de velocidad de\nataque";
        var image = DrawOffer(bounds, ["Fragmento de penetración\nde magia", attackSpeed, "Fragmento de daño de\nataque"]);
        var read = detector.ReadPanel(image, bounds, layouts, Mode.Caos, previous: null);
        Check(read?.Result.Shards is [StatShard.MagicPenetration, StatShard.AttackSpeed, StatShard.AttackDamage],
            $"lectura de tres títulos largos en ARAM Caos: {string.Join(" / ", read?.Result.Texts ?? [])}");
        var twistedFate = new GameContext(Mode.Caos, "TwistedFate");
        Check(ratings.Shards.ForOffer(twistedFate, read!.Value.Result) is
                [{ ArenaChoice: { Tier: Tier.A, Recommended: true } }, { ArenaChoice: { Tier: Tier.C } }, { ArenaChoice: { Tier: Tier.C } }] &&
            ratings.Shards.ForOffer(new(Mode.Caos, "Jinx"), read.Value.Result) is
                [{ ArenaChoice: { Tier: Tier.C } }, { ArenaChoice: { Tier: Tier.A } }, { ArenaChoice: { Tier: Tier.A } }],
            "la misma selección de fragmentos recibe tiers según el campeón en ARAM Caos");

        var next = DrawOffer(bounds, ["Fragmento de poder de\nhabilidad", attackSpeed, "Fragmento de armadura"]);
        var changed = detector.ReadPanel(next, bounds, layouts, Mode.Caos, read.Value.Layout);
        Check(changed?.Result.Shards is [StatShard.AbilityPower, StatShard.AttackSpeed, StatShard.Armor] &&
            ratings.Shards.ForOffer(twistedFate, changed.Value.Result) is
                [{ ArenaChoice: { Tier: Tier.A, Recommended: true } }, { ArenaChoice: { Tier: Tier.C } }, { ArenaChoice: { Tier: Tier.B } }],
            $"la oferta nueva actualiza los fragmentos y admite títulos de una línea: {string.Join(" / ", changed?.Result.Texts ?? [])}");
    }

    private static BitmapSource DrawOffer(Int32Rect bounds, string[] titles)
    {
        var visual = new DrawingVisual();
        var typeface = new Typeface(new FontFamily("Cambria"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(Brushes.Black, null, new Rect(0, 0, bounds.Width, bounds.Height));
            drawing.DrawRectangle(new SolidColorBrush(Color.FromRgb(27, 105, 149)), null, new Rect(1142, 1100, 278, 86));
            for (int index = 0; index < titles.Length; index++)
            {
                var title = new FormattedText(titles[index], CultureInfo.GetCultureInfo("es-MX"), FlowDirection.LeftToRight,
                    typeface, 27, Brushes.Beige, 1) { TextAlignment = TextAlignment.Center, LineHeight = 35 };
                drawing.DrawText(title, new Point(790 + 490 * index, titles[index].Contains('\n') ? 538 : 573));
            }
        }
        var image = new RenderTargetBitmap(bounds.Width, bounds.Height, 96, 96, PixelFormats.Pbgra32);
        image.Render(visual);
        return image;
    }

    private static void CheckRell(Ratings ratings, GameContext rell, DetectionResult offer)
    {
        var badges = ratings.Shards.ForOffer(rell, offer);
        Check(offer.Shards is [StatShard.Power, StatShard.Haste, StatShard.Health] && badges is
            [{ ArenaChoice: { Tier: Tier.C } }, { ArenaChoice: { Tier: Tier.S, Recommended: true } }, { ArenaChoice: { Tier: Tier.A } }],
            $"los tres fragmentos quedan identificados y Rell prioriza aceleración: {string.Join(" / ", offer.Texts)}");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
