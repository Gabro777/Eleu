namespace Eleu.Braille;

/// <summary>
/// A paragraph already translated into Braille cells. <see cref="Level"/> &gt; 0 for headings (goes in the table of contents);
/// <see cref="PrintPage"/> is the page of the print document where it starts (0 = unknown).
/// </summary>
public sealed record BrailleParagraph(string Cells, bool IsHeading = false, bool NewPage = false, int Level = 0, int PrintPage = 0);

public enum BraillePageNumber { None, TopRight, BottomRight }

/// <summary>
/// Braille page format: cells per line and lines per page, plus numbering, running title on top,
/// left margin (for binding), print page indicators and a table of contents at the start.
/// </summary>
public sealed record PageFormat(int CellsPerLine = 32, int LinesPerPage = 27)
{
    public const int MinCells = 10;
    public const int MaxCells = 60;
    public const int MinLines = 5;
    public const int MaxLines = 60;

    public BraillePageNumber Number { get; init; } = BraillePageNumber.None;

    /// <summary>The document title, already in cells, to repeat on top of every page; null = none.</summary>
    public string? Title { get; init; }

    public int LeftMargin { get; init; }

    /// <summary>The liblouis table (file name) the text is translated with: the language and the kind of Braille.</summary>
    public string Table { get; init; } = BrailleTranslator.DefaultTable;

    /// <summary>Blank cells at the start of every paragraph (not of headings): 2, as usual in Braille; 0 = none.</summary>
    public int ParagraphIndent { get; init; } = 2;

    public const int MaxParagraphIndent = 10;

    /// <summary>A line of dashes with the number where a new print page starts.</summary>
    public bool PrintPages { get; init; }

    public bool TableOfContents { get; init; }

    /// <summary>A blank line between one line of text and the next: easier to read.</summary>
    public bool DoubleSpacing { get; init; }

    /// <summary>
    /// Prints on both sides of the sheet. On the back the binding is on the right: the margin moves to the right and the
    /// page number moves with it, so it does not end up on the edge the printer cannot reach or that gets bound.
    /// </summary>
    public bool DoubleSided { get; init; }

    /// <summary>Two blank lines in the middle of the page: the sheet is folded there (for mailing) without crushing the dots.</summary>
    public bool FoldInHalf { get; init; }

    /// <summary>The two fold lines (from 0, counting all the lines of the page).</summary>
    public (int First, int Second) FoldLines => (LinesPerPage / 2 - 1, LinesPerPage / 2);
}

/// <summary>
/// Lays out the Braille cells on the page: wraps between words, indents the start of paragraphs (two cells, unless chosen otherwise) and
/// leaves a blank line before headings. If asked, adds on top (or at the bottom) the title and the page number,
/// the lines marking print pages and, at the start, a table of contents with the Braille page numbers.
/// </summary>
public static class Paginator
{
    /// <summary>Dots 3-6: the Braille dash, for the lines marking print pages.</summary>
    public const char Hyphen = (char)0x2824;

    /// <summary>Dot 3: the leader dot of tables of contents, between the heading and the page number.</summary>
    public const char Dot = (char)0x2804;

