using System.Globalization;

namespace Eleu.Documents;

/// <summary>
/// A table as a grid: rows → cells → paragraphs, plus the width of each column in points (0 = automatic).
/// Used to change rows and columns and to read the table row by row.
/// </summary>
public sealed class Grid
{
    public List<List<List<Paragraph>>> Rows { get; } = [];

    public List<float> Widths { get; } = [];

    public int Columns => Rows.Count == 0 ? 0 : Rows.Max(r => r.Count);

    /// <summary>The grid of a document table (the paragraphs with <see cref="Paragraph.Cell"/> in that table).</summary>
    public static Grid From(IEnumerable<Paragraph> paragraphs, int table = 0)
    {
        var grid = new Grid();
        var cells = paragraphs.Where(p => p.Cell?.Table == table).ToList();
        foreach (var row in cells.GroupBy(p => p.Cell!.Row).OrderBy(g => g.Key))
            grid.Rows.Add(row.GroupBy(p => p.Cell!.Column).OrderBy(g => g.Key).Select(c => c.ToList()).ToList());
        for (int c = 0; c < grid.Columns; c++)
            grid.Widths.Add(cells.Select(p => p.Cell!).Where(x => x.Column == c).Select(x => x.Width).FirstOrDefault(l => l > 0));
        return grid;
    }

    /// <summary>The grid paragraphs with their cell position, ready to be written as table number <paramref name="table"/>.</summary>
    public List<Paragraph> Paragraphs(int table = 0)
    {
        var result = new List<Paragraph>();
        for (int r = 0; r < Rows.Count; r++)
            for (int c = 0; c < Rows[r].Count; c++)
            {
                if (Rows[r][c].Count == 0)
                    Rows[r][c].Add(new Paragraph());
                foreach (var paragraph in Rows[r][c])
                {
                    paragraph.Cell = new CellPosition(table, r, c, c < Widths.Count ? Widths[c] : 0);
                    result.Add(paragraph);
                }
            }
        return result;
    }

    public static string Text(List<Paragraph> cell) => string.Join(" ", cell.Select(p => p.Text.Trim())).Trim();

    public void InsertRow(int index) =>
        Rows.Insert(Math.Clamp(index, 0, Rows.Count), Enumerable.Range(0, Math.Max(1, Columns)).Select(_ => new List<Paragraph> { new() }).ToList());

    public void InsertColumn(int index)
    {
        foreach (var row in Rows)
            row.Insert(Math.Clamp(index, 0, row.Count), [new Paragraph()]);
        // The new column takes the width of its neighbor.
        int nearby = Math.Clamp(index > 0 ? index - 1 : 0, 0, Math.Max(0, Widths.Count - 1));
        Widths.Insert(Math.Clamp(index, 0, Widths.Count), Widths.Count > 0 ? Widths[nearby] : 0);
    }

    public void DeleteColumn(int index)
    {
        foreach (var row in Rows.Where(r => index < r.Count && r.Count > 1))
            row.RemoveAt(index);
        if (index < Widths.Count && Widths.Count > 1)
            Widths.RemoveAt(index);
    }

    /// <summary>
    /// Sorts the rows by the text of the column. Numbers are compared as numbers ("9" before "10"),
    /// the rest in Italian alphabetical order. With <paramref name="header"/> the first row stays on top.
    /// </summary>
    public void Sort(int column, bool ascending, bool header)
    {
        int before = header && Rows.Count > 1 ? 1 : 0;
        var toSort = Rows.Skip(before).ToList();
        var comparison = StringComparer.Create(CultureInfo.GetCultureInfo("it-IT"), ignoreCase: true);
        int Compare(List<List<Paragraph>> a, List<List<Paragraph>> b)
        {
            string ta = column < a.Count ? Text(a[column]) : "", tb = column < b.Count ? Text(b[column]) : "";
            bool na = Number(ta, out var va), nb = Number(tb, out var vb);
            int outcome = na && nb ? va.CompareTo(vb) : na ? -1 : nb ? 1 : comparison.Compare(ta, tb);
            return ascending ? outcome : -outcome;
        }
        // Stable sort: ties keep the previous order.
        var sorted = toSort.Select((r, i) => (r, i)).OrderBy(x => x.r, Comparer<List<List<Paragraph>>>.Create(Compare))
            .ThenBy(x => x.i).Select(x => x.r).ToList();
        Rows.RemoveRange(before, toSort.Count);
        Rows.AddRange(sorted);
    }

    private static bool Number(string text, out decimal @value) =>
        decimal.TryParse(text.Replace("€", "").Replace("%", "").Trim(), NumberStyles.Number, CultureInfo.GetCultureInfo("it-IT"), out @value);

    /// <summary>
    /// The table to read straight through (Braille, audiobook): the first row lists the columns, every following row
    /// becomes a paragraph "Nome: Anna; Età: 40". Empty cells are skipped.
    /// </summary>
    public List<Paragraph> ReadableLines()
    {
        var result = new List<Paragraph>();
        if (Rows.Count == 0)
            return result;
        var header = Rows[0].Select(Text).ToList();
        // Every row read keeps the print page of its first cell (Braille needs it).
        static int Page(List<List<Paragraph>> row) => row.SelectMany(c => c).Select(p => p.PrintPage).FirstOrDefault();
        void Add(string text, int page)
        {
            var p = new Paragraph { PrintPage = page };
            p.Add(new Segment(text));
            result.Add(p);
        }
        Add($"Tabella: {string.Join("; ", header.Where(t => t.Length > 0))}", Page(Rows[0]));
        foreach (var row in Rows.Skip(1))
        {
            var parts = row.Select((cell, c) => (Name: c < header.Count ? header[c] : "", Value: Text(cell)))
                .Where(x => x.Value.Length > 0)
                .Select(x => x.Name.Length > 0 ? $"{x.Name}: {x.Value}" : x.Value);
            Add(string.Join("; ", parts), Page(row));
        }
        Add("Fine tabella", Page(Rows[^1]));
        return result;
    }

    /// <summary>The document with every table in place of its cells in the form of <see cref="ReadableLines"/>.</summary>
    public static DocumentContent Readable(DocumentContent content)
    {
        var result = new DocumentContent { Page = content.Page };
        result.Footnotes.AddRange(content.Footnotes);
        var paragraphs = content.Paragraphs;
        for (int i = 0; i < paragraphs.Count; i++)
        {
            if (paragraphs[i].Cell is not { } cell)
            {
                result.Paragraphs.Add(paragraphs[i]);
                continue;
            }
            int end = i;
            while (end < paragraphs.Count && paragraphs[end].Cell?.Table == cell.Table)
                end++;
            result.Paragraphs.AddRange(From(paragraphs.Skip(i).Take(end - i), cell.Table).ReadableLines());
            i = end - 1;
        }
        return result;
    }
}
