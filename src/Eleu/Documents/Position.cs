namespace Eleu.Documents;

/// <summary>
/// Cursor position in logical lines (paragraphs; in a table each cell is a line),
/// not in the visual lines that depend on the window width.
/// Line and column start from 1.
/// </summary>
public readonly record struct Position(int Line, int Column, int TotalLines)
{
    public static Position Compute(string text, int index)
    {
        index = Math.Clamp(index, 0, text.Length);
        var paragraphs = TextStructure.Paragraphs(text);
        int number = TextStructure.ParagraphOf(paragraphs, index);
        int rowStart = paragraphs[number].Start;

        // The page break at the start of a line is invisible: it does not count as a column.
        int invisible = rowStart < index && text[rowStart] == '\f' ? 1 : 0;
        return new Position(number + 1, Math.Max(0, index - rowStart) + 1 - invisible, paragraphs.Count);
    }

    public string Description() => $"Riga {Line} di {TotalLines}, colonna {Column}";
}
