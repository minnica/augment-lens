using System.IO;
using System.Net;
using System.Net.Http;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Aumentos;

internal static class BuildVisibilityTests
{
    internal static void Run(Ratings ratings, string assets, string? shopShot, string? closedShot)
    {
        CheckChampionLookup().GetAwaiter().GetResult();
        var visibility = new BuildVisibility();
        Check(!visibility.ShouldShow(false), "la build automática espera la tienda");
        visibility.ObserveShop(open: true);
        Check(visibility.ShouldShow(true), "abrir la tienda muestra la build sin pulsar el atajo");
        Check(!visibility.Toggle() && !visibility.ShouldShow(true), "el atajo oculta la build durante la compra");
        Check(visibility.Toggle() && visibility.ShouldShow(true), "el atajo vuelve a mostrar la build en la tienda");
        visibility.Toggle();
        visibility.ObserveShop(open: false);
        visibility.ObserveShop(open: true);
        Check(visibility.ShouldShow(true), "cerrar y reabrir la tienda recupera la visibilidad automática");
        visibility.ObserveShop(open: false);
        Check(visibility.Toggle() && visibility.ShouldShow(false), "el atajo fuerza la build fuera de la tienda");
        Check(!visibility.Toggle() && !visibility.ShouldShow(false), "el atajo devuelve el control a la tienda");
        Check(ShopDetection.IsShopHeader("RECOMENDADOS  TODOS LOS OBJETOS  CONJUNTOS DE OBJETOS") &&
            ShopDetection.IsShopHeader("Recommended | All items") &&
            !ShopDetection.IsShopHeader("La tienda de objetos recomendados"), "identificación por dos pestañas de tienda");

        if (shopShot is null && closedShot is null)
            return;
        using var ocr = new Detection(ratings, Path.Combine(assets, "tessdata"));
        var detector = new ShopDetection();
        Int32Rect? shop = null;
        if (shopShot is not null)
        {
            var frame = Diagnostics.OpenBitmap(shopShot);
            shop = detector.Read(frame, ocr, previous: null);
            Check(shop is not null && detector.Read(frame, ocr, shop) == shop, "tienda real reconocida y sostenida");
            var edge = ShopDetection.ProbeRegion(shop!.Value, frame.PixelWidth, frame.PixelHeight)!.Value;
            var cropped = new CroppedBitmap(frame, edge);
            Check(detector.MatchesProbe(cropped, new Int32Rect(0, 0, cropped.PixelWidth, cropped.PixelHeight)),
                "la señal de reapertura se reconoce con solo el recorte del borde");

            var resized = Transform(frame, 1600, 900, new Rect(0, 0, 1600, 900));
            Check(detector.Read(resized, ocr, previous: null) is not null, "tienda reconocida al cambiar resolución");

            const int offsetX = 120, offsetY = 93;
            var moved = Transform(frame, frame.PixelWidth, frame.PixelHeight,
                new Rect(offsetX, offsetY, frame.PixelWidth, frame.PixelHeight));
            var found = detector.Read(moved, ocr, shop);
            Check(found is { } after && shop is { } before &&
                Math.Abs(after.X - before.X - offsetX) < 15 && Math.Abs(after.Y - before.Y - offsetY) < 5,
                "la build sigue la tienda si se mueve");
        }
        if (closedShot is not null)
        {
            var frame = Diagnostics.OpenBitmap(closedShot);
            Check(detector.Read(frame, ocr, shop) is null, "la selección de aumentos no abre la build");
            if (shop is { } remembered && ShopDetection.ProbeRegion(remembered, frame.PixelWidth, frame.PixelHeight) is { } edge)
            {
                var cropped = new CroppedBitmap(frame, edge);
                Check(!detector.MatchesProbe(cropped, new Int32Rect(0, 0, cropped.PixelWidth, cropped.PixelHeight)),
                    "el recorte deja de confirmar la tienda al cerrar y conserva la posición para reabrir");
            }
        }
    }

    private static async Task CheckChampionLookup()
    {
        using var game = new LocalGameFixture();
        using var http = new HttpClient(game) { BaseAddress = new Uri("https://127.0.0.1:2999/liveclientdata/") };
        var missing = await LiveGame.ReadMatch(http, CancellationToken.None);
        Check(missing is { Mode: Mode.Caos, Match: null }, "el modo confirmado se conserva si la API no identifica al campeón");
        game.MissingChampion = false;
        var loaded = await LiveGame.ReadMatch(http, CancellationToken.None);
        Check(loaded is { Mode: Mode.Caos, Match.Game.Champion: "Gragas" }, "el aviso puede dar paso a una build al reconocer el campeón");
        game.ActiveStats = """{"magicPenetrationFlat":20,"magicPenetrationPercent":0.71775,"critChance":0.58,"lifeSteal":0,"attackRange":500}""";
        var withStatistics = await LiveGame.ReadMatch(http, CancellationToken.None);
        Check(withStatistics?.Match?.Mayhem is { MagicPenetration: { Flat: 20, Remaining: 0.71775 },
            CombatStats: { CritChance: 0.58, LifeSteal: 0, AttackRange: 500 }, Opponents: [{ Champion: "Lux", Level: 14 }] },
            "la API entrega las estadísticas de combate y el nivel de los rivales");
        game.ActiveStats = """{"magicPenetrationFlat":20,"magicPenetrationPercent":"desconocido","critChance":8,"lifeSteal":0.1,"attackRange":0}""";
        Check(await LiveGame.ReadMatch(http, CancellationToken.None) is
            { Match.Mayhem: { MagicPenetration: null, CombatStats: { CritChance: null, LifeSteal: 0.1, AttackRange: null }, Items: [3020] } },
            "un campo inválido no oculta las estadísticas válidas ni el inventario");
        game.SupportedMode = false;
        Check(await LiveGame.ReadMatch(http, CancellationToken.None) is null, "los modos incompatibles no muestran el aviso de campeón");
    }

    private sealed class LocalGameFixture : HttpMessageHandler
    {
        internal bool MissingChampion { get; set; } = true;
        internal bool SupportedMode { get; set; } = true;
        internal string? ActiveStats { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string? payload = request.RequestUri?.AbsolutePath switch
            {
                "/liveclientdata/gamestats" => SupportedMode ? """{"gameMode":"KIWI"}""" : """{"gameMode":"ARAM"}""",
                "/liveclientdata/activeplayername" when !MissingChampion => """
                    "Player#000"
                    """,
                "/liveclientdata/activeplayer" when ActiveStats is not null =>
                    $$"""{"riotId":"Player#000","championStats":{{ActiveStats}}}""",
                "/liveclientdata/playerlist" => """
                    [{"riotId":"Player#000","rawChampionName":"game_character_displayname_Gragas","team":"ORDER","items":[{"itemID":3020}]},
                     {"rawChampionName":"game_character_displayname_Lux","team":"CHAOS","level":14,"items":[]}]
                    """,
                _ => null
            };
            return Task.FromResult(payload is null ? new HttpResponseMessage(HttpStatusCode.NotFound) :
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(payload) });
        }
    }

    private static BitmapSource Transform(BitmapSource input, int width, int height, Rect destination)
    {
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
            drawing.DrawImage(input, destination);
        var frame = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        frame.Render(visual);
        return frame;
    }

    private static void Check(bool result, string message)
    {
        if (!result)
            throw new InvalidOperationException(message);
    }
}
