using System.Text;

namespace Eleu.Braille;

/// <summary>
/// Opens a BRF file made by Eleu or by another program and turns it back into text paragraphs, so it can be edited.
/// The page layout is undone: page numbers and running titles, print page lines, table of contents leaders and the
/// blank fold lines are dropped, the left margin is removed, wrapped lines are joined again. A new paragraph starts on
/// an indented line or after a blank line, as Braille layouts mark them; with double spacing, where every line is
/// followed by a blank one, after a line that ends a sentence when the next one starts with a capital.
/// </summary>
public static class BrfReader
{
    private const char Empty = BrailleTranslator.EmptyCell;

    /// <summary>The number sign (dots 3-4-5-6) and the digits 1-9, 0 (the letters a-j).</summary>
    private const char NumberSign = (char)0x283C;
    private const string Digits = "⠁⠃⠉⠙⠑⠋⠛⠓⠊⠚";

    /// <summary>The paragraphs of the file and how many Braille pages it has.</summary>
    public static (List<string> Paragraphs, int Pages) Read(byte[] brf, Func<string, string> backTranslate)
    {
        var pages = Pages(brf);
        return (Paragraphs(pages, backTranslate), pages.Count);
    }

    /// <summary>The file as pages of lines of Unicode Braille cells.</summary>
    public static List<List<string>> Pages(byte[] brf)
    {
        var text = Encoding.Latin1.GetString(brf).Replace("\r\n", "\n").Replace('\r', '\n');
        var pages = new List<List<string>>();
        foreach (var page in text.Split('\f'))
        {
            var lines = page.Split('\n').Select(Cells).ToList();
            while (lines.Count > 0 && lines[^1].Length == 0)
                lines.RemoveAt(lines.Count - 1);
            pages.Add(lines);
        }
        while (pages.Count > 0 && pages[^1].Count == 0)
            pages.RemoveAt(pages.Count - 1);
        return pages;
    }

    private static string Cells(string line)
    {
        var cells = new StringBuilder(line.Length);
        foreach (char c in line)
        {
            int dots = BrfFormat.AsciiTable.IndexOf(char.ToUpperInvariant(c));
            cells.Append(dots < 0 ? Empty : (char)(Empty + dots));
        }
        return cells.ToString().TrimEnd(Empty);
    }

    /// <summary>A text line of the page: its text, its indent past the margin, and whether a blank line comes before it.</summary>
    private sealed record TextLine(string Text, int Indent, bool BlankBefore, bool PageTop);

    public static List<string> Paragraphs(List<List<string>> pages, Func<string, string> backTranslate)
    {
        var lines = TextLines(pages, backTranslate);
        // Double spacing: no two text lines are ever next to each other.
        bool doubleSpaced = lines.Count(l => !l.PageTop) >= 3 && lines.Skip(1).Where(l => !l.PageTop).All(l => l.BlankBefore);

        var paragraphs = new List<string>();
        var current = new StringBuilder();
        void Close()
        {
            if (current.Length > 0)
                paragraphs.Add(current.ToString().Trim());
            current.Clear();
        }

        foreach (var line in lines)
        {
            bool start = line.Indent > 0
                || (doubleSpaced ? EndsSentence(current) && StartsWithCapital(line.Text) : line.BlankBefore);
            if (start)
                Close();
            if (current.Length > 0)
                current.Append(' ');
            current.Append(line.Text);
        }
        Close();
        return paragraphs;
    }

