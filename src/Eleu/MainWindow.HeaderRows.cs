using System.Text.RegularExpressions;
using Eleu.Documents;

namespace Eleu;

// Table header rows (those repeated at the top of every page that name the columns).
// The Windows text box does not keep them: as soon as it gets the table it forgets \trhdr. Eleu remembers them separately,
// like bookmarks: for each table with a header, an empty Text Object Model range at the start of the
// table (it moves by itself with the text) and how many header rows it has.
public sealed partial class MainWindow
{
    private readonly List<(dynamic Start, int Rows)> _headerRows = [];
    private List<(int Start, int Rows)>? _pendingHeaderRows;

    private void PrepareHeaderRows()
    {
        _text.HandleDestroyed += (_, _) =>
        {
            _pendingHeaderRows = _headerRows.Select(x => ((int)x.Start.Start, x.Rows)).ToList();
            _headerRows.Clear();
        };
        _text.HandleCreated += (_, _) =>
        {
            if (_pendingHeaderRows is { } pending)
                BeginInvoke(() =>
                {
                    foreach (var (start, rows) in pending)
                        SetHeaderRows(start, rows);
                });
            _pendingHeaderRows = null;
        };
    }

    /// <summary>How many header rows each table in the text has, in order.</summary>
    private int[] HeaderRows(IReadOnlyList<TableInText> tables)
    {
        var byStart = new Dictionary<int, int>();
        foreach (var (start, rows) in _headerRows)
            byStart[(int)start.Start] = rows;
        return tables.Select(t => byStart.TryGetValue(t.Start, out int n) ? Math.Min(n, t.Rows.Count) : 0).ToArray();
    }

    private int HeaderRows(TableInText table) =>
        _headerRows.Where(x => (int)x.Start.Start == table.Start).Select(x => Math.Min(x.Rows, table.Rows.Count)).FirstOrDefault();

    /// <summary>The table starting there has <paramref name="rows"/> header rows (0 = none).</summary>
    private void SetHeaderRows(int tableStart, int rows)
    {
        _headerRows.RemoveAll(x => (int)x.Start.Start == tableStart);
        if (rows > 0 && Tom is { } tom)
            _headerRows.Add((tom.Range(tableStart, tableStart), rows));
    }

    /// <summary>The header rows of each table of a model (the leading rows marked as header).</summary>
    public static Dictionary<int, int> HeaderRowsOfModel(DocumentContent model) =>
        model.Paragraphs.Where(p => p.Cell is not null).GroupBy(p => p.Cell!.Table).ToDictionary(g => g.Key, g =>
        {
            int n = 0;
            foreach (var row in g.GroupBy(p => p.Cell!.Row).OrderBy(r => r.Key))
            {
                if (row.Key != n || !row.Any(p => p.Cell!.Header))
                    break;
                n++;
            }
            return n;
        });

    /// <summary>After loading: the header rows of the model (or of the RTF read) applied to the tables of the text, in order.</summary>
    private void HeaderRowsAfterLoad(DocumentContent? model)
    {
        _headerRows.Clear();
        if (model is null)
            return;
        var counts = HeaderRowsOfModel(model);
        if (counts.Values.All(n => n == 0))
            return;
        var tables = TextStructure.Tables(_text.Text);
        for (int i = 0; i < tables.Count; i++)
            if (counts.TryGetValue(i, out int n) && n > 0)
                SetHeaderRows(tables[i].Start, n);
    }

    /// <summary>Marks the cells of the header rows in the model to save.</summary>
    private void MarkHeaderRows(DocumentContent model)
    {
        if (_headerRows.Count == 0)
            return;
        var rows = HeaderRows(TextStructure.Tables(_text.Text));
        foreach (var p in model.Paragraphs)
            if (p.Cell is { } cell && cell.Table < rows.Length && cell.Row < rows[cell.Table])
                p.Cell = cell with { Header = true };
    }

    /// <summary>
    /// Puts \trhdr back into the RTF to save. The text box writes each table row with its own \trowd, in text
    /// order: the k-th \trowd is the k-th row. If the counts do not match the RTF stays as it is.
    /// </summary>
    private string HeaderRowsInRtf(string rtf)
    {
        if (_headerRows.Count == 0)
            return rtf;
        var tables = TextStructure.Tables(_text.Text);
        var rows = HeaderRows(tables);
        var toMark = new HashSet<int>();
        int firstLine = 0;
        for (int t = 0; t < tables.Count; t++)
        {
            for (int r = 0; r < rows[t]; r++)
                toMark.Add(firstLine + r);
            firstLine += tables[t].Rows.Count;
        }
        if (toMark.Count == 0 || RtfRowStart().Count(rtf) != firstLine)
            return rtf;
        int k = 0;
        return RtfRowStart().Replace(rtf, m => toMark.Contains(k++) ? m.Value + @"\trhdr" : m.Value);
    }

    /// <summary>Inserisci > Tabelle > Riga di intestazione: the first row acts as the header, or no longer.</summary>
    private void ToggleHeaderRow()
    {
        if (CellHere() is not { } cell)
            return;
        bool now = HeaderRows(cell.Table) == 0;
        SetHeaderRows(cell.Table.Start, now ? 1 : 0);
        _text.Modified = true;
        _announcer.Announce(now
            ? "La prima riga è l'intestazione della tabella: si ripete in cima a ogni pagina"
            : "La tabella non ha più la riga di intestazione");
    }

    [GeneratedRegex(@"\\trowd(?![a-z])")]
    private static partial Regex RtfRowStart();
}