    /// <param name="translate">For page numbers and the word "Sommario"; without it, digits are used as they are.</param>
    public static List<List<string>> Paginate(IEnumerable<BrailleParagraph> paragraphs, PageFormat format, Func<string, string>? translate = null)
    {
        translate ??= t => t;
        var list = paragraphs.ToList();
        int width = Math.Max(4, format.CellsPerLine - format.LeftMargin);
        bool topLine = format.Number == BraillePageNumber.TopRight || format.Title is not null;
        bool bottomLine = format.Number == BraillePageNumber.BottomRight;
        int textLines = Math.Max(1, format.LinesPerPage - (topLine ? 1 : 0) - (bottomLine ? 1 : 0) - (format.FoldInHalf ? 2 : 0));
        List<Line> Spaced(List<Line> rows) => format.DoubleSpacing ? Doubled(rows) : rows;

        var (body, bodyHeadings) = Pages(Spaced(Lines(list, format, width, translate)), textLines);
        var tableOfContents = new List<List<string>>();
        if (format.TableOfContents && bodyHeadings.Count > 0)
        {
            // The table of contents pushes the text pages forward: it is redone until the number of pages stops changing.
            for (int attempt = 0; attempt < 4; attempt++)
            {
                int tocPages = tableOfContents.Count;
                tableOfContents = Pages(Spaced(TableOfContents(bodyHeadings, tocPages, width, translate)), textLines).Pages;
                if (tableOfContents.Count == tocPages)
                    break;
            }
        }

        var result = new List<List<string>>();
        var all = tableOfContents.Concat(body).ToList();
        var (fold1, fold2) = format.FoldLines;
        for (int i = 0; i < all.Count; i++)
        {
            // On the back (even pages, when double-sided) the binding margin is on the right.
            bool back = format.DoubleSided && i % 2 == 1;
            var margin = back ? "" : new string(BrailleTranslator.EmptyCell, format.LeftMargin);
            int usable = format.CellsPerLine - format.LeftMargin;
            var number = translate((i + 1).ToString());
            string? head = topLine
                ? margin + HeaderRow(format.Title ?? "", format.Number == BraillePageNumber.TopRight ? number : "", usable)
                : null;
            string? footer = bottomLine ? margin + HeaderRow("", number, usable) : null;

            // Line by line: title and number on top, number at the bottom, the two fold lines in the middle.
            var page = new List<string>();
            int text = 0;
            var rows = all[i];
            for (int r = 0; r < format.LinesPerPage; r++)
            {
                if (r == 0 && head is not null)
                    page.Add(head);
                else if (r == format.LinesPerPage - 1 && footer is not null)
                    page.Add(footer);
                else if (format.FoldInHalf && (r == fold1 || r == fold2))
                {
                    if (text >= rows.Count && footer is null)
                        break;
                    page.Add("");
                }
                else if (text < rows.Count)
                {
                    // After the fold a blank spacing line is not needed: it would make three blank lines in a row.
                    if (rows[text].Length == 0 && page.Count > 0 && page[^1].Length == 0 && text + 1 < rows.Count)
                        text++;
                    var row = rows[text++];
                    page.Add(row.Length == 0 ? "" : margin + row);
                }
                else if (footer is not null)
                    page.Add("");
                else
                    break;
            }
            result.Add(page);
        }
        return result;
    }

    /// <summary>Double spacing: a blank line after every line of text (never two in a row).</summary>
    private static List<Line> Doubled(List<Line> rows)
    {
        var doubles = new List<Line>();
        for (int i = 0; i < rows.Count; i++)
        {
            doubles.Add(rows[i]);
            bool nextBlank = i + 1 < rows.Count && rows[i + 1].Cells.Length == 0 && !rows[i + 1].NewPage;
            if (rows[i].Cells.Length > 0 && !nextBlank)
                doubles.Add(new Line(""));
        }
        return doubles;
    }

    /// <summary>A line with one text on the left and one on the right; the left text is shortened if there is no room.</summary>
    private static string HeaderRow(string left, string right, int width)
    {
        int space = right.Length > 0 ? width - right.Length - 1 : width;
        if (left.Length > space)
            left = left[..Math.Max(0, space)];
        if (right.Length == 0)
            return left;
        return left + new string(BrailleTranslator.EmptyCell, width - left.Length - right.Length) + right;
    }

    /// <summary>A line of text, with the heading (if it is the start of a heading) and whether it must start a new page.</summary>
    private sealed record Line(string Cells, bool NewPage = false, (string Cells, int Level)? Heading = null);

