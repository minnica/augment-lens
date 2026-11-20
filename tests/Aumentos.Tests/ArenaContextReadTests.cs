using System.IO;
using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Aumentos;

internal static class ArenaContextReadTests
{
    internal static void Run(Ratings ratings, string assets, string? levels, string? fireworks)
    {
        CheckInventory();
        CheckCatalog(ratings);
        Check(Detection.ParseArenaUpgradeLevel("Nv. 2\n\nEnfriamiento", maximum: 2) == 2 &&
            Detection.ParseArenaUpgradeLevel("NV.3") == 3 &&
            Detection.ParseArenaUpgradeLevel("Nv. 3", maximum: 2) is null &&
            Detection.ParseArenaUpgradeLevel("Nv. 2\nNv. 3") is null &&
            Detection.ParseArenaUpgradeLevel("Al llegar a Nv. 2\nNivelar 3 aumentos") is null,
            "el OCR exige una línea de nivel inequívoca y comprueba el máximo si se conoce");

        using var detector = new Detection(ratings, Path.Combine(assets, "tessdata"));
        CheckSharedAnvilTitle(detector, assets, ratings);
        if (levels is not null)
        {
            CheckOffer(detector, assets, levels, [364, 1034, 180], [null, 2, 2]);
            var image = Diagnostics.OpenBitmap(levels);
            var layouts = new LayoutStore(Path.Combine(assets, "layouts.json"), "").Find(Mode.Arena,
                image.PixelWidth, image.PixelHeight);
            Check(detector.HasArenaUpgradeHeading(image) && ArenaCardEdge.Read(image, layouts.Single()) is { } edge &&
                Math.Abs(edge - image.PixelHeight * .1582) <= 3, "se reconoce el encabezado dedicado y el borde de sus cartas");
        }
        if (fireworks is not null)
            CheckOffer(detector, assets, fireworks, null, [null, 2, null]);
        Console.WriteLine("Contexto de Arena verificado con inventario propio y niveles por posición.");
    }

    private static void CheckSharedAnvilTitle(Detection detector, string assets, Ratings ratings)
    {
        var candidates = ratings.FindTitle(Mode.Arena, "¡Estadísticas sobre Estadísticas!");
        Check(Detection.ChooseByAnvils(candidates, "¡Obtienes 3 Yunques de Estadísticas!")?.Id == 227 &&
            Detection.ChooseByAnvils(candidates, "¡Obtienes 4 “Y Yunques de Estadísticas!")?.Id == 228 &&
            Detection.ChooseByAnvils(candidates, "4 Yunques de Estadísticas", level: 2)?.Id == 227 &&
            Detection.ChooseByAnvils(candidates, "6 Yunques de Estadísticas", level: 3)?.Id == 228 &&
            Detection.ChooseByAnvils(candidates, "3 Yunques de Estadísticas y 4 Yunques de Estadísticas") is null &&
            ratings.Rate(Mode.Arena, "Milio", 227) == Tier.A && ratings.Rate(Mode.Arena, "Milio", 228) == Tier.A,
            "distingue tres y cuatro yunques en Arena, reconoce el icono y respeta los conteos por nivel");

        var bounds = new Int32Rect(0, 0, 2560, 1440);
        var layout = new LayoutStore(Path.Combine(assets, "layouts.json"), "").Find(Mode.Arena, bounds.Width, bounds.Height).Single();
        var first = detector.Scan(Offer(4), bounds, layout, Mode.Arena, panelConfirmed: true);
        Check(first.Cards.SequenceEqual([1333, 228, 68]),
            $"lee completo el nombre de dos líneas: {string.Join(" / ", first.Texts)}");
        var rerolled = detector.Scan(Offer(3), bounds, layout, Mode.Arena, panelConfirmed: true);
        Check(rerolled.Cards.SequenceEqual([1333, 227, 68]),
            $"cambiar solo el dígito de la descripción relee solo su posición: {string.Join(" / ", rerolled.Cards)}");
        var upgraded = detector.Scan(Offer(4, 2), bounds, layout, Mode.Arena, panelConfirmed: true);
        Check(upgraded.Cards.SequenceEqual([1333, 227, 68]) && upgraded.UpgradeLevels?[1] == 2,
            "el mismo conteo de cuatro yunques con nivel 2 identifica la variante que comienza con tres");
        detector.Clear();

        BitmapSource Offer(int anvils, int? level = null)
        {
            var visual = new DrawingVisual();
            using (var draw = visual.RenderOpen())
            {
                draw.DrawRectangle(Brushes.Black, null, new Rect(0, 0, bounds.Width, bounds.Height));
                var titles = layout.Titles.Select(title => title.Pixels(bounds.Width, bounds.Height)).ToArray();
                Write("Plan de Escape", titles[0], 9);
                Write("¡Estadísticas sobre", titles[1], -17);
                Write("Estadísticas!", titles[1], 17);
                Write("Recursividad", titles[2], 9);
                if (level.HasValue)
                    Write($"Nv. {level.Value}", titles[1], 95, 17);
                var body = new Point(titles[1].X + 30, titles[1].Y + 124);
                draw.DrawText(Text("¡Obtienes", 18), body);
                draw.DrawText(Text(anvils.ToString(CultureInfo.InvariantCulture), 18), body + new Vector(80, 0));
                draw.DrawText(Text("Yunques de Estadísticas!", 18), body + new Vector(100, 0));

                void Write(string text, Int32Rect title, int y, double size = 24)
                {
                    var formatted = Text(text, size);
                    draw.DrawText(formatted, new Point(title.X + (title.Width - formatted.Width) / 2, title.Y + y));
                }
            }
            var image = new RenderTargetBitmap(bounds.Width, bounds.Height, 96, 96, PixelFormats.Pbgra32);
            image.Render(visual);
            return image;
        }

        static FormattedText Text(string text, double size) => new(text, CultureInfo.GetCultureInfo("es-MX"),
            FlowDirection.LeftToRight, new Typeface("Georgia"), size, Brushes.White, 1);
    }

