using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Aumentos;

internal static class ArenaLegendaryTests
{
    private static readonly string[] RellOffer = ["Flautas de Bandle", "Fuerza de la Naturaleza", "Convergencia de Zeke"];

    internal static void Run(Ratings ratings, string assets, string? screenshot)
    {
        var rell = new GameContext(Mode.Arena, "Rell");
        var offer = new DetectionResult(true, new int?[3], RellOffer, [true, true, true]);
        CheckRellOffer(ratings, rell, offer);

        var jinx = ratings.Legendaries.ForOffer(rell with { Champion = "Jinx" },
            offer with { Texts = ["Filo del Infinito", "Corazón de Acero", "Convergencia de Zeke"] });
        Check(jinx is [ { ArenaChoice: { Tier: Tier.A, Recommended: true } }, { Local: "C" }, { Local: "C" } ],
            "los objetos de críticos favorecen a Jinx y los de tanque a Rell");
        Check(ratings.Prismatics.Identify(offer) is null &&
            ratings.Legendaries.Identify(offer with { Texts = ["Cuota del Segador", "Armadura Pétrea", "Fulminación"] }) is null &&
            ratings.Legendaries.Identify(offer with { Texts = ["Flautas de Bandle", "Guantelete del Agujero Negro", "Convergencia de Zeke"] }) is null &&
            ratings.Legendaries.Identify(offer with { Texts = ["Botas de Mercurio", "Fragmento de Vida", "Botas Jonias de la Lucidez"] }) is null &&
            ratings.Legendaries.Identify(offer with { Cards = [1, null, null] }) is null &&
            ratings.Legendaries.ForOffer(rell with { Mode = Mode.Caos }, offer) is null,
            "las cartas legendarias se distinguen de prismáticos, fragmentos, botas y aumentos");

        var partial = ratings.Legendaries.ForOffer(rell,
            offer with { Texts = ["Flautas de Bandle", "Convergencia de Zeke", ""], Items = [true, true, false] });
        Check(partial is [ { ArenaChoice: { Tier: Tier.S, Recommended: false } }, { Local: "A" }, null ],
            "una carta ilegible conserva los tiers conocidos sin elegir la mejor de toda la oferta");
        CheckSharedChemtechTitle(ratings, assets);

        if (screenshot is not null)
        {
            var image = Diagnostics.OpenBitmap(screenshot);
            var layouts = new LayoutStore(Path.Combine(assets, "layouts.json"), "")
                .Find(Mode.Arena, image.PixelWidth, image.PixelHeight);
            using var detector = new Detection(ratings, Path.Combine(assets, "tessdata"));
            var read = detector.ReadPanel(image, new Int32Rect(0, 0, image.PixelWidth, image.PixelHeight),
                layouts, Mode.Arena, previous: null);
            Check(read is not null, "panel de objetos legendarios en la captura de Rell");
            CheckRellOffer(ratings, rell, read!.Value.Result);
            Console.WriteLine("Captura de Rell: Flautas de Bandle S recomendadas · Fuerza de la Naturaleza B · Convergencia de Zeke A.");
        }
        Console.WriteLine("Tiers de objetos legendarios verificados con afinidad por campeón.");
    }

    private static void CheckRellOffer(Ratings ratings, GameContext rell, DetectionResult offer)
    {
        var badges = ratings.Legendaries.ForOffer(rell, offer);
        Check(ratings.Legendaries.Identify(offer)?.Select(item => item?.Id).SequenceEqual([222524, 224401, 223050]) == true &&
            badges is [
                { ArenaChoice: { Tier: Tier.S, Recommended: true } },
                { ArenaChoice: { Tier: Tier.B, Recommended: false } },
                { ArenaChoice: { Tier: Tier.A, Recommended: false } } ] &&
            badges.All(badge => badge!.External is null),
            $"el control de Rell activa Flautas de Bandle: {string.Join(" / ", offer.Texts)}");
    }

    private static void CheckSharedChemtechTitle(Ratings ratings, string assets)
    {
        var bounds = new Int32Rect(0, 0, 2560, 1440);
        var layout = new LayoutStore(Path.Combine(assets, "layouts.json"), "").Find(Mode.Arena, bounds.Width, bounds.Height)
            .Single(l => l.Titles.Length == 3);
        using var detector = new Detection(ratings, Path.Combine(assets, "tessdata"));
        var item = Offer("Infligir daño aplica un 40% de Heridas Graves", "a campeones enemigos durante 3 seg.");
        var augment = Offer("Todo el daño que infliges aplica Heridas", "Graves.");

        var first = detector.Scan(item, bounds, layout, Mode.Arena, panelConfirmed: true);
        Check(first.Items[2] && first.Cards[2] is null,
            $"la descripción de Toxina Fúngica conserva el objeto: {first.Texts[2]}");
        var offered = detector.Scan(augment, bounds, layout, Mode.Arena, panelConfirmed: true);
        Check(!offered.Items[2] && offered.Cards[2] == 347,
            $"cambiar solo la descripción reconoce el aumento Putrificador Quimtech: {offered.Texts[2]}");
        var restored = detector.Scan(item, bounds, layout, Mode.Arena, panelConfirmed: true);
        Check(restored.Items[2] && restored.Cards[2] is null,
            "la misma posición recupera su clasificación de objeto al cambiar otra vez la descripción");

        BitmapSource Offer(string firstLine, string secondLine)
        {
            var visual = new DrawingVisual();
            using (var draw = visual.RenderOpen())
            {
                draw.DrawRectangle(Brushes.Black, null, new Rect(0, 0, bounds.Width, bounds.Height));
                var titles = layout.Titles.Select(title => title.Pixels(bounds.Width, bounds.Height)).ToArray();
                var typeface = new Typeface("Georgia");
                Write("Flautas de Bandle", titles[0], 13, 27);
                Write("Convergencia de Zeke", titles[1], 13, 27);
                Write("Putrificador Quimtech", titles[2], 13, 27);
                Write(firstLine, titles[2], 121, 17);
                Write(secondLine, titles[2], 145, 17);

                void Write(string value, Int32Rect title, int offset, double size)
                {
                    var text = new FormattedText(value, CultureInfo.GetCultureInfo("es-MX"), FlowDirection.LeftToRight,
                        typeface, size, Brushes.White, 1);
                    draw.DrawText(text, new Point(title.X + (title.Width - text.Width) / 2, title.Y + offset));
                }
            }
            var frame = new RenderTargetBitmap(bounds.Width, bounds.Height, 96, 96, PixelFormats.Pbgra32);
            frame.Render(visual);
            return frame;
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
