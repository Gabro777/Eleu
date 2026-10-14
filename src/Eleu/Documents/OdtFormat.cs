using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Eleu.Documents;

/// <summary>
/// Reading of OpenDocument documents (.odt), LibreOffice's: a zip with the text in content.xml, styles and
/// page in styles.xml, properties in meta.xml and images in Pictures. What fits Eleu's model is read:
/// character and paragraph formatting, headings, lists, simple tables, footnotes, bookmarks, comments and
/// images with alternative text. Writing is in OdtFormat.Writing.cs.
/// </summary>
public static partial class OdtFormat
{
    public const string MimeType = "application/vnd.oasis.opendocument.text";

    /// <summary>The height of a header or footer line, in points.</summary>
    private const float ZoneLine = 14f;

    private static readonly XNamespace Office = "urn:oasis:names:tc:opendocument:xmlns:office:1.0";
    private static readonly XNamespace Style = "urn:oasis:names:tc:opendocument:xmlns:style:1.0";
    private static readonly XNamespace Text = "urn:oasis:names:tc:opendocument:xmlns:text:1.0";
    private static readonly XNamespace Table = "urn:oasis:names:tc:opendocument:xmlns:table:1.0";
    private static readonly XNamespace Draw = "urn:oasis:names:tc:opendocument:xmlns:drawing:1.0";
    private static readonly XNamespace Fo = "urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0";
    private static readonly XNamespace XLink = "http://www.w3.org/1999/xlink";
    private static readonly XNamespace Dc = "http://purl.org/dc/elements/1.1/";
    private static readonly XNamespace Meta = "urn:oasis:names:tc:opendocument:xmlns:meta:1.0";
    private static readonly XNamespace Svg = "urn:oasis:names:tc:opendocument:xmlns:svg-compatible:1.0";
    private static readonly XNamespace Manifest = "urn:oasis:names:tc:opendocument:xmlns:manifest:1.0";

    /// <summary>A style: family, parent, text and paragraph properties, list and heading level.</summary>
    private sealed record OdtStyle(
        string Family,
        string? Parent,
        bool Automatic,
        Dictionary<string, string> TextProperties,
        Dictionary<string, string> ParagraphProperties,
        string? ListStyle,
        int HeadingLevel,
        float ColumnWidth);

    public static DocumentContent Read(Stream stream)
    {
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var contentXml = Xml(zip, "content.xml") ?? throw new InvalidDataException("Il file non è un documento OpenDocument: manca content.xml.");
        var stylesXml = Xml(zip, "styles.xml");
        var metaXml = Xml(zip, "meta.xml");

        var styles = new Dictionary<string, OdtStyle>();
        var lists = new Dictionary<string, XElement>();
        foreach (var root in new[] { stylesXml?.Root?.Element(Office + "styles"), stylesXml?.Root?.Element(Office + "automatic-styles"),
                     contentXml.Root?.Element(Office + "automatic-styles") })
            if (root is not null)
                ReadStyles(root, root.Name.LocalName == "automatic-styles", styles, lists);

        var content = new DocumentContent();
        if (stylesXml is not null)
            content.Page = ReadPage(stylesXml, styles);
        if (metaXml?.Root?.Element(Office + "meta") is { } meta)
            content.Metadata = ReadMetadata(meta);

        var body = contentXml.Root?.Element(Office + "body")?.Element(Office + "text");
        if (body is null)
            return content;
        var reader = new Reader(content, styles, lists, zip,
            body.Descendants(Office + "annotation-end").Select(x => (string?)x.Attribute(Office + "name")).OfType<string>().ToHashSet());
        reader.Blocks(body.Elements(), null);
        reader.Warnings();
        return content;
    }

    private static XDocument? Xml(ZipArchive zip, string name)
    {
        var entry = zip.GetEntry(name);
        if (entry is null)
            return null;
        using var stream = entry.Open();
        return XDocument.Load(stream, LoadOptions.PreserveWhitespace);
    }

