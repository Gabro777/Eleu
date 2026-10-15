namespace Eleu.Braille;

/// <summary>
/// A test page for the Braille printer, to match Eleu's page format with what really fits on the sheet:
/// a ruler with the cells numbered to the last one of the line, a line with the chosen format, two lines of symbols to
/// check the printer's Braille table, then "riga 5", "riga 6"... down to the last line of the page. With double-sided
/// printing a second page shows the back. The page number, the title and the other options are left out on purpose:
/// the page shows only how many cells and lines fit.
/// </summary>
public static class TestPage
{
    /// <summary>The symbols to check, on two lines: if the printer reads them with another Braille table, they come out wrong.</summary>
    public const string Punctuation = ". , ; : ? ! ' \" ( ) [ ]";
    public const string Accents = "à è é ì ò ù @ % & + = /";

    /// <summary>The digits 1-9 and 0 without the number sign (the letters a-j), for the ruler.</summary>
    private const string Digits = "⠁⠃⠉⠙⠑⠋⠛⠓⠊⠚";

    public static List<List<string>> Pages(PageFormat format, Func<string, string> translate)
    {
        int width = Math.Max(1, format.CellsPerLine - format.LeftMargin);
        var pages = new List<List<string>> { Page(format, translate, width, back: false) };
        if (format.DoubleSided)
            pages.Add(Page(format, translate, width, back: true));
        return pages;
    }

    private static List<string> Page(PageFormat format, Func<string, string> translate, int width, bool back)
    {
        var margin = back ? "" : new string(BrailleTranslator.EmptyCell, format.LeftMargin);
        string Line(string text)
        {
            var cells = translate(text);
            return margin + (cells.Length > width ? cells[..width] : cells);
        }

        // The ruler: cell n shows the last digit of n, so the last cell read is the number of cells that fit.
        var ruler = string.Concat(Enumerable.Range(1, width).Select(n => Digits[(n - 1) % 10]));
        var lines = new List<string>
        {
            margin + ruler,
            Line($"{(back ? "retro: " : "")}{format.CellsPerLine} celle, {format.LinesPerPage} righe"),
            Line(Punctuation),
            Line(Accents),
        };
        for (int n = lines.Count + 1; n <= format.LinesPerPage; n++)
            lines.Add(Line(n == format.LinesPerPage ? $"ultima riga {n}" : $"riga {n}"));
        return lines;
    }
}
