using System.Text;
using System.Text.RegularExpressions;

namespace Eleu.Documents;

/// <summary>
/// Reads the RTF produced by the text box (RichEdit) and converts it into <see cref="DocumentContent"/>.
/// Handles what Eleu can keep: character (font, size, style, colors, position, spacing),
/// paragraph (alignment, indents, spacing, line spacing), headings ("heading N" styles) and lists.
/// The rest is ignored.
/// </summary>
public static partial class RtfReader
{
    /// <summary>Indent added to lists to make room for the bullet or number.</summary>
    public const float ListIndent = 18f;

    // Destinations whose content is not document text.
    private static readonly HashSet<string> TargetsToSkip =
    [
        "fonttbl", "colortbl", "stylesheet", "info", "pict", "object", "header", "footer",
        "headerl", "headerr", "footerl", "footerr", "pntext", "listtable", "listoverridetable",
        "themedata", "colorschememapping", "latentstyles", "datastore", "xmlnstbl", "rsidtbl",
        "generator", "fldinst", "footnote", "annotation",
    ];

    private sealed record CharacterState(Segment Format, bool Skip, int UnicodeSkip);

    public static DocumentContent Read(string rtf)
    {
        var headingStyles = ReadHeadingStyles(rtf);
        var characters = ReadCharacters(rtf);
        var colors = ReadColors(rtf);

        var content = new DocumentContent();
        var paragraph = new Paragraph();
        var text = new StringBuilder();
        var state = new CharacterState(new Segment(""), false, 1);
        var stack = new Stack<CharacterState>();
        var encoding = Encoding.Latin1;
        int charactersToSkip = 0;
        bool groupStart = false;
        var textFormat = state.Format;
        bool multipleLineSpacing = false;
        int rawLineSpacing = 0;

        // Tables: the trowd word opens a row (and a new table if there was no row before), cellx gives the
        // right edge of each cell, intbl says the paragraph is in a table, cell closes the cell, row the row.
        int table = -1, row = -1, column = 0;
        bool inTable = false, afterLine = false, header = false;
        var cellBorders = new List<int>();

        CellPosition CurrentCell()
        {
            int right = column < cellBorders.Count ? cellBorders[column] : 0;
            int left = column > 0 && column - 1 < cellBorders.Count ? cellBorders[column - 1] : 0;
            return new CellPosition(table, row, column, Math.Max(0, right - left) / 20f, header);
        }

        void Format(Func<Segment, Segment> change) => state = state with { Format = change(state.Format) };

        void CloseSegment()
        {
            if (text.Length == 0)
                return;
            paragraph.Add(textFormat with { Text = text.ToString() });
            text.Clear();
        }

        void AddCharacter(char c)
        {
            if (state.Skip)
                return;
            if (charactersToSkip > 0)
            {
                charactersToSkip--;
                return;
            }
            if (text.Length > 0 && textFormat != state.Format)
                CloseSegment();
            textFormat = state.Format;
            text.Append(c);
        }

        void CloseParagraph()
        {
            CloseSegment();
            if (inTable && table >= 0)
                paragraph.Cell = CurrentCell();
            else
                afterLine = false;
            // In RTF paragraph properties last until the next \pard: the next paragraph inherits them
            // as they are in the file, before removing the indent added for lists.
            var next = paragraph.CopyProperties();
            content.Paragraphs.Add(Normalize(paragraph));
            paragraph = next;
        }

        int i = 0;
        while (i < rtf.Length)
        {
            char c = rtf[i];
            switch (c)
            {
                case '{':
                    stack.Push(state);
                    groupStart = true;
                    i++;
                    continue;
                case '}':
                    if (stack.Count > 0)
                        state = stack.Pop();
                    groupStart = false;
                    i++;
                    continue;
                case '\r' or '\n':
                    i++;
                    continue;
                case '\\':
                    break;
                default:
                    AddCharacter(c);
                    groupStart = false;
                    i++;
                    continue;
            }

            // Control symbol
            i++;
            if (i >= rtf.Length)
                break;
            char after = rtf[i];

            if (after is '\\' or '{' or '}')
            {
                AddCharacter(after);
                i++;
                groupStart = false;
                continue;
            }
            if (after == '\'')
            {
                if (i + 2 < rtf.Length && byte.TryParse(rtf.AsSpan(i + 1, 2), System.Globalization.NumberStyles.HexNumber, null, out var b))
                    AddCharacter(encoding.GetString([b])[0]);
                i += 3;
                groupStart = false;
                continue;
            }
            if (after == '*')
            {
                // {\* ...} = optional destination: skipped, but checked for a list definition.
                state = state with { Skip = true };
                if (groupStart && rtf.AsSpan(i + 1).StartsWith(@"\pn") && !rtf.AsSpan(i + 1).StartsWith(@"\pntext"))
                {
                    int end = rtf.IndexOf('}', i);
                    var definition = end > 0 ? rtf.AsSpan(i, end - i) : [];
                    if (definition.Contains(@"\pnlvlblt", StringComparison.Ordinal))
                        paragraph.Bulleted = true;
                    else if (definition.Contains(@"\pnlvl", StringComparison.Ordinal))
                        paragraph.Numbered = true;
                }
                i++;
                continue;
            }
            if (after is '\r' or '\n')
            {
                if (!state.Skip)
                    CloseParagraph();
                i++;
                continue;
            }
            if (after == '~')
            {
                AddCharacter('\u00A0');
                i++;
                continue;
            }
            if (after is '-' or '_' or ':' or '|')
            {
                if (after == '_')
                    AddCharacter('-');
                i++;
                continue;
            }

            // Control word with optional numeric parameter
            int start = i;
            while (i < rtf.Length && char.IsAsciiLetter(rtf[i]))
                i++;
            string word = rtf[start..i];
            int? parameter = null;
            int numberStart = i;
            if (i < rtf.Length && (rtf[i] == '-' || char.IsAsciiDigit(rtf[i])))
            {
                i++;
                while (i < rtf.Length && char.IsAsciiDigit(rtf[i]))
                    i++;
                if (int.TryParse(rtf.AsSpan(numberStart, i - numberStart), out var n))
                    parameter = n;
            }
            if (i < rtf.Length && rtf[i] == ' ')
                i++;

            bool firstWordOfGroup = groupStart;
            groupStart = false;

            if (firstWordOfGroup && TargetsToSkip.Contains(word))
            {
                state = state with { Skip = true };
                continue;
            }

            switch (word)
            {
                case "par" or "line" when !state.Skip:
                    CloseParagraph();
                    break;
                case "page" when !state.Skip:
                    // Page break: the next paragraph (or this one, if it has no text yet) starts on a new page.
                    if (text.Length > 0 || paragraph.Segments.Count > 0)
                        CloseParagraph();
                    paragraph.PageBreak = true;
                    break;
                case "pagebb" when !state.Skip:
                    paragraph.PageBreak = true;
                    break;
                case "trowd":
                    // The row advances at every \row, not at every \trowd: Word repeats the row definition
                    // after the cells too, right before \row.
                    if (!afterLine)
                    {
                        table++;
                        row = 0;
                    }
                    column = 0;
                    cellBorders.Clear();
                    header = false;
                    afterLine = true;
                    break;
                case "cellx" when parameter is int border:
                    cellBorders.Add(border);
                    break;
                case "trhdr":
                    header = true;
                    break;
                case "intbl" when table >= 0:
                    inTable = true;
                    break;
                case "cell" when !state.Skip && inTable:
                    CloseParagraph();
                    column++;
                    break;
                case "row" when !state.Skip:
                    inTable = false;
                    row++;
                    column = 0;
                    afterLine = true;
                    break;
                case "pard":
                    bool justReadBreak = paragraph.PageBreak && paragraph.Segments.Count == 0 && text.Length == 0;
                    paragraph.ResetProperties();
                    inTable = false; // pard also removes intbl, which in a table follows right after
                    paragraph.PageBreak = justReadBreak; // a \page just read also applies after a \pard
                    rawLineSpacing = 0;
                    multipleLineSpacing = false;
                    break;
                case "plain":
                    Format(_ => new Segment(""));
                    break;
                case "b":
                    Format(f => f with { Bold = parameter != 0 });
                    break;
                case "i":
                    Format(f => f with { Italic = parameter != 0 });
                    break;
                case "ul" or "ulw" or "uld" or "uldb" or "ulth" or "uldash":
                    Format(f => f with { Underline = parameter != 0 });
                    break;
                case "ulnone":
                    Format(f => f with { Underline = false });
                    break;
                case "strike":
                    Format(f => f with { Strikethrough = parameter != 0 });
                    break;
                case "super":
                    Format(f => f with { Position = TextPosition.Superscript });
                    break;
                case "sub":
                    Format(f => f with { Position = TextPosition.Subscript });
                    break;
                case "nosupersub":
                    Format(f => f with { Position = TextPosition.Normal });
                    break;
                case "fs" when parameter is > 0:
                    Format(f => f with { Size = parameter.Value / 2f });
                    break;
                case "f" when parameter is int characterNumber && !state.Skip:
                    Format(f => f with { Font = characters.GetValueOrDefault(characterNumber) });
                    break;
                case "cf" when parameter is int colorIndex:
                    Format(f => f with { Color = Color(colors, colorIndex) });
                    break;
                case "highlight" or "cb" or "chcbpat" when parameter is int backgroundIndex:
                    Format(f => f with { Background = Color(colors, backgroundIndex) });
                    break;
                case "expndtw" when parameter is int spacingTwips:
                    Format(f => f with { Spacing = spacingTwips / 20f });
                    break;
                case "expnd" when parameter is int spacingQuarters:
                    Format(f => f with { Spacing = spacingQuarters / 4f });
                    break;
                case "s" when parameter is int styleNumber && !state.Skip:
                    paragraph.HeadingLevel = headingStyles.GetValueOrDefault(styleNumber);
                    break;
                case "ql":
                    paragraph.Alignment = Alignment.Left;
                    break;
                case "qc":
                    paragraph.Alignment = Alignment.Center;
                    break;
                case "qr":
                    paragraph.Alignment = Alignment.Right;
                    break;
                case "qj":
                    paragraph.Alignment = Alignment.Justified;
                    break;
                case "li" when parameter is int left && !state.Skip:
                    paragraph.LeftIndent = left / 20f;
                    break;
                case "ri" when parameter is int right && !state.Skip:
                    paragraph.RightIndent = right / 20f;
                    break;
                case "fi" when parameter is int firstLine && !state.Skip:
                    paragraph.FirstLineIndent = firstLine / 20f;
                    break;
                case "sb" when parameter is int before && !state.Skip:
                    paragraph.SpaceBefore = before / 20f;
                    break;
                case "sa" when parameter is int afterParagraph && !state.Skip:
                    paragraph.SpaceAfter = afterParagraph / 20f;
                    break;
                case "sl" when parameter is int lineSpacing && !state.Skip:
                    rawLineSpacing = lineSpacing;
                    paragraph.LineSpacing = multipleLineSpacing && lineSpacing > 0 ? lineSpacing / 240f : 1f;
                    break;
                case "slmult" when !state.Skip:
                    multipleLineSpacing = parameter == 1;
                    paragraph.LineSpacing = multipleLineSpacing && rawLineSpacing > 0 ? rawLineSpacing / 240f : 1f;
                    break;
                case "ansicpg" when parameter is int page:
                    encoding = EncodingFromCodePage(page);
                    break;
                case "uc" when parameter is int jump:
                    state = state with { UnicodeSkip = jump };
                    break;
                case "u" when parameter is int code:
                    AddCharacter((char)(code < 0 ? code + 65536 : code));
                    charactersToSkip = state.Skip ? 0 : state.UnicodeSkip;
                    break;
                case "tab":
                    AddCharacter('\t');
                    break;
                case "emdash":
                    AddCharacter('\u2014');
                    break;
                case "endash":
                    AddCharacter('\u2013');
                    break;
                case "lquote":
                    AddCharacter('\u2018');
                    break;
                case "rquote":
                    AddCharacter('\u2019');
                    break;
                case "ldblquote":
                    AddCharacter('\u201C');
                    break;
                case "rdblquote":
                    AddCharacter('\u201D');
                    break;
                case "bullet":
                    AddCharacter('\u2022');
                    break;
            }
        }

        CloseSegment();
        if (paragraph.Segments.Count > 0)
            content.Paragraphs.Add(Normalize(paragraph));
        content.Page = ReadPage(rtf);
        return content;
    }

