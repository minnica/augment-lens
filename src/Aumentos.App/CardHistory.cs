using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Aumentos;

internal sealed class CardHistory
{
    private const string Header = "fecha,modo,campeon,parche,partida,seleccion,posicion,id_aumento,aumento,tier,origen";
    private readonly Ratings ratings;
    private readonly Dictionary<(Mode, int), string> names;
    private readonly List<string> pending = [];
    private readonly (int Id, string Tier)?[] slots = new (int, string)?[4];
    private nint window;
    private Guid match;
    private int selection;
    private long? closedSince;
    private long nextWrite;
    public string FilePath { get; }

    internal CardHistory(string folder, Ratings ratings)
    {
        FilePath = Path.Combine(folder, "historial-aumentos.csv");
        this.ratings = ratings;
        names = ratings.Catalog.Augments.ToDictionary(card => (Enum.Parse<Mode>(card.Mode), card.Id), card => card.Name);
    }

    internal void Observe(nint handle, Mode mode, string champion, int?[] cards, string?[] labels, long now)
    {
        if (window != handle || selection == 0)
        {
            window = handle;
            match = Guid.NewGuid();
            selection = 1;
            Array.Clear(slots);
        }
        else if (closedSince is { } closed && now - closed >= 1200)
        {
            selection++;
            Array.Clear(slots);
        }
        closedSince = null;
        CaptureRows(DateTimeOffset.Now, mode, champion, match.ToString("N"), selection, cards, labels, "Partida", remember: true);
    }

    internal void ObserveClosed(nint handle, long now)
    {
        if (window == handle && selection > 0)
            closedSince ??= now;
    }

    internal void Import(DateTimeOffset date, Mode mode, string champion, string reference, int?[] cards)
    {
        var labels = cards.Select(id => id.HasValue ? ratings.Rate(mode, champion, id.Value)?.ToString() ?? "Sin tier" : null).ToArray();
        CaptureRows(date, mode, champion, "", null, cards, labels, $"Captura · tier recalculado · {reference}", remember: false);
    }

    private void CaptureRows(DateTimeOffset date, Mode mode, string champion, string game, int? round,
        int?[] cards, string?[] labels, string origin, bool remember)
    {
        for (int slot = 0; slot < cards.Length; slot++)
        {
            if (cards[slot] is not { } id || labels[slot] is not { } tier || !names.TryGetValue((mode, id), out var name))
                continue;
            if (remember)
            {
                if (slots[slot] == (id, tier))
                    continue;
                slots[slot] = (id, tier);
            }
            pending.Add(string.Join(",", new[]
            {
                date.ToString("o", CultureInfo.InvariantCulture),
                mode == Mode.Caos ? "ARAM Caos" : "Arena", champion, ratings.Catalog.Patch, game,
                round?.ToString(CultureInfo.InvariantCulture) ?? "", (slot + 1).ToString(CultureInfo.InvariantCulture),
                id.ToString(CultureInfo.InvariantCulture), name, tier, origin
            }.Select(CsvField)));
        }
    }

    internal void WriteIfDue(long now)
    {
        if (pending.Count == 0 || now < nextWrite)
            return;
        try { Flush(); }
        catch (IOException) { nextWrite = now + 5000; }
        catch (UnauthorizedAccessException) { nextWrite = now + 5000; }
    }

    internal void Flush(bool createWhenEmpty = false)
    {
        if (pending.Count == 0 && !createWhenEmpty)
            return;
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        using var stream = new FileStream(FilePath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read);
        long originalLength = stream.Length;
        if (originalLength > 0 && pending.Count == 0)
            return;
        var lines = originalLength == 0 ? new[] { Header }.Concat(pending) : pending;
        var payload = $"{string.Join("\r\n", lines)}\r\n";
        if (originalLength == 0)
            payload = "\uFEFF" + payload;
        try
        {
            stream.Position = originalLength;
            stream.Write(Encoding.UTF8.GetBytes(payload));
            stream.Flush();
        }
        catch (IOException)
        {
            stream.SetLength(originalLength);
            throw;
        }
        pending.Clear();
    }

    private static string CsvField(string value) => $"\"{value.Replace("\"", "\"\"")}\"";
}