    private static List<TextLine> TextLines(List<List<string>> pages, Func<string, string> backTranslate)
    {
        // A line repeated at the top of the pages is a running title (with or without the page number).
        var tops = pages.Where(p => p.Count > 0).Select(p => WithoutNumber(p[0]).Trim(Empty)).ToList();
        var runningTitle = tops.Count >= 2
            ? tops.GroupBy(t => t).Where(g => g.Key.Length > 0 && g.Count() * 2 >= tops.Count && g.Count() >= 2).Select(g => g.Key).FirstOrDefault()
            : null;

        // Fold lines: two blank lines in the middle of every full page.
        int length = pages.Select(p => p.Count).DefaultIfEmpty(0).Max();
        var full = pages.Where(p => p.Count == length).ToList();
        int fold = length / 2 - 1;
        bool folded = length >= 6 && full.Count > 0 && full.All(p => p[fold].Length == 0 && p[fold + 1].Length == 0);

        // Page numbers and running titles are not text.
        var cleaned = pages.Select(page =>
        {
            var lines = page.ToList();
            if (lines.Count > 0 && (IsNumberOnly(lines[0]) || WithoutNumber(lines[0]) != lines[0]
                || runningTitle is not null && lines[0].Trim(Empty) == runningTitle))
                lines[0] = "";
            if (lines.Count > 0 && IsNumberOnly(lines[^1]))
                lines[^1] = "";
            return lines;
        }).ToList();

        // The binding margin: the smallest indent, on the front and on the back pages (with double-sided printing
        // the margin is only on the front). A single page could mistake a paragraph indent for the margin.
        int Margin(int parity) => cleaned.Where((_, i) => i % 2 == parity)
            .SelectMany(p => p).Where(l => l.Length > 0 && !IsLayoutLine(l)).Select(IndentOf).DefaultIfEmpty(0).Min();
        int front = Margin(0);
        int[] margins = [front, Math.Min(front, Margin(1))]; // the back never has more margin than the front

        var result = new List<TextLine>();
        bool blankBefore = false, leaders = false;
        for (int p = 0; p < cleaned.Count; p++)
        {
            var lines = cleaned[p];
            int margin = margins[p % 2];
            bool pageTop = true;
            blankBefore = false; // a blank line at the end of the page says nothing about the next one
            for (int i = 0; i < lines.Count; i++)
            {
                var raw = lines[i];
                if (folded && (i == fold || i == fold + 1))
                    continue;
                if (raw.Length == 0)
                {
                    // The empty top line (page number or title removed) is not a paragraph break.
                    if (i > 0)
                        blankBefore = true;
                    continue;
                }
                if (IsLayoutLine(raw))
                {
                    leaders |= raw.Contains(new string(Paginator.Dot, 3));
                    continue;
                }
                var line = raw[Math.Min(margin, raw.Length)..];
                var text = backTranslate(line.Trim(Empty)).Trim();
                if (text.Length > 0)
                    result.Add(new TextLine(text, IndentOf(line), blankBefore, pageTop));
                pageTop = false;
                blankBefore = false;
            }
        }
        // The table of contents heading is left alone once its entries are gone.
        if (leaders && result.Count > 0 && result[0].Text.Equals("Sommario", StringComparison.OrdinalIgnoreCase))
        {
            result.RemoveAt(0);
            if (result.Count > 0)
                result[0] = result[0] with { BlankBefore = true };
        }
        return result;
    }

    private static bool EndsSentence(StringBuilder text) => text.Length > 0 && ".!?:»\"".Contains(text[^1]);

    private static bool StartsWithCapital(string text) => text.Length > 0 && char.IsUpper(text[0]);

    private static int IndentOf(string line) => line.Length - line.TrimStart(Empty).Length;

    /// <summary>Print page lines (a run of dashes and a number) and table of contents entries (leader dots).</summary>
    private static bool IsLayoutLine(string line) =>
        line.Contains(new string(Paginator.Hyphen, 4)) || line.Contains(new string(Paginator.Dot, 3));

    /// <summary>A line with only a Braille number: a page number at the top or bottom of the page.</summary>
    private static bool IsNumberOnly(string line)
    {
        var words = line.Split(Empty, StringSplitOptions.RemoveEmptyEntries);
        return words.Length == 1 && IsNumber(words[0]);
    }

    private static bool IsNumber(string word) =>
        word.Length >= 2 && word[0] == NumberSign && word[1..].All(c => Digits.Contains(c));

    /// <summary>The line without a page number at its right end (separated by at least two empty cells).</summary>
    private static string WithoutNumber(string line)
    {
        int end = line.LastIndexOf(Empty);
        if (end > 0 && IsNumber(line[(end + 1)..]) && line[end - 1] == Empty)
            return line[..end].TrimEnd(Empty);
        return line;
    }
}
