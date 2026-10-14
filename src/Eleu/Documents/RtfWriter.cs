using System.Globalization;
using System.Text;

namespace Eleu.Documents;

/// <summary>
/// Converts a <see cref="DocumentContent"/> into RTF to load into the text box.
/// Headings use RichEdit's predefined "heading N" styles, so they stay recognizable.
/// </summary>
public static class RtfWriter
{
    private const int SymbolIndex = 1;

    public static string Write(DocumentContent content, string character = "Segoe UI", float baseSize = DocumentContent.BaseSize)
    {
        var segments = content.Paragraphs.SelectMany(p => p.Segments).ToList();

        // Font table: 0 = the default one, 1 = Symbol (for list bullets), then the other ones used.
        var characters = new List<string> { character, "Symbol" };
        foreach (var name in segments.Select(s => s.Font).OfType<string>())
            if (!characters.Contains(name, StringComparer.OrdinalIgnoreCase))
                characters.Add(name);

        // Color table: index 0 is "automatic".
        var colors = segments.SelectMany(s => new[] { s.Color, s.Background }).OfType<int>().Distinct().ToList();

        var rtf = new StringBuilder();
        rtf.Append(@"{\rtf1\ansi\ansicpg1252\deff0\uc1");
        rtf.Append(@"{\fonttbl");
        for (int i = 0; i < characters.Count; i++)
        {
            rtf.Append(@"{\f").Append(i).Append(i == SymbolIndex ? @"\fnil\fcharset2 " : @"\fnil\fcharset0 ");
            Escape(characters[i], rtf);
            rtf.Append(";}");
        }
        rtf.Append('}');
        rtf.Append(@"{\colortbl ;");
        foreach (var color in colors)
            rtf.Append(@"\red").Append((color >> 16) & 0xFF).Append(@"\green").Append((color >> 8) & 0xFF).Append(@"\blue").Append(color & 0xFF).Append(';');
        rtf.Append('}');
        rtf.Append(@"{\stylesheet{ Normal;}");
        for (int level = 1; level <= 9; level++)
            rtf.Append(@"{\s").Append(level).Append(@" heading ").Append(level).Append(";}");
        rtf.Append("}\r\n");

        int number = 0;
        var paragraphs = content.Paragraphs;
        for (int index = 0; index < paragraphs.Count; index++)
        {
            var paragraph = paragraphs[index];
            var cell = paragraph.Cell;
            var previous = index > 0 ? paragraphs[index - 1].Cell : null;
            var following = index + 1 < paragraphs.Count ? paragraphs[index + 1].Cell : null;
            if (cell is not null && !cell.SameLine(previous))
                WriteRowStart(paragraphs, index, rtf);

            number = paragraph.Numbered ? number + 1 : 0;
            WriteParagraph(paragraph, number, rtf);

            float paragraphSize = paragraph.HeadingLevel > 0
                ? DocumentContent.HeadingSize(paragraph.HeadingLevel)
                : baseSize;

            // The page break is the form feed character at the start of the paragraph, as in Word.
            if (paragraph.PageBreak)
                rtf.Append(@"\page ");

            if (paragraph.Segments.Count == 0)
                rtf.Append(@"\f0\fs").Append(HalfPoints(paragraphSize)).Append(' ');

            foreach (var segment in paragraph.Segments)
            {
                int characterIndex = segment.Font is null
                    ? 0
                    : characters.FindIndex(c => c.Equals(segment.Font, StringComparison.OrdinalIgnoreCase));
                rtf.Append(@"{\f").Append(characterIndex).Append(@"\fs").Append(HalfPoints(segment.Size ?? paragraphSize));
                if (segment.Bold || paragraph.HeadingLevel > 0)
                    rtf.Append(@"\b");
                if (segment.Italic)
                    rtf.Append(@"\i");
                if (segment.Underline)
                    rtf.Append(@"\ul");
                if (segment.Strikethrough)
                    rtf.Append(@"\strike");
                if (segment.Position == TextPosition.Superscript)
                    rtf.Append(@"\super");
                else if (segment.Position == TextPosition.Subscript)
                    rtf.Append(@"\sub");
                if (segment.Color is int color)
                    rtf.Append(@"\cf").Append(colors.IndexOf(color) + 1);
                if (segment.Background is int background)
                    rtf.Append(@"\highlight").Append(colors.IndexOf(background) + 1);
                if (segment.Spacing != 0)
                    rtf.Append(@"\expndtw").Append(Twip(segment.Spacing));
                rtf.Append(' ');
                Escape(segment.Text, rtf);
                rtf.Append('}');
            }
            // In a table the last paragraph of the cell ends with \cell, the last cell of the row also with \row.
            if (cell is null || cell.SameCell(following))
                rtf.Append("\\par\r\n");
            else
                rtf.Append(cell.SameLine(following) ? "\\cell " : "\\cell\\row\r\n");
        }

        rtf.Append('}');
        return rtf.ToString();
    }

    /// <summary>Width of a column without a chosen width: 4 cm, in twips.</summary>
    private const int DefaultColumnWidth = 2268;

