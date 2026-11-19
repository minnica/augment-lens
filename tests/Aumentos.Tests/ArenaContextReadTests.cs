using System.IO;
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
        if (levels is not null)
            CheckOffer(detector, assets, levels, [364, 1034, 180], [null, 2, 2]);
        if (fireworks is not null)
            CheckOffer(detector, assets, fireworks, null, [null, 2, null]);
        Console.WriteLine("Contexto de Arena verificado con inventario propio y niveles por posición.");
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