    private static void ReadStyles(XElement root, bool automatic, Dictionary<string, OdtStyle> styles, Dictionary<string, XElement> lists)
    {
        foreach (var e in root.Elements())
        {
            if (e.Name == Text + "list-style" && (string?)e.Attribute(Style + "name") is string listName)
            {
                lists[listName] = e;
                continue;
            }
            if (e.Name != Style + "style" || (string?)e.Attribute(Style + "name") is not string name)
                continue;
            static Dictionary<string, string> Properties(XElement? p) =>
                p?.Attributes().ToDictionary(a => a.Name.NamespaceName + "|" + a.Name.LocalName, a => a.Value) ?? [];
            styles[name] = new OdtStyle(
                (string?)e.Attribute(Style + "family") ?? "",
                (string?)e.Attribute(Style + "parent-style-name"),
                automatic,
                Properties(e.Element(Style + "text-properties")),
                Properties(e.Element(Style + "paragraph-properties")),
                (string?)e.Attribute(Style + "list-style-name"),
                int.TryParse((string?)e.Attribute(Style + "default-outline-level"), out int level) ? level : 0,
                Points((string?)e.Element(Style + "table-column-properties")?.Attribute(Style + "column-width")) ?? 0);
        }
    }

    private static string Key(XNamespace ns, string name) => ns.NamespaceName + "|" + name;

    /// <summary>An ODF measure ("2cm", "12pt", "0.5in", "10mm") in points; null if it cannot be understood.</summary>
    public static float? Points(string? measure)
    {
        if (string.IsNullOrWhiteSpace(measure))
            return null;
        measure = measure.Trim();
        int i = 0;
        while (i < measure.Length && (char.IsDigit(measure[i]) || measure[i] is '.' or '-' or '+'))
            i++;
        if (!float.TryParse(measure[..i], NumberStyles.Float, CultureInfo.InvariantCulture, out float @value))
            return null;
        return measure[i..].ToLowerInvariant() switch
        {
            "pt" => @value,
            "cm" => @value * 72f / 2.54f,
            "mm" => @value * 72f / 25.4f,
            "in" or "inch" => @value * 72f,
            "pc" => @value * 12f,
            "px" => @value * 0.75f,
            _ => null,
        };
    }