    private static void CheckCatalog(Ratings ratings)
    {
        var deft = ratings.ArenaAugment(22);
        var fireworks = ratings.ArenaAugment(51);
        Check(deft is { MaxLevel: 3 } && deft.EffectAt(2) == "Velocidad de ataque de 60 % a 80 %" &&
            deft.EffectAt(3) == "Velocidad de ataque de 80 % a 130 %" && deft.EffectAt(1) is null &&
            fireworks is { MaxLevel: 3 } &&
            fireworks.EffectAt(2) == "Fuegos artificiales de 4 a 5 · Daño de 90 % a 100 %" &&
            fireworks.EffectAt(3) == "Fuegos artificiales de 5 a 6 · Daño de 100 % a 110 %",
            "las mejoras comparan el nivel anterior y el ofrecido según los datos del parche");
        Check(ratings.ArenaAugment(110) is { MaxLevel: 2 } clothesline &&
            clothesline.EffectAt(2) == "Vida máxima de 2 % a 2.6 % · Daño de ataque de 28 % a 32 % · Poder de habilidad de 16 % a 24 %",
            "En las Cuerdas muestra el daño por segundo de la carta");
        Check(ratings.ArenaAugment(1034) is { MaxLevel: 2 } veil && veil.EffectAt(2) == "Enfriamiento de 20 s a 10 s" &&
            ratings.ArenaAugment(1328) is { MaxLevel: 2 } rhythm &&
            rhythm.EffectAt(2) == "Máximo de acumulaciones de 10 a 20" && rhythm.EffectAt(3) is null,
            "las cartas compartidas usan los valores y máximos propios de Arena");
    }

