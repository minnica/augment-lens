using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualBasic.FileIO;

namespace Aumentos;

internal sealed class CardHistory
{
    private const string OriginalHeader = "fecha,modo,campeon,parche,partida,seleccion,posicion,id_aumento,aumento,tier,origen";
    private const string HeaderWithOpGg = OriginalHeader + ",opgg";
    private const string Header = HeaderWithOpGg + ",arammayhem_popularidad_global_pct,arammayhem_parche";
    private readonly Ratings ratings;
    private readonly Dictionary<(Mode, int), string> names;
    private readonly List<string> pending = [];
    private readonly (int Id, CardComparison Comparison)?[] slots = new (int, CardComparison)?[4];
    private bool checkedHeader;
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

    internal void Observe(nint handle, Mode mode, string champion, int?[] cards, CardComparison?[] labels, long now)
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

    internal void Import(DateTimeOffset date, Mode mode, string champion, string reference, int?[] cards,
        OpGgSnapshot? snapshot = null, AramMayhemSnapshot? mayhem = null)
    {
        var labels = CardComparison.ForOffer(ratings, new GameContext(mode, champion), cards, new bool[cards.Length],
            snapshot, mayhem: mayhem);
        CaptureRows(date, mode, champion, "", null, cards, labels, $"Captura · tier recalculado · {reference}", remember: false);
    }

    private void CaptureRows(DateTimeOffset date, Mode mode, string champion, string game, int? round,
        int?[] cards, CardComparison?[] labels, string origin, bool remember)
    {
        for (int slot = 0; slot < cards.Length; slot++)
        {
            if (cards[slot] is not { } id || labels[slot] is not { } comparison || !names.TryGetValue((mode, id), out var name))
                continue;
            if (remember)
            {
                if (slots[slot] == (id, comparison))
                    continue;
                slots[slot] = (id, comparison);
            }
            pending.Add(string.Join(",", new[]
            {
                date.ToString("o", CultureInfo.InvariantCulture),
                mode == Mode.Caos ? "ARAM Caos" : "Arena", champion, ratings.Catalog.Patch, game,
                round?.ToString(CultureInfo.InvariantCulture) ?? "", (slot + 1).ToString(CultureInfo.InvariantCulture),
                id.ToString(CultureInfo.InvariantCulture), name, comparison.Local, origin, comparison.ExternalText,
                comparison.Mayhem?.Percent.ToString(CultureInfo.InvariantCulture) ?? "", comparison.Mayhem?.Patch ?? ""
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
        if (!checkedHeader)
        {
            UpgradeHeader();
            checkedHeader = true;
        }
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

    private void UpgradeHeader()
    {
        if (!File.Exists(FilePath)) return;
        var temp = $"{FilePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (var original = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (original.Length == 0) return;
                using var reader = new TextFieldParser(original, Encoding.UTF8, true, leaveOpen: true) { TrimWhiteSpace = false };
                reader.SetDelimiters(",");
                var header = string.Join(",", reader.ReadFields() ?? []);
                if (header == Header) return;
                var oldColumns = header switch
                {
                    OriginalHeader => 11,
                    HeaderWithOpGg => 12,
                    _ => throw new IOException("Columnas de historial desconocidas.")
                };
                var extraColumns = new string(',', oldColumns == 11 ? 3 : 2);
                using var writer = new StreamWriter(temp, false, new UTF8Encoding(true));
                writer.WriteLine(Header);
                while (!reader.EndOfData)
                {
                    var fields = reader.ReadFields();
                    if (fields?.Length != oldColumns) throw new IOException("Fila incompleta en el historial.");
                    writer.WriteLine(string.Join(",", fields.Select(CsvField)) + extraColumns);
                }
            }
            File.Move(temp, FilePath, overwrite: true);
        }
        catch (MalformedLineException error) { throw new IOException("Fila ilegible en el historial.", error); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
