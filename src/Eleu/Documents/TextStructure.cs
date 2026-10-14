namespace Eleu.Documents;

/// <summary>A table in the text box text: where it starts and ends and, row by row, where the text of each cell is.</summary>
public sealed record TableInText(int Start, int End, IReadOnlyList<IReadOnlyList<(int Start, int End)>> Rows);

/// <summary>The cell a position is in: table, row and column (from 0).</summary>
public sealed record CellInText(TableInText Table, int TableNumber, int Row, int Column)
{
    public (int Start, int End) Text => Table.Rows[Row][Column];
}

/// <summary>
/// How the text box text is built (see Editor.DocumentTextBox): paragraphs separated by '\n' and, in a table,
/// rows starting with <see cref="RowStart"/> and ending with <see cref="RowEnd"/> (each on a
/// "line" of its own) and cells ending with <see cref="CellEnd"/>. For the reader, each cell is a
/// paragraph and the technical lines do not exist.
/// </summary>
public static class TextStructure
{
    public const char RowStart = (char)0xFFF9;
    public const char RowEnd = (char)0xFFFB;
    public const char CellEnd = '\a';

    public static bool IsTechnical(char c) => c is RowStart or RowEnd or CellEnd;

    /// <summary>The text without the technical table characters; the end of a cell becomes a space.</summary>
    public static string Readable(string text) =>
        text.Replace(CellEnd, ' ').Replace(RowStart.ToString(), "").Replace(RowEnd.ToString(), "");

    /// <summary>The real paragraphs, as ranges [Start, End) without the separator; each cell is a paragraph.</summary>
    public static List<(int Start, int End)> Paragraphs(string text)
    {
        var paragraphs = new List<(int, int)>();
        int start = 0;
        for (int i = 0; i <= text.Length; i++)
        {
            if (i < text.Length && text[i] is not ('\n' or CellEnd))
                continue;
            bool isTechnical = i - start == 1 && text[start] is RowStart or RowEnd;
            if (!isTechnical)
                paragraphs.Add((start, i));
            start = i + 1;
        }
        return paragraphs;
    }

    /// <summary>The number (from 0) of the paragraph the position is in; on a technical line, the next paragraph.</summary>
    public static int ParagraphOf(IReadOnlyList<(int Start, int End)> paragraphs, int position)
    {
        for (int i = 0; i < paragraphs.Count; i++)
            if (position <= paragraphs[i].End)
                return i;
        return paragraphs.Count - 1;
    }

    public static List<TableInText> Tables(string text)
    {
        var tables = new List<TableInText>();
        int i = 0;
        while (i < text.Length)
        {
            if (!RowStartAt(text, i))
            {
                i++;
                continue;
            }
            int start = i;
            var rows = new List<IReadOnlyList<(int, int)>>();
            while (RowStartAt(text, i))
            {
                int rowEnd = text.IndexOf(RowEnd, i + 2);
                if (rowEnd < 0)
                    break;
                var cells = new List<(int, int)>();
                int cell = i + 2;
                for (int j = cell; j < rowEnd; j++)
                {
                    if (text[j] != CellEnd)
                        continue;
                    cells.Add((cell, j));
                    cell = j + 1;
                }
                if (cells.Count == 0)
                    cells.Add((i + 2, rowEnd));
                rows.Add(cells);
                i = Math.Min(text.Length, rowEnd + 2);
            }
            if (rows.Count == 0)
            {
                i++;
                continue;
            }
            tables.Add(new TableInText(start, i, rows));
        }
        return tables;
    }

    private static bool RowStartAt(string text, int i) =>
        i + 1 < text.Length && text[i] == RowStart && text[i + 1] == '\n';

    /// <summary>The cell the position is in, or null if it is not in a table.</summary>
    public static CellInText? CellAt(string text, int position)
    {
        var tables = Tables(text);
        for (int t = 0; t < tables.Count; t++)
        {
            var table = tables[t];
            if (position < table.Start || position >= table.End)
                continue;
            for (int r = 0; r < table.Rows.Count; r++)
            {
                var cells = table.Rows[r];
                int rowEnd = cells[^1].End + 3; // \a, RowEnd, \n
                if (position >= rowEnd && r < table.Rows.Count - 1)
                    continue;
                int column = cells.Count - 1;
                for (int c = 0; c < cells.Count; c++)
                {
                    if (position <= cells[c].End)
                    {
                        column = c;
                        break;
                    }
                }
                return new CellInText(table, t, r, column);
            }
        }
        return null;
    }
}