    private static int? Color(string? @value) =>
        @value is { Length: 7 } && @value[0] == '#' && int.TryParse(@value[1..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int c)
            ? c
            : null;

    private static PageLayout ReadPage(XDocument stylesXml, Dictionary<string, OdtStyle> styles)
    {
        var page = new PageLayout();
        var master = stylesXml.Root?.Element(Office + "master-styles")?.Elements(Style + "master-page").FirstOrDefault();
        var layoutName = (string?)master?.Attribute(Style + "page-layout-name");
        var layout = stylesXml.Root?.Element(Office + "automatic-styles")?.Elements(Style + "page-layout")
            .FirstOrDefault(l => (string?)l.Attribute(Style + "name") == layoutName);
        var p = layout?.Element(Style + "page-layout-properties");
        if (p is null)
            return page;

        float width = Points((string?)p.Attribute(Fo + "page-width")) ?? page.Width;
        float height = Points((string?)p.Attribute(Fo + "page-height")) ?? page.Height;
        bool horizontal = (string?)p.Attribute(Style + "print-orientation") == "landscape" || width > height;
        float top = Points((string?)p.Attribute(Fo + "margin-top")) ?? page.TopMargin;
        float bottom = Points((string?)p.Attribute(Fo + "margin-bottom")) ?? page.BottomMargin;

        // In ODF the header is inside the margin: the text starts after the header and its spacing.
        string header = master?.Element(Style + "header") is { } h ? TextWithVariables(h) : "";
        string footer = master?.Element(Style + "footer") is { } f ? TextWithVariables(f) : "";
        // The zone's space: its height (at least one line) plus the distance from the text.
        float ZoneHeight(string name)
        {
            var z = layout!.Element(Style + name)?.Element(Style + "header-footer-properties");
            float min = Points((string?)z?.Attribute(Fo + "min-height")) ?? 0;
            float distance = Points((string?)z?.Attribute(Fo + (name == "header-style" ? "margin-bottom" : "margin-top"))) ?? 0;
            return Math.Max(min, ZoneLine) + distance;
        }
        float headerDistance = page.HeaderDistance, footerDistance = page.FooterDistance;
        if (header.Length > 0)
        {
            headerDistance = top;
            top += ZoneHeight("header-style");
        }
        if (footer.Length > 0)
        {
            footerDistance = bottom;
            bottom += ZoneHeight("footer-style");
        }

        return page with
        {
            Width = Math.Min(width, height),
            Height = Math.Max(width, height),
            Horizontal = horizontal,
            LeftMargin = Points((string?)p.Attribute(Fo + "margin-left")) ?? page.LeftMargin,
            RightMargin = Points((string?)p.Attribute(Fo + "margin-right")) ?? page.RightMargin,
            TopMargin = top,
            BottomMargin = bottom,
            HeaderDistance = headerDistance,
            FooterDistance = footerDistance,
            Header = header,
            Footer = footer,
            SkipFirstPage = master?.Element(Style + "header-first") is { } first && !first.Descendants(Text + "p").Any(x => x.Value.Length > 0),
        };
    }

    /// <summary>The header or footer text, with fields as Eleu variables ({pagina}, {pagine}...).</summary>
    private static string TextWithVariables(XElement zone)
    {
        var text = new StringBuilder();
        void Read(XNode node)
        {
            switch (node)
            {
                case XText t:
                    text.Append(t.Value);
                    break;
                case XElement e when e.Name == Text + "page-number":
                    text.Append("{pagina}");
                    break;
                case XElement e when e.Name == Text + "page-count":
                    text.Append("{pagine}");
                    break;
                case XElement e when e.Name == Text + "title":
                    text.Append("{titolo}");
                    break;
                case XElement e when e.Name == Text + "date":
                    text.Append("{data}");
                    break;
                case XElement e when e.Name == Text + "s":
                    text.Append(' ', (int?)e.Attribute(Text + "c") ?? 1);
                    break;
                case XElement e when e.Name == Text + "tab":
                    text.Append('\t');
                    break;
                case XElement e:
                    if (e.Name == Text + "p" && text.Length > 0)
                        text.Append(' ');
                    foreach (var child in e.Nodes())
                        Read(child);
                    break;
            }
        }
        Read(zone);
        return text.ToString().Trim();
    }

    private static Metadata ReadMetadata(XElement meta)
    {
        string Field(XName name) => meta.Element(name)?.Value.Trim() ?? "";
        var author = Field(Meta + "initial-creator");
        if (author.Length == 0)
            author = Field(Dc + "creator");
        DateTime? created = DateTime.TryParse(Field(Meta + "creation-date"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var date) ? date : null;
        return new Metadata(Field(Dc + "title"), author, Field(Dc + "subject"),
            string.Join(", ", meta.Elements(Meta + "keyword").Select(k => k.Value.Trim()).Where(k => k.Length > 0)), created);
    }

    /// <summary>Reads the document body, paragraph by paragraph, taking lists and tables into account.</summary>
    private sealed class Reader(DocumentContent content, Dictionary<string, OdtStyle> styles, Dictionary<string, XElement> lists, ZipArchive zip,
        HashSet<string> commentEnds)
    {
        private readonly Dictionary<string, int> _openComments = [];
        private int _tables;
        private bool _pendingBreak;
        private bool _lostImages, _mergedCells, _nestedTables, _closingNotes, _textBoxes;

        /// <summary>Paragraphs, headings, lists, tables and sections, in order. <paramref name="cell"/>: where they are, if in a table.</summary>
        public void Blocks(IEnumerable<XElement> elements, CellPosition? cell, (string? Style, int Level)? list = null)
        {
            foreach (var e in elements)
            {
                if (e.Name == Text + "p" || e.Name == Text + "h")
                    Paragraph(e, cell, list);
                else if (e.Name == Text + "list")
                    List(e, cell, list);
                else if (e.Name == Table + "table")
                {
                    if (cell is not null)
                    {
                        // A table inside a table: its text stays in the outer cell.
                        _nestedTables = true;
                        Blocks(e.Descendants().Where(x => x.Name == Text + "p" || x.Name == Text + "h"), cell);
                    }
                    else
                        ReadTable(e);
                }
                else if (e.Name == Text + "section" || e.Name == Text + "index-body" || e.Name == Text + "table-of-content"
                         || e.Name == Text + "alphabetical-index" || e.Name == Text + "illustration-index" || e.Name == Text + "bibliography"
                         || e.Name == Text + "user-index" || e.Name == Text + "object-index" || e.Name == Text + "table-index")
                    Blocks(e.Elements(), cell, list);
                else if (e.Name == Text + "soft-page-break")
                    continue;
                else if (e.Name == Draw + "frame")
                {
                    var withImage = new Paragraph { Cell = cell };
                    Frame(e, withImage);
                    if (withImage.Segments.Count > 0)
                        content.Paragraphs.Add(withImage);
                }
            }
        }

        /// <summary>At the end of reading: what Eleu could not keep from the file.</summary>
        public void Warnings()
        {
            if (_mergedCells || _nestedTables)
                content.NotPreserved.Add("celle unite o tabelle dentro le tabelle");
            if (_lostImages)
                content.NotPreserved.Add("immagini o disegni che Eleu non sa leggere");
            if (_closingNotes)
                content.NotPreserved.Add("note di chiusura (diventano note a piè di pagina)");
            if (_textBoxes)
                content.NotPreserved.Add("caselle di testo");
        }

        private void List(XElement list, CellPosition? cell, (string? Style, int Level)? outer)
        {
            var style = (string?)list.Attribute(Text + "style-name") ?? outer?.Style;
            var inside = (style, (outer?.Level ?? 0) + 1);
            foreach (var item in list.Elements().Where(x => x.Name == Text + "list-item" || x.Name == Text + "list-header"))
                Blocks(item.Elements(), cell, inside);
        }

        /// <summary>Numbered or bulleted: the list style says it for that level.</summary>
        private bool Numbered(string? listStyle, int level)
        {
            if (listStyle is null || !lists.TryGetValue(listStyle, out var definition))
                return false;
            var levels = definition.Elements().Where(x => x.Name.LocalName.StartsWith("list-level-style", StringComparison.Ordinal)).ToList();
            var chosen = levels.FirstOrDefault(x => (int?)x.Attribute(Text + "level") == level) ?? levels.FirstOrDefault();
            return chosen?.Name == Text + "list-level-style-number" && (string?)chosen.Attribute(Style + "num-format") is not ("" or null);
        }

        private void ReadTable(XElement table)
        {
            int number = _tables++;
            // Column widths, repeated ones too (table:number-columns-repeated).
            var widths = new List<float>();
            foreach (var column in table.Descendants(Table + "table-column").Where(x => x.Ancestors(Table + "table").First() == table))
            {
                int times = Math.Clamp((int?)column.Attribute(Table + "number-columns-repeated") ?? 1, 1, 64);
                float width = (string?)column.Attribute(Table + "style-name") is string name && styles.TryGetValue(name, out var style)
                    ? style.ColumnWidth
                    : 0;
                for (int i = 0; i < times; i++)
                    widths.Add(width);
            }

            int row = 0;
            foreach (var r in table.Descendants(Table + "table-row").Where(x => x.Ancestors(Table + "table").First() == table))
            {
                bool header = r.Parent?.Name == Table + "table-header-rows";
                int column = 0;
                foreach (var c in r.Elements())
                {
                    if (c.Name == Table + "covered-table-cell")
                    {
                        _mergedCells = true;
                        continue;
                    }
                    if (c.Name != Table + "table-cell")
                        continue;
                    if ((int?)c.Attribute(Table + "number-columns-spanned") > 1 || (int?)c.Attribute(Table + "number-rows-spanned") > 1)
                        _mergedCells = true;
                    int times = Math.Clamp((int?)c.Attribute(Table + "number-columns-repeated") ?? 1, 1, 64);
                    for (int v = 0; v < times; v++)
                    {
                        var position = new CellPosition(number, row, column, column < widths.Count ? widths[column] : 0, header);
                        int before = content.Paragraphs.Count;
                        Blocks(c.Elements(), position);
                        if (content.Paragraphs.Count == before)
                            content.Paragraphs.Add(new Paragraph { Cell = position });
                        column++;
                    }
                }
                row++;
            }
        }

        private void Paragraph(XElement p, CellPosition? cell, (string? Style, int Level)? list)
        {
            var styleName = (string?)p.Attribute(Text + "style-name");
            var paragraph = new Paragraph { Cell = cell };
            if (p.Name == Text + "h")
                paragraph.HeadingLevel = Math.Clamp((int?)p.Attribute(Text + "outline-level") ?? 1, 1, 9);
            else if (LevelFromStyle(styleName) is int level and > 0)
                paragraph.HeadingLevel = Math.Min(level, 9);

            // A "page break after" of the previous paragraph applies to this one.
            bool breakFromPrevious = _pendingBreak;
            _pendingBreak = false;
            ParagraphProperties(styleName, paragraph);
            if (breakFromPrevious)
                paragraph.PageBreak = true;

            if (list is { } inList && inList.Level > 0 && paragraph.HeadingLevel == 0)
            {
                if (Numbered(inList.Style ?? ListStyleOf(styleName), inList.Level))
                    paragraph.Numbered = true;
                else
                    paragraph.Bulleted = true;
                paragraph.LeftIndent = 0;
                paragraph.FirstLineIndent = 0;
            }

            var paragraphFormat = TextFormat(styleName, new Segment(""));
            var current = paragraph;
            Inline(p.Nodes(), paragraphFormat, ref current);
            current.RemoveTrailingSpaces();
            content.Paragraphs.Add(current);
        }

        private int? LevelFromStyle(string? name)
        {
            for (int laps = 0; name is not null && styles.TryGetValue(name, out var style) && laps < 20; laps++)
            {
                if (style.HeadingLevel > 0)
                    return style.HeadingLevel;
                name = style.Parent;
            }
            return null;
        }

        private string? ListStyleOf(string? name)
        {
            for (int laps = 0; name is not null && styles.TryGetValue(name, out var style) && laps < 20; laps++)
            {
                if (style.ListStyle is not null)
                    return style.ListStyle;
                name = style.Parent;
            }
            return null;
        }

        /// <summary>
        /// Alignment, indents, spacing, line spacing and page break: from direct formatting (automatic styles) and,
        /// for alignment, also from named styles like "Centrato".
        /// </summary>
        private void ParagraphProperties(string? name, Paragraph paragraph)
        {
            var chain = Chain(name).ToList();
            string? Value(XNamespace ns, string attribute, bool onlyAutomatic = true) =>
                chain.Where(s => !onlyAutomatic || s.Automatic).Select(s => s.ParagraphProperties.GetValueOrDefault(Key(ns, attribute))).FirstOrDefault(v => v is not null);

            paragraph.Alignment = Value(Fo, "text-align", onlyAutomatic: false) switch
            {
                "center" => Alignment.Center,
                "end" or "right" => Alignment.Right,
                "justify" => Alignment.Justified,
                _ => Alignment.Left,
            };
            paragraph.LeftIndent = Points(Value(Fo, "margin-left")) ?? 0;
            paragraph.RightIndent = Points(Value(Fo, "margin-right")) ?? 0;
            paragraph.FirstLineIndent = Points(Value(Fo, "text-indent")) ?? 0;
            paragraph.SpaceBefore = Points(Value(Fo, "margin-top")) ?? 0;
            paragraph.SpaceAfter = Points(Value(Fo, "margin-bottom")) ?? 0;
            if (Value(Fo, "line-height") is string lineSpacing && lineSpacing.EndsWith('%')
                && float.TryParse(lineSpacing[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out float percent) && percent > 0)
                paragraph.LineSpacing = percent / 100f;
            if (Value(Fo, "break-before", onlyAutomatic: false) == "page")
                paragraph.PageBreak = true;
            if (Value(Fo, "break-after", onlyAutomatic: false) == "page")
                _pendingBreak = true;
        }

        /// <summary>The style and its parents, nearest first.</summary>
        private IEnumerable<OdtStyle> Chain(string? name)
        {
            for (int laps = 0; name is not null && styles.TryGetValue(name, out var style) && laps < 20; laps++)
            {
                yield return style;
                name = style.Parent;
            }
        }

        /// <summary>
        /// The character formatting according to the style, on top of the given one. From named paragraph styles
        /// only bold, italic and the like are taken, not font and size: Eleu decides those.
        /// </summary>
        private Segment TextFormat(string? name, Segment base_)
        {
            var chain = Chain(name).Reverse().ToList(); // from the farthest: the nearest wins
            var f = base_;
            foreach (var style in chain)
            {
                bool complete = style.Automatic || style.Family != "paragraph";
                var t = style.TextProperties;
                if (t.GetValueOrDefault(Key(Fo, "font-weight")) is string weight)
                    f = f with { Bold = weight == "bold" || (int.TryParse(weight, out int n) && n >= 600) };
                if (t.GetValueOrDefault(Key(Fo, "font-style")) is string italic)
                    f = f with { Italic = italic is "italic" or "oblique" };
                if (t.GetValueOrDefault(Key(Style, "text-underline-style")) is string underline)
                    f = f with { Underline = underline != "none" };
                if (t.GetValueOrDefault(Key(Style, "text-line-through-style")) is string strikethrough)
                    f = f with { Strikethrough = strikethrough != "none" };
                if (t.GetValueOrDefault(Key(Style, "text-position")) is string position)
                    f = f with { Position = PositionFrom(position) };
                if (!complete)
                    continue;
                if (Points(t.GetValueOrDefault(Key(Fo, "font-size"))) is float size)
                    f = f with { Size = size };
                if ((t.GetValueOrDefault(Key(Style, "font-name")) ?? t.GetValueOrDefault(Key(Fo, "font-family"))) is string character)
                    f = f with { Font = character.Trim('\'', '"') };
                if (t.GetValueOrDefault(Key(Fo, "color")) is string color)
                    f = f with { Color = Color(color) is int c && c != 0 ? c : null };
                if (t.GetValueOrDefault(Key(Fo, "background-color")) is string background)
                    f = f with { Background = Color(background) };
                if (Points(t.GetValueOrDefault(Key(Fo, "letter-spacing"))) is float spacing)
                    f = f with { Spacing = spacing };
            }
            return f;
        }

        private static TextPosition PositionFrom(string @value)
        {
            var first = @value.Trim().Split(' ')[0];
            if (first == "super")
                return TextPosition.Superscript;
            if (first == "sub")
                return TextPosition.Subscript;
            return float.TryParse(first.TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out float v) && v != 0
                ? (v > 0 ? TextPosition.Superscript : TextPosition.Subscript)
                : TextPosition.Normal;
        }

        /// <summary>The content of a paragraph: text, formatting, line breaks, footnotes, bookmarks, comments, images.</summary>
        private void Inline(IEnumerable<XNode> nodes, Segment format, ref Paragraph paragraph)
        {
            foreach (var node in nodes)
            {
                if (node is XText t)
                {
                    // In ODF consecutive spaces count as one; extra ones are text:s.
                    var text = t.Value.Replace('\n', ' ').Replace('\r', ' ').Replace('\t', ' ');
                    var compact = new StringBuilder();
                    var soFar = paragraph.Text;
                    bool afterSpace = soFar.Length == 0 || soFar[^1] == ' ';
                    foreach (var c in text)
                    {
                        if (c == ' ' && afterSpace)
                            continue;
                        compact.Append(c);
                        afterSpace = c == ' ';
                    }
                    paragraph.Add(format with { Text = compact.ToString() });
                    continue;
                }
                if (node is not XElement e)
                    continue;

                if (e.Name == Text + "span")
                    Inline(e.Nodes(), TextFormat((string?)e.Attribute(Text + "style-name"), format), ref paragraph);
                else if (e.Name == Text + "s")
                    paragraph.Add(format with { Text = new string(' ', Math.Clamp((int?)e.Attribute(Text + "c") ?? 1, 1, 1000)) });
                else if (e.Name == Text + "tab")
                    paragraph.Add(format with { Text = "\t" });
                else if (e.Name == Text + "line-break")
                {
                    paragraph.RemoveTrailingSpaces();
                    content.Paragraphs.Add(paragraph);
                    var @new = paragraph.CopyProperties();
                    @new.Cell = paragraph.Cell;
                    paragraph = @new;
                }
                else if (e.Name == Text + "note")
                {
                    if ((string?)e.Attribute(Text + "note-class") == "endnote")
                        _closingNotes = true;
                    if (content.Footnotes.Count < FootnoteMarkers.Max)
                    {
                        var footnoteBody = e.Element(Text + "note-body");
                        content.Footnotes.Add(string.Join("\n", footnoteBody?.Descendants().Where(x => x.Name == Text + "p" || x.Name == Text + "h")
                            .Select(PlainText) ?? []).Trim());
                        paragraph.Add(new Segment(FootnoteMarkers.Placeholder(content.Footnotes.Count - 1).ToString()));
                    }
                }
                else if (e.Name == Text + "bookmark" || e.Name == Text + "bookmark-start")
                {
                    if ((string?)e.Attribute(Text + "name") is string name && !Bookmarks.Hidden(name) && content.Bookmarks.Count < Bookmarks.Max)
                    {
                        content.Bookmarks.Add(name.Trim());
                        paragraph.Add(new Segment(Bookmarks.Placeholder(content.Bookmarks.Count - 1).ToString()));
                    }
                }
                else if (e.Name == Office + "annotation")
                    Comment(e, paragraph);
                else if (e.Name == Office + "annotation-end")
                {
                    if ((string?)e.Attribute(Office + "name") is string name && _openComments.Remove(name, out int index))
                        paragraph.Add(new Segment(CommentMarkers.End(index).ToString()));
                }
                else if (e.Name == Draw + "frame" || e.Name == Draw + "a")
                    Frame(e, paragraph);
                else if (e.Name == Text + "note-citation" || e.Name == Text + "number" || e.Name == Text + "bookmark-end" || e.Name == Text + "soft-page-break"
                         || e.Name == Text + "reference-mark-start" || e.Name == Text + "reference-mark-end")
                    continue;
                else
                    // Links, fields and everything else: what counts is the text they show.
                    Inline(e.Nodes(), format, ref paragraph);
            }
        }

        /// <summary>
        /// A comment: if it has a name and its end comes later, it is on the text in between; otherwise at a point.
        /// </summary>
        private void Comment(XElement annotation, Paragraph paragraph)
        {
            if (content.Comments.Count >= CommentMarkers.Max)
                return;
            var text = string.Join("\n", annotation.Elements().Where(x => x.Name == Text + "p" || x.Name == Text + "h").Select(PlainText)).Trim();
            var author = annotation.Element(Dc + "creator")?.Value.Trim() ?? "";
            DateTime? date = DateTime.TryParse(annotation.Element(Dc + "date")?.Value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var d) ? d : null;
            content.Comments.Add(new Comment(text, author, date));
            int index = content.Comments.Count - 1;
            paragraph.Add(new Segment(CommentMarkers.Start(index).ToString()));
            var name = (string?)annotation.Attribute(Office + "name");
            bool hasEnd = name is not null && commentEnds.Contains(name) && !_openComments.ContainsKey(name);
            if (hasEnd)
                _openComments[name!] = index;
            else
                paragraph.Add(new Segment(CommentMarkers.End(index).ToString()));
        }

        /// <summary>A frame: if it contains an image Eleu can read, it becomes an image with alternative text.</summary>
        private void Frame(XElement frame, Paragraph paragraph)
        {
            if (frame.Name == Draw + "a")
            {
                foreach (var inner in frame.Elements(Draw + "frame"))
                    Frame(inner, paragraph);
                return;
            }
            if (frame.Element(Draw + "text-box") is not null)
            {
                _textBoxes = true;
                return;
            }
            var href = (string?)frame.Element(Draw + "image")?.Attribute(XLink + "href");
            var entry = href is null ? null : zip.GetEntry(href.TrimStart('.', '/'));
            if (entry is null || content.Images.Count >= Images.Max)
            {
                _lostImages = true;
                return;
            }
            var heading = frame.Element(Svg + "title")?.Value.Trim() ?? "";
            var description = frame.Element(Svg + "desc")?.Value.Trim() ?? "";
            try
            {
                using var stream = entry.Open();
                using var copy = new MemoryStream();
                stream.CopyTo(copy);
                var image = Images.FromData(copy.ToArray(), heading.Length > 0 ? heading : description);
                if (Points((string?)frame.Attribute(Svg + "width")) is float width && Points((string?)frame.Attribute(Svg + "height")) is float height
                    && width > 0 && height > 0)
                    image = image with { Width = width, Height = height };
                content.Images.Add(image);
                paragraph.Add(new Segment(Images.Placeholder(content.Images.Count - 1).ToString()));
            }
            catch (Exception ex) when (ex is ArgumentException or System.Runtime.InteropServices.ExternalException or OutOfMemoryException or InvalidDataException)
            {
                _lostImages = true;
            }
        }

        /// <summary>The text of a footnote or comment paragraph, with spaces and tabs.</summary>
        private static string PlainText(XElement p)
        {
            var text = new StringBuilder();
            foreach (var n in p.DescendantNodes())
            {
                if (n is XText t && t.Parent?.Name != Text + "note-citation")
                    text.Append(t.Value);
                else if (n is XElement e && e.Name == Text + "s")
                    text.Append(' ', (int?)e.Attribute(Text + "c") ?? 1);
                else if (n is XElement e2 && e2.Name == Text + "tab")
                    text.Append('\t');
            }
            return text.ToString().Trim();
        }
    }
}