    private static void CheckInventory()
    {
        const string roster = """
            [
                {"riotIdGameName":"Self","riotIdTagLine":"001","rawChampionName":"game_character_displayname_Rell",
                 "items":[{"itemID":223084},{"itemID":443080}]},
                {"riotId":"Self#002","riotIdGameName":"Self","rawChampionName":"game_character_displayname_Ornn",
                 "items":[{"itemID":223065}]},
                {"riotId":"Mate#001","rawChampionName":"game_character_displayname_Jinx","items":[]},
                {"riotId":"Rival#001","rawChampionName":"game_character_displayname_Vi"}
            ]
            """;
        using var players = JsonDocument.Parse(roster);
        var fallback = LiveGame.ParseMatch(Mode.Arena, players.RootElement, "self#001");
        Check(fallback?.Arena is { InventoryKnown: true } && fallback.Arena.Items.SequenceEqual([223084, 443080]) &&
            LiveGame.ParseMatch(Mode.Arena, players.RootElement, "Self") is null,
            "la API de respaldo distingue a los jugadores con el mismo nombre y toma únicamente los objetos propios");
        Check(LiveGame.ParseMatch(Mode.Arena, players.RootElement, "Mate#001")?.Arena is { InventoryKnown: true, Items.Length: 0 } &&
            LiveGame.ParseMatch(Mode.Arena, players.RootElement, "Rival#001")?.Arena is { InventoryKnown: false, Items.Length: 0 },
            "un inventario vacío comprobado se distingue de uno que la API no entregó");

        using var snapshot = JsonDocument.Parse($$$"""
            {"gameData":{"gameMode":"CHERRY","gameTime":180.5},
             "activePlayer":{"riotIdGameName":"Self","riotIdTagLine":"001","summonerName":"Self"},
             "allPlayers":{{{roster}}}}
            """);
        var match = LiveGame.ParseArenaMatch(snapshot.RootElement);
        Check(match?.Game == new GameContext(Mode.Arena, "Rell") && match.GameTime == 180.5 &&
            match.Arena?.Items.SequenceEqual([223084, 443080]) == true,
            "la instantánea principal une modo, campeón, tiempo e inventario propios");
        Check(!(match! with { GameTime = 181 }).IsNewSession(match) &&
            (match with { GameTime = 8 }).IsNewSession(match) &&
            (match with { Game = new(Mode.Arena, "Ornn") }).IsNewSession(match),
            "un nuevo contexto descarta el inventario aunque se repita el campeón tras reiniciarse el reloj");
    }

    private static void CheckOffer(Detection detection, string assets, string path, int?[]? ids, int?[] levels)
    {
        detection.Clear();
        var image = Diagnostics.OpenBitmap(path);
        var layouts = new LayoutStore(Path.Combine(assets, "layouts.json"), "").Find(Mode.Arena, image.PixelWidth, image.PixelHeight);
        var scan = new Int32Rect(0, 0, image.PixelWidth, image.PixelHeight);
        var initial = detection.ReadPanel(image, scan, layouts, Mode.Arena, null);
        Check(initial?.Result.UpgradeLevels?.SequenceEqual(levels) == true &&
            (ids is null || initial.Value.Result.Cards.SequenceEqual(ids)),
            $"niveles explícitos por posición en {Path.GetFileName(path)}: {string.Join(", ", initial?.Result.UpgradeLevels ?? [])}");

        var layout = initial!.Value.Layout;
        var title = layout.Titles[1].Pixels(image.PixelWidth, image.PixelHeight);
        var visual = new DrawingVisual();
        using (var draw = visual.RenderOpen())
        {
            draw.DrawImage(image, new Rect(0, 0, image.PixelWidth, image.PixelHeight));
            draw.DrawRectangle(Brushes.Black, null,
                new Rect(title.X + title.Width / 4, title.Y + title.Height * 8 / 5, title.Width / 2, title.Height * 7 / 5));
        }
        var changed = new RenderTargetBitmap(image.PixelWidth, image.PixelHeight, 96, 96, PixelFormats.Pbgra32);
        changed.Render(visual);
        var replay = detection.ReadPanel(changed, scan, layouts, Mode.Arena, layout);
        Check(replay?.Result.Cards.SequenceEqual(initial.Value.Result.Cards) == true &&
            replay.Value.Result.UpgradeLevels?[1] is null &&
            replay.Value.Result.UpgradeLevels?[0] == levels[0] &&
            replay.Value.Result.UpgradeLevels?[2] == levels[2],
            "si cambia el nivel central manteniendo el título, se descarta el nivel anterior solo en esa posición");
        var restored = detection.ReadPanel(image, scan, layouts, Mode.Arena, layout);
        Check(restored?.Result.UpgradeLevels?.SequenceEqual(levels) == true,
            "el nivel se recupera cuando reaparece la etiqueta visible");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