    private static List<Line> Lines(List<BrailleParagraph> paragraphs, PageFormat format, int width, Func<string, string> translate)
    {
        var rows = new List<Line>();
        int printPage = 0;
        bool newPage = false;
        foreach (var paragraph in paragraphs)
        {
            if (paragraph.NewPage && rows.Count > 0)
                newPage = true;
            if (format.PrintPages && paragraph.PrintPage > 0 && paragraph.PrintPage != printPage)
            {
                // The first print page is not marked: that is where it starts.
                if (printPage > 0)
                {
                    var number = translate(paragraph.PrintPage.ToString());
                    rows.Add(new Line(new string(Hyphen, Math.Max(1, width - number.Length - 1)) + BrailleTranslator.EmptyCell + number, newPage));
                    newPage = false;
                }
                printPage = paragraph.PrintPage;
            }
            if (paragraph.Cells.Trim(BrailleTranslator.EmptyCell, ' ').Length == 0)
                continue;
            if (paragraph.IsHeading && rows.Count > 0 && !newPage)
                rows.Add(new Line(""));
            int indent = paragraph.IsHeading ? 0 : Math.Clamp(format.ParagraphIndent, 0, Math.Max(0, width - 1));
            bool before = true;
            foreach (var row in GoToNextLine(paragraph.Cells, width, indent))
            {
                rows.Add(new Line(row, newPage,
                    before && paragraph.IsHeading ? (paragraph.Cells.Trim(BrailleTranslator.EmptyCell), Math.Max(1, paragraph.Level)) : null));
                newPage = false;
                before = false;
            }
        }
        return rows;
    }

    /// <summary>The lines split into pages, and for each heading the page (from 0) it is on.</summary>
    private static (List<List<string>> Pages, List<(string Cells, int Level, int Page)> Headings) Pages(List<Line> rows, int textLines)
    {
        var pages = new List<List<string>>();
        var headings = new List<(string, int, int)>();
        List<string>? page = null;
        foreach (var row in rows)
        {
            if (page is null || row.NewPage || page.Count >= textLines)
            {
                // A blank line at the top of the page is not needed.
                if (row.Cells.Length == 0 && !row.NewPage && page is not null)
                {
                    page = null;
                    continue;
                }
                page = [];
                pages.Add(page);
            }
            if (row.Cells.Length == 0 && page.Count == 0)
                continue;
            if (row.Heading is var (cells, level))
                headings.Add((cells, level, pages.Count - 1));
            page.Add(row.Cells);
        }
        pages.RemoveAll(p => p.Count == 0);
        return (pages, headings);
    }

    /// <summary>The table of contents lines: "Sommario", then each heading (indented by level), the dots and the page.</summary>
    private static List<Line> TableOfContents(List<(string Cells, int Level, int Page)> headings, int tocPages, int width, Func<string, string> translate)
    {
        var rows = new List<Line> { new(translate("Sommario")), new("") };
        foreach (var (cells, level, page) in headings)
        {
            var number = translate((page + 1 + tocPages).ToString());
            var indent = new string(BrailleTranslator.EmptyCell, Math.Min(6, (level - 1) * 2));
            int space = width - indent.Length - number.Length - 2; // at least one space and one dot
            var heading = cells.Length > space ? cells[..Math.Max(1, space)] : cells;
            var dots = new string(Dot, Math.Max(1, width - indent.Length - heading.Length - number.Length - 2));
            rows.Add(new Line(indent + heading + BrailleTranslator.EmptyCell + dots + BrailleTranslator.EmptyCell + number));
        }
        return rows;
    }

    private static IEnumerable<string> GoToNextLine(string cells, int width, int indent)
    {
        var words = cells.Split(BrailleTranslator.EmptyCell, StringSplitOptions.RemoveEmptyEntries);
        var row = new string(BrailleTranslator.EmptyCell, indent);
        bool blankLine = true;

        foreach (var wholeWord in words)
        {
            var word = wholeWord;
            while (true)
            {
                int space = blankLine ? 0 : 1;
                if (row.Length + space + word.Length <= width)
                {
                    row += (blankLine ? "" : BrailleTranslator.EmptyCell.ToString()) + word;
                    blankLine = false;
                    break;
                }
                if (!blankLine)
                {
                    yield return row;
                    row = "";
                    blankLine = true;
                    continue;
                }
                // Word longer than the line: it gets split.
                int rest = width - row.Length;
                yield return row + word[..rest];
                word = word[rest..];
                row = "";
            }
        }
        if (!blankLine)
            yield return row;
    }
}