    /// <summary>Paper, margins, header, footer and page numbers written in the RTF.</summary>
    public static PageLayout ReadPage(string rtf)
    {
        var defaults = new PageLayout();
        float Value(string word, float otherwise)
        {
            var m = Regex.Match(rtf, $@"\\{word}(-?\d+)");
            return m.Success ? int.Parse(m.Groups[1].Value) / 20f : otherwise;
        }

        bool horizontal = Regex.IsMatch(rtf, @"\\landscape(?![a-z])");
        float width = Value("paperw", horizontal ? defaults.Height : defaults.Width);
        float height = Value("paperh", horizontal ? defaults.Width : defaults.Height);

        // Fields (PAGE, NUMPAGES, ...) go back to variables like {pagina}, written with escaped braces.
        string WithVariables(string group) => Regex.Replace(group, @"\{\\field\{\\\*\\fldinst\s*([^}]*)\}\{\\fldrslt[^}]*\}\}",
            m => PageLayout.FieldPlaceholder(m.Groups[1].Value) is string s ? s.Replace("{", @"\{").Replace("}", @"\}") : "");
        string Text(string name) => string.Join(" ", GroupText(WithVariables(Group(rtf, @"{\" + name))));

        return new PageLayout(
            horizontal ? height : width,
            horizontal ? width : height,
            horizontal,
            Value("margl", defaults.LeftMargin),
            Value("margr", defaults.RightMargin),
            Value("margt", defaults.TopMargin),
            Value("margb", defaults.BottomMargin),
            Value("headery", defaults.HeaderDistance),
            Value("footery", defaults.FooterDistance),
            Text("header"),
            Text("footer"),
            Regex.IsMatch(rtf, @"\\titlepg(?![a-z])"));
    }

    /// <summary>The text lines of a group like {\header ...}, read as a small document of its own.</summary>
    private static IEnumerable<string> GroupText(string group)
    {
        // "{\header\pard\qc Testo\par}": remove "{\header" (or "{\footer") and the final brace.
        if (group.Length < @"{\header}".Length)
            return [];
        var inner = group[@"{\header".Length..^1];
        return Read(@"{\rtf1 " + inner + "}").Paragraphs.Select(p => p.Text.Trim()).Where(t => t.Length > 0);
    }

    /// <summary>In lists the indent of the bullet or number is not an indent chosen by the user: it is removed.</summary>
    private static Paragraph Normalize(Paragraph paragraph)
    {
        if (paragraph.Bulleted || paragraph.Numbered)
        {
            paragraph.LeftIndent = Math.Max(0, paragraph.LeftIndent - ListIndent);
            paragraph.FirstLineIndent = 0;
        }
        return paragraph;
    }

    private static int? Color(List<int?> colors, int index) => index > 0 && index < colors.Count ? colors[index] : null;

    /// <summary>Maps the stylesheet style numbers (\sN) to heading levels ("heading N").</summary>
    private static Dictionary<int, int> ReadHeadingStyles(string rtf)
    {
        var map = new Dictionary<int, int>();
        foreach (Match m in StyleItem().Matches(rtf))
        {
            var name = m.Groups["nome"].Value.Trim();
            var heading = HeadingName().Match(name);
            if (heading.Success)
                map[int.Parse(m.Groups["numero"].Value)] = int.Parse(heading.Groups[1].Value);
        }
        return map;
    }

    /// <summary>Font table: \fN → font name.</summary>
    private static Dictionary<int, string> ReadCharacters(string rtf)
    {
        var map = new Dictionary<int, string>();
        foreach (Match m in CharacterItem().Matches(Group(rtf, @"{\fonttbl")))
            map.TryAdd(int.Parse(m.Groups["numero"].Value), m.Groups["nome"].Value.Trim());
        return map;
    }

    /// <summary>The RTF group starting with <paramref name="opening"/>, up to the brace that closes it.</summary>
    private static string Group(string rtf, string opening)
    {
        // The name must end there: "{\header" must not find "{\headerf".
        var found = Regex.Match(rtf, Regex.Escape(opening) + "(?![a-z])");
        if (!found.Success)
            return "";
        int start = found.Index;
        int depth = 0;
        for (int i = start; i < rtf.Length; i++)
        {
            if (rtf[i] == '\\')
            {
                i++; // skip the escaped character, including the braces \{ \}
                continue;
            }
            if (rtf[i] == '{')
                depth++;
            else if (rtf[i] == '}' && --depth == 0)
                return rtf[start..(i + 1)];
        }
        return rtf[start..];
    }

    /// <summary>Color table: index 0 is "automatic" (null), the others 0xRRGGBB.</summary>
    private static List<int?> ReadColors(string rtf)
    {
        var colors = new List<int?>();
        var group = Group(rtf, @"{\colortbl");
        if (group.Length == 0)
            return colors;
        var table = group[@"{\colortbl".Length..^1];
        foreach (var entry in table.Split(';')[..^1])
        {
            int Component(string name)
            {
                var m = Regex.Match(entry, $@"\\{name}(\d+)");
                return m.Success ? int.Parse(m.Groups[1].Value) & 0xFF : 0;
            }
            colors.Add(entry.Contains(@"\red") || entry.Contains(@"\green") || entry.Contains(@"\blue")
                ? (Component("red") << 16) | (Component("green") << 8) | Component("blue")
                : null);
        }
        return colors;
    }

    private static Encoding EncodingFromCodePage(int page)
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(page);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException)
        {
            return Encoding.Latin1;
        }
    }

    [GeneratedRegex(@"\{\\s(?<numero>\d+)(?:\\[a-z]+-?\d*\s?)*(?<nome>[^\\{};]+);\}")]
    private static partial Regex StyleItem();

    [GeneratedRegex(@"\{\\f(?<numero>\d+)(?:\\[a-z]+-?\d*\s?)*(?:\{[^{}]*\}\s*)*(?<nome>[^\\{};]+);\}")]
    private static partial Regex CharacterItem();

    [GeneratedRegex(@"^heading\s*(\d)$", RegexOptions.IgnoreCase)]
    private static partial Regex HeadingName();
}