    /// <summary>
    /// The start of a table row: \trowd, the optional \trhdr (header row) and for each cell
    /// the thin borders and its right edge (\cellx), adding up the column widths.
    /// </summary>
    private static void WriteRowStart(IReadOnlyList<Paragraph> paragraphs, int index, StringBuilder rtf)
    {
        var before = paragraphs[index].Cell!;
        var cells = paragraphs.Skip(index).Select(p => p.Cell).TakeWhile(c => before.SameLine(c))
            .DistinctBy(c => c!.Column).ToList();
        rtf.Append(@"\trowd\trgaph108\trleft0");
        if (before.Header)
            rtf.Append(@"\trhdr");
        int right = 0;
        foreach (var c in cells)
        {
            right += c!.Width > 0 ? Twip(c.Width) : DefaultColumnWidth;
            rtf.Append(@"\clbrdrl\brdrw10\brdrs\clbrdrt\brdrw10\brdrs\clbrdrr\brdrw10\brdrs\clbrdrb\brdrw10\brdrs");
            rtf.Append(@"\cellx").Append(right);
        }
        rtf.Append("\r\n");
    }

    private static void WriteParagraph(Paragraph paragraph, int number, StringBuilder rtf)
    {
        rtf.Append(@"\pard");
        if (paragraph.Cell is not null)
            rtf.Append(@"\intbl");
        if (paragraph.HeadingLevel > 0)
            rtf.Append(@"\s").Append(paragraph.HeadingLevel);
        rtf.Append(paragraph.Alignment switch
        {
            Alignment.Center => @"\qc",
            Alignment.Right => @"\qr",
            Alignment.Justified => @"\qj",
            _ => @"\ql",
        });

        float left = paragraph.LeftIndent;
        float firstLine = paragraph.FirstLineIndent;
        if (paragraph.Numbered)
        {
            rtf.Append(@"{\pntext\f0 ").Append(number).Append(@".\tab}{\*\pn\pnlvlbody\pnf0\pnindent0\pnstart1\pndec{\pntxta.}}");
            left += 18;
            firstLine = -18;
        }
        else if (paragraph.Bulleted)
        {
            rtf.Append(@"{\pntext\f1\'B7\tab}{\*\pn\pnlvlblt\pnf1\pnindent0{\pntxtb\'B7}}");
            left += 18;
            firstLine = -18;
        }

        if (left != 0)
            rtf.Append(@"\li").Append(Twip(left));
        if (paragraph.RightIndent != 0)
            rtf.Append(@"\ri").Append(Twip(paragraph.RightIndent));
        if (firstLine != 0)
            rtf.Append(@"\fi").Append(Twip(firstLine));
        if (paragraph.SpaceBefore != 0)
            rtf.Append(@"\sb").Append(Twip(paragraph.SpaceBefore));
        if (paragraph.SpaceAfter != 0)
            rtf.Append(@"\sa").Append(Twip(paragraph.SpaceAfter));
        if (paragraph.LineSpacing != 1f)
            rtf.Append(@"\sl").Append((int)Math.Round(paragraph.LineSpacing * 240)).Append(@"\slmult1");
    }

    /// <summary>
    /// Adds to an RTF (e.g. the text box's) the page settings, the header and the footer,
    /// right before the first paragraph. The text box does not keep them: they are added only in saved files.
    /// </summary>
    public static string WithPage(string rtf, PageLayout page)
    {
        var (width, height) = page.Effective;
        var parts = new StringBuilder();
        parts.Append(@"\paperw").Append(Twip(width)).Append(@"\paperh").Append(Twip(height))
            .Append(@"\margl").Append(Twip(page.LeftMargin)).Append(@"\margr").Append(Twip(page.RightMargin))
            .Append(@"\margt").Append(Twip(page.TopMargin)).Append(@"\margb").Append(Twip(page.BottomMargin));
        if (page.Horizontal)
            parts.Append(@"\landscape");
        parts.Append(@"\headery").Append(Twip(page.HeaderDistance)).Append(@"\footery").Append(Twip(page.FooterDistance));
        if (page.SkipFirstPage)
            parts.Append(@"\titlepg{\headerf\pard\par}{\footerf\pard\par}");
        WriteBorder("header", page.Header, parts);
        WriteBorder("footer", page.Footer, parts);
        parts.Append("\r\n");

        int firstParagraph = rtf.IndexOf(@"\pard", StringComparison.Ordinal);
        return firstParagraph < 0 ? rtf : rtf.Insert(firstParagraph, parts.ToString());
    }

    /// <summary>Header or footer: the variables become fields (PAGE, NUMPAGES, ...) that every program updates.</summary>
    private static void WriteBorder(string target, string model, StringBuilder rtf)
    {
        if (model.Length == 0)
            return;
        rtf.Append('{').Append('\\').Append(target).Append(@"\pard\qc ");
        foreach (var (text, field) in PageLayout.Pieces(model))
        {
            if (field is null)
                Escape(text, rtf);
            else
                rtf.Append(@"{\field{\*\fldinst ").Append(field).Append(@"}{\fldrslt 1}}");
        }
        rtf.Append(@"\par}");
    }

    /// <summary>Points to twips (twentieths of a point), the unit of RTF and DOCX.</summary>
    public static int Twip(float points) => (int)Math.Round(points * 20);

    private static string HalfPoints(float points) =>
        ((int)Math.Round(points * 2)).ToString(CultureInfo.InvariantCulture);

    private static void Escape(string text, StringBuilder rtf)
    {
        foreach (char c in text)
        {
            switch (c)
            {
                case '\\' or '{' or '}':
                    rtf.Append('\\').Append(c);
                    break;
                case '\t':
                    rtf.Append(@"\tab ");
                    break;
                case '\n' or '\r':
                    break;
                case < (char)0x80:
                    rtf.Append(c);
                    break;
                default:
                    rtf.Append(@"\u").Append((short)c).Append('?');
                    break;
            }
        }
    }
}
