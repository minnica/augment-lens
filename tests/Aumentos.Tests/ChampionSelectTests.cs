using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using Aumentos;

internal static class ChampionSelectTests
{
    internal static void Run()
    {
        using var lobby = JsonDocument.Parse("""{"phase":"ChampSelect","gameData":{"queue":{"id":2400}}}""");
        using var started = JsonDocument.Parse("""{"phase":"InProgress","gameData":{"queue":{"id":2400}}}""");
        using var normalAram = JsonDocument.Parse("""{"phase":"ChampSelect","gameData":{"queue":{"id":450}}}""");
        Check(ChampionSelectClient.IsMayhemSelect(lobby.RootElement) &&
            !ChampionSelectClient.IsMayhemSelect(started.RootElement) &&
            !ChampionSelectClient.IsMayhemSelect(normalAram.RootElement), "fase y cola de ARAM Caos");

        using var selection = JsonDocument.Parse("""
            {"localPlayerCellId":4,"myTeam":[{"cellId":0,"championId":103},
                {"cellId":4,"championId":85,"spell1Id":32,"spell2Id":4}]}
            """);
        using var swapped = JsonDocument.Parse("""
            {"localPlayerCellId":4,"myTeam":[{"cellId":0,"championId":85},
                {"cellId":4,"championId":103,"spell1Id":6,"spell2Id":4}]}
            """);
        using var empty = JsonDocument.Parse("""
            {"localPlayerCellId":4,"myTeam":[{"cellId":0,"championId":85},{"cellId":4,"championId":0}]}
            """);
        Check(ChampionSelectClient.OwnPick(selection.RootElement) == (85, 32, 4) &&
            ChampionSelectClient.OwnPick(swapped.RootElement) == (103, 6, 4) &&
            ChampionSelectClient.OwnPick(empty.RootElement) is null, "campeón y hechizos de la propia celda al hacer un intercambio");

        var page = ReadSpells();
        var main = new OpGgSpellPair("Destello", "Marca");
        Check(page.Spells is { Length: 2 } spells && spells.SequenceEqual([main, new("Destello", "Fantasma")]) &&
            SpellOverlay.Matches(main, new("Kennen", "Marca", "Destello")) &&
            !SpellOverlay.Matches(main, new("Kennen", "Fantasma", "Destello")),
            "combinaciones de OP.GG sin duplicados y comparación independiente de D y F");
        Console.WriteLine("Selección de campeón y hechizos de OP.GG verificados.");
    }

    internal static void Smoke()
    {
        var overlay = new SpellOverlay();
        try
        {
            var bounds = new Int32Rect(10, 10, 1040, 720);
            overlay.Render(new SpellDisplay(bounds, 0, "Kennen", new("Kennen", "Marca", "Destello"), new(ReadSpells(), "Prueba")));
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            var handle = new WindowInteropHelper(overlay).Handle;
            var placement = Native.Bounds(handle);
            var panel = (StackPanel)((Border)((Viewbox)overlay.Content).Child).Child;
            Check(Native.IsWindowVisible(handle) && placement.X > bounds.X + bounds.Width / 2 &&
                placement.X + placement.Width <= bounds.X + bounds.Width && placement.Y >= bounds.Y &&
                placement.Y + placement.Height <= bounds.Y + bounds.Height &&
                (Native.GetWindowLongW(handle, -20) & 0x08000020) == 0x08000020 &&
                panel.Children.OfType<TextBlock>().Select(row => row.Text).SequenceEqual(
                    ["Kennen · Hechizos de OP.GG", "Destello + Marca", "Ya tienes estos hechizos",
                     "Alternativa · Destello + Fantasma", "Parche anterior 26.22"]),
                "panel dentro del cliente, sin interceptar clics y con las dos combinaciones de OP.GG");
            overlay.Render(null);
            Check(!Native.IsWindowVisible(handle), "el panel se retira al salir de selección");
            Console.WriteLine("Panel de hechizos en WPF verificado.");
        }
        finally { overlay.Close(); }
    }

    private static OpGgBuildSnapshot ReadSpells()
    {
        var game = new GameContext(Mode.Caos, "Kennen");
        var flash = Spell("SummonerFlash", "Destello");
        var mark = Spell("SummonerSnowball", "Marca");
        var ghost = Spell("SummonerHaste", "Fantasma");
        return OpGgBuildPage.Read($"""
            <title>Kennen ARAM: Caos, parche 16.22</title><link rel="canonical" href="{OpGgBuildPage.Address(game)}">
            <table><caption>SummonerSpells Table</caption><thead><tr><th>Hechizos de invocador</th></tr></thead>
            <tbody>
            <tr><td>{flash}{mark}</td></tr>
            <tr><td>{mark}{flash}</td></tr>
            <tr><td>{flash}{ghost}</td></tr>
            </tbody></table>
            """, game, "16.23");

        static string Spell(string key, string name) =>
            $"""<img alt="{name}" src="https://opgg-static.akamaized.net/meta/images/lol/16.22.1/spell/{key}.png?w=64">""";
    }

    private static void Check(bool success, string message)
    {
        if (!success) throw new InvalidOperationException($"Falló: {message}");
    }
}
