using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Eleu.Documents;

// Writing of OpenDocument documents (.odt): content.xml with the text and direct formatting (automatic styles),
// styles.xml with heading styles, page, header and footer, meta.xml with the properties.
public static partial class OdtFormat
{
    private static readonly XNamespace Loext = "urn:org:documentfoundation:names:experimental:office:xmlns:loext:1.0";

    public static void Write(DocumentContent content, Stream stream, string character = "Calibri", float baseSize = DocumentContent.BaseSize)
    {
        var writer = new Writer(content, character, baseSize);
        var body = writer.Body();

        using var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);
        // The first file must be "mimetype", uncompressed: that is how programs recognize the format.
        var kind = zip.CreateEntry("mimetype", CompressionLevel.NoCompression);
        using (var s = kind.Open())
            s.Write(Encoding.ASCII.GetBytes(MimeType));
        Save(zip, "content.xml", writer.Content(body));
        Save(zip, "styles.xml", writer.Styles());
        Save(zip, "meta.xml", Metadata(content.Metadata));
        foreach (var (path, data, _) in writer.Figures)
        {
            var entry = zip.CreateEntry(path, CompressionLevel.NoCompression);
            using var s = entry.Open();
            s.Write(data);
        }
        Save(zip, "META-INF/manifest.xml", ManifestXml(writer.Figures.Select(f => (f.Path, f.Kind))));
    }

    private static void Save(ZipArchive zip, string name, XDocument xml)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var s = entry.Open();
        using var w = XmlWriter.Create(s, new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = false });
        xml.Save(w);
    }

    /// <summary>The xmlns attributes for all the prefixes used, to put on the root element.</summary>
    private static IEnumerable<XAttribute> Spaces() =>
    [
        new(XNamespace.Xmlns + "office", Office), new(XNamespace.Xmlns + "style", Style), new(XNamespace.Xmlns + "text", Text),
        new(XNamespace.Xmlns + "table", Table), new(XNamespace.Xmlns + "draw", Draw), new(XNamespace.Xmlns + "fo", Fo),
        new(XNamespace.Xmlns + "xlink", XLink), new(XNamespace.Xmlns + "dc", Dc), new(XNamespace.Xmlns + "meta", Meta),
        new(XNamespace.Xmlns + "svg", Svg), new(XNamespace.Xmlns + "loext", Loext),
    ];

    private static string Pt(float points) => points.ToString("0.###", CultureInfo.InvariantCulture) + "pt";

    private static string Hex(int color) => "#" + (color & 0xFFFFFF).ToString("x6", CultureInfo.InvariantCulture);

    /// <summary>The text without the characters that cannot appear in XML.</summary>
    private static string Cleaned(string text) =>
        text.All(XmlConvert.IsXmlChar) ? text : string.Concat(text.Where(XmlConvert.IsXmlChar));

    private static XDocument Metadata(Metadata m)
    {
        var meta = new XElement(Office + "meta", new XElement(Meta + "generator", "Eleu"));
        if (m.Title.Length > 0)
            meta.Add(new XElement(Dc + "title", Cleaned(m.Title)));
        if (m.Subject.Length > 0)
            meta.Add(new XElement(Dc + "subject", Cleaned(m.Subject)));
        foreach (var word in m.Keywords.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            meta.Add(new XElement(Meta + "keyword", Cleaned(word)));
        if (m.Author.Length > 0)
        {
            meta.Add(new XElement(Meta + "initial-creator", Cleaned(m.Author)));
            meta.Add(new XElement(Dc + "creator", Cleaned(m.Author)));
        }
        meta.Add(new XElement(Meta + "creation-date", (m.Created ?? DateTime.Now).ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture)));
        meta.Add(new XElement(Dc + "date", DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture)));
        meta.Add(new XElement(Dc + "language", "it-IT"));
        return new XDocument(new XElement(Office + "document-meta", Spaces(), new XAttribute(Office + "version", "1.3"), meta));
    }

    private static XDocument ManifestXml(IEnumerable<(string Path, string Kind)> figures)
    {
        XElement FileEntry(string path, string kind) =>
            new(Manifest + "file-entry", new XAttribute(Manifest + "full-path", path), new XAttribute(Manifest + "media-type", kind));
        var root = new XElement(Manifest + "manifest", new XAttribute(XNamespace.Xmlns + "manifest", Manifest), new XAttribute(Manifest + "version", "1.3"),
            new XElement(Manifest + "file-entry", new XAttribute(Manifest + "full-path", "/"), new XAttribute(Manifest + "version", "1.3"),
                new XAttribute(Manifest + "media-type", MimeType)),
            FileEntry("content.xml", "text/xml"), FileEntry("styles.xml", "text/xml"), FileEntry("meta.xml", "text/xml"));
        foreach (var (path, kind) in figures)
            root.Add(FileEntry(path, kind));
        return new XDocument(root);
    }

    /// <summary>Builds content.xml and styles.xml, collecting automatic styles and images along the way.</summary>
    private sealed class Writer(DocumentContent content, string character, float baseSize)
    {
        private readonly Dictionary<string, string> _paragraphStyles = [];   // properties → name (P1, P2...)
        private readonly Dictionary<Segment, string> _textStyles = [];     // format → name (T1, T2...)
        private readonly List<XElement> _automatic = [];
        private readonly HashSet<string> _characters = [character];
        private readonly HashSet<string> _bookmarks = new(StringComparer.Ordinal);
        private readonly Dictionary<int, (bool HasEnd, bool AtAPoint)> _comments = [];
        private int _footnotes;

        public List<(string Path, byte[] Data, string Kind)> Figures { get; } = [];

        public XElement Body()
        {
            PrepareComments();
            var text = new XElement(Office + "text");
            int i = 0;
            var paragraphs = content.Paragraphs;
            while (i < paragraphs.Count)
            {
                if (paragraphs[i].Cell is { } cell)
                {
                    int end = i;
                    while (end < paragraphs.Count && paragraphs[end].Cell?.Table == cell.Table)
                        end++;
                    text.Add(WriteTable(cell.Table, paragraphs.Skip(i).Take(end - i).ToList()));
                    i = end;
                }
                else
                {
                    int end = i;
                    while (end < paragraphs.Count && paragraphs[end].Cell is null)
                        end++;
                    Blocks(text, paragraphs.Skip(i).Take(end - i).ToList());
                    i = end;
                }
            }
            if (!text.HasElements)
                text.Add(new XElement(Text + "p"));
            return text;
        }

        /// <summary>
        /// Which comments have a start and an end (written with name and end), and which are at a point (start and end
        /// next to each other: a comment without a range).
        /// </summary>
        private void PrepareComments()
        {
            var all = string.Join('\n', content.Paragraphs.Select(p => p.Text));
            for (int i = 0; i < content.Comments.Count; i++)
            {
                int start = all.IndexOf(CommentMarkers.Start(i)), end = all.IndexOf(CommentMarkers.End(i));
                if (start < 0)
                    continue;
                bool hasEnd = end > start;
                _comments[i] = (hasEnd && end > start + 1, hasEnd && end == start + 1);
            }
        }

        public XDocument Content(XElement body)
        {
            var root = new XElement(Office + "document-content", Spaces(), new XAttribute(Office + "version", "1.3"),
                Characters(),
                new XElement(Office + "automatic-styles", ListStyles(), FrameStyle(), _automatic),
                new XElement(Office + "body", body));
            return new XDocument(root);
        }

        private XElement Characters() => new(Office + "font-face-decls",
            _characters.Order().Select(c => new XElement(Style + "font-face", new XAttribute(Style + "name", c),
                new XAttribute(Svg + "font-family", c.Contains(' ') ? $"'{c}'" : c))));

        private static IEnumerable<XElement> ListStyles()
        {
            // L1 bulleted, L2 numbered, on all levels.
            var bulleted = new XElement(Text + "list-style", new XAttribute(Style + "name", "L1"));
            var numbered = new XElement(Text + "list-style", new XAttribute(Style + "name", "L2"));
            for (int level = 1; level <= 10; level++)
            {
                var indent = new XElement(Style + "list-level-properties",
                    new XAttribute(Text + "list-level-position-and-space-mode", "label-alignment"),
                    new XElement(Style + "list-level-label-alignment", new XAttribute(Text + "label-followed-by", "listtab"),
                        new XAttribute(Fo + "text-indent", "-0.635cm"), new XAttribute(Fo + "margin-left", $"{(0.635 * (level + 1)).ToString("0.###", CultureInfo.InvariantCulture)}cm")));
                bulleted.Add(new XElement(Text + "list-level-style-bullet", new XAttribute(Text + "level", level),
                    new XAttribute(Text + "bullet-char", "•"), new XElement(indent)));
                numbered.Add(new XElement(Text + "list-level-style-number", new XAttribute(Text + "level", level),
                    new XAttribute(Style + "num-suffix", "."), new XAttribute(Style + "num-format", "1"), new XElement(indent)));
            }
            return [bulleted, numbered];
        }

        private static XElement FrameStyle() => new(Style + "style", new XAttribute(Style + "name", "fr1"), new XAttribute(Style + "family", "graphic"),
            new XElement(Style + "graphic-properties", new XAttribute(Style + "vertical-pos", "top"), new XAttribute(Style + "vertical-rel", "baseline")));

        /// <summary>Paragraphs outside tables or inside a cell: those in a list go in one text:list per group.</summary>
        private void Blocks(XElement container, List<Paragraph> paragraphs)
        {
            int i = 0;
            while (i < paragraphs.Count)
            {
                var p = paragraphs[i];
                if ((p.Bulleted || p.Numbered) && p.HeadingLevel == 0)
                {
                    var list = new XElement(Text + "list", new XAttribute(Text + "style-name", p.Numbered ? "L2" : "L1"));
                    while (i < paragraphs.Count && paragraphs[i].HeadingLevel == 0 && paragraphs[i].Numbered == p.Numbered
                           && paragraphs[i].Bulleted == p.Bulleted && !(i > 0 && paragraphs[i].PageBreak && list.HasElements))
                    {
                        list.Add(new XElement(Text + "list-item", Paragraph(paragraphs[i])));
                        i++;
                    }
                    container.Add(list);
                    continue;
                }
                container.Add(Paragraph(p));
                i++;
            }
        }

        private XElement WriteTable(int number, List<Paragraph> paragraphs)
        {
            var cells = paragraphs.Select(p => p.Cell!).ToList();
            int columns = cells.Max(c => c.Column) + 1;
            var widths = Enumerable.Range(0, columns)
                .Select(c => cells.Where(x => x.Column == c).Select(x => x.Width).FirstOrDefault(l => l > 0))
                .Select(l => l > 0 ? l : 113.4f) // 4 cm, as in Word
                .ToList();

            string name = $"Tabella{number + 1}";
            _automatic.Add(new XElement(Style + "style", new XAttribute(Style + "name", name), new XAttribute(Style + "family", "table"),
                new XElement(Style + "table-properties", new XAttribute(Style + "width", Pt(widths.Sum())), new XAttribute(Table + "align", "left"))));
            var table = new XElement(Table + "table", new XAttribute(Table + "name", name), new XAttribute(Table + "style-name", name));
            for (int c = 0; c < columns; c++)
            {
                var columnStyle = $"{name}.C{c + 1}";
                _automatic.Add(new XElement(Style + "style", new XAttribute(Style + "name", columnStyle), new XAttribute(Style + "family", "table-column"),
                    new XElement(Style + "table-column-properties", new XAttribute(Style + "column-width", Pt(widths[c])))));
                table.Add(new XElement(Table + "table-column", new XAttribute(Table + "style-name", columnStyle)));
            }
            if (!_automatic.Any(s => (string?)s.Attribute(Style + "name") == "Cella"))
                _automatic.Add(new XElement(Style + "style", new XAttribute(Style + "name", "Cella"), new XAttribute(Style + "family", "table-cell"),
                    new XElement(Style + "table-cell-properties", new XAttribute(Fo + "padding", "0.1cm"), new XAttribute(Fo + "border", "0.5pt solid #000000"))));

            XElement? header = null;
            foreach (var row in paragraphs.GroupBy(p => p.Cell!.Row).OrderBy(g => g.Key))
            {
                var rowElement = new XElement(Table + "table-row");
                for (int c = 0; c < columns; c++)
                {
                    var cell = new XElement(Table + "table-cell", new XAttribute(Table + "style-name", "Cella"), new XAttribute(Office + "value-type", "string"));
                    var inside = row.Where(p => p.Cell!.Column == c).ToList();
                    if (inside.Count == 0)
                        cell.Add(new XElement(Text + "p"));
                    else
                        Blocks(cell, inside);
                    rowElement.Add(cell);
                }
                if (row.First().Cell!.Header && (header is not null || table.Elements(Table + "table-row").Any() == false))
                {
                    header ??= new XElement(Table + "table-header-rows");
                    if (header.Parent is null)
                        table.Add(header);
                    header.Add(rowElement);
                }
                else
                    table.Add(rowElement);
            }
            return table;
        }

        private XElement Paragraph(Paragraph p)
        {
            bool heading = p.HeadingLevel > 0;
            var element = new XElement(Text + (heading ? "h" : "p"));
            element.Add(new XAttribute(Text + "style-name", ParagraphStyle(p)));
            if (heading)
                element.Add(new XAttribute(Text + "outline-level", p.HeadingLevel));
            bool afterSpace = true; // at the start of a paragraph a space must be written as text:s
            foreach (var segment in p.Segments)
                Segment(element, segment, ref afterSpace);
            return element;
        }

        private string ParagraphStyle(Paragraph p)
        {
            var parent = p.HeadingLevel > 0 ? $"Heading_20_{p.HeadingLevel}" : "Standard";
            var properties = new List<XAttribute>();
            if (p.Alignment != Alignment.Left)
                properties.Add(new XAttribute(Fo + "text-align", p.Alignment switch
                {
                    Alignment.Center => "center",
                    Alignment.Right => "end",
                    _ => "justify",
                }));
            if (p.LeftIndent != 0)
                properties.Add(new XAttribute(Fo + "margin-left", Pt(p.LeftIndent)));
            if (p.RightIndent != 0)
                properties.Add(new XAttribute(Fo + "margin-right", Pt(p.RightIndent)));
            if (p.FirstLineIndent != 0)
                properties.Add(new XAttribute(Fo + "text-indent", Pt(p.FirstLineIndent)));
            if (p.SpaceBefore != 0)
                properties.Add(new XAttribute(Fo + "margin-top", Pt(p.SpaceBefore)));
            if (p.SpaceAfter != 0)
                properties.Add(new XAttribute(Fo + "margin-bottom", Pt(p.SpaceAfter)));
            if (p.LineSpacing != 1f)
                properties.Add(new XAttribute(Fo + "line-height", $"{(p.LineSpacing * 100).ToString("0", CultureInfo.InvariantCulture)}%"));
            if (p.PageBreak)
                properties.Add(new XAttribute(Fo + "break-before", "page"));
            if (properties.Count == 0)
                return parent;

            var key = parent + "|" + string.Join("|", properties.Select(a => a.ToString()));
            if (_paragraphStyles.TryGetValue(key, out var name))
                return name;
            name = $"P{_paragraphStyles.Count + 1}";
            _paragraphStyles[key] = name;
            _automatic.Add(new XElement(Style + "style", new XAttribute(Style + "name", name), new XAttribute(Style + "family", "paragraph"),
                new XAttribute(Style + "parent-style-name", parent), new XElement(Style + "paragraph-properties", properties)));
            return name;
        }

        private string? TextStyle(Segment s)
        {
            var format = s with { Text = "" };
            if (format == new Segment(""))
                return null;
            if (_textStyles.TryGetValue(format, out var name))
                return name;
            var properties = new XElement(Style + "text-properties");
            if (s.Font is string c)
            {
                _characters.Add(c);
                properties.Add(new XAttribute(Style + "font-name", c));
            }
            if (s.Size is float d)
                properties.Add(new XAttribute(Fo + "font-size", Pt(d)));
            if (s.Bold)
                properties.Add(new XAttribute(Fo + "font-weight", "bold"));
            if (s.Italic)
                properties.Add(new XAttribute(Fo + "font-style", "italic"));
            if (s.Underline)
                properties.Add(new XAttribute(Style + "text-underline-style", "solid"), new XAttribute(Style + "text-underline-width", "auto"),
                    new XAttribute(Style + "text-underline-color", "font-color"));
            if (s.Strikethrough)
                properties.Add(new XAttribute(Style + "text-line-through-style", "solid"));
            if (s.Position != TextPosition.Normal)
                properties.Add(new XAttribute(Style + "text-position", s.Position == TextPosition.Superscript ? "super 58%" : "sub 58%"));
            if (s.Color is int color)
                properties.Add(new XAttribute(Fo + "color", Hex(color)));
            if (s.Background is int background)
                properties.Add(new XAttribute(Fo + "background-color", Hex(background)));
            if (s.Spacing != 0)
                properties.Add(new XAttribute(Fo + "letter-spacing", Pt(s.Spacing)));
            name = $"T{_textStyles.Count + 1}";
            _textStyles[format] = name;
            _automatic.Add(new XElement(Style + "style", new XAttribute(Style + "name", name), new XAttribute(Style + "family", "text"), properties));
            return name;
        }

        /// <summary>The text of a segment, with placeholders in the right place and spaces as ODF wants them.</summary>
        private void Segment(XElement paragraph, Segment segment, ref bool afterSpace)
        {
            var style = TextStyle(segment);
            XElement? span = null;
            XElement Target()
            {
                if (style is null)
                    return paragraph;
                if (span is null)
                {
                    span = new XElement(Text + "span", new XAttribute(Text + "style-name", style));
                    paragraph.Add(span);
                }
                return span;
            }

            var text = new StringBuilder();
            void Clear()
            {
                if (text.Length > 0)
                    Target().Add(text.ToString());
                text.Clear();
            }

            int i = 0;
            var t = segment.Text;
            while (i < t.Length)
            {
                char c = t[i];
                if (c == ' ')
                {
                    int n = 0;
                    while (i + n < t.Length && t[i + n] == ' ')
                        n++;
                    i += n;
                    // The first space after a normal character is text; the others (and those at the start of a paragraph) are text:s.
                    if (!afterSpace)
                    {
                        text.Append(' ');
                        n--;
                    }
                    if (n > 0)
                    {
                        Clear();
                        var spaces = new XElement(Text + "s");
                        if (n > 1)
                            spaces.Add(new XAttribute(Text + "c", n));
                        Target().Add(spaces);
                    }
                    afterSpace = true;
                    continue;
                }
                if (c == '\t')
                {
                    Clear();
                    Target().Add(new XElement(Text + "tab"));
                    afterSpace = false;
                    i++;
                    continue;
                }
                if (c is >= '' and <= '')
                {
                    Clear();
                    if (Element(c) is { } element)
                    {
                        paragraph.Add(element);
                        span = null; // the text after goes into another span
                        // Footnotes and images are visible; bookmarks and comments are not, and do not separate spaces.
                        if (element.Name == Text + "note" || element.Name == Draw + "frame")
                            afterSpace = false;
                    }
                    i++;
                    continue;
                }
                if (XmlConvert.IsXmlChar(c))
                    text.Append(c);
                afterSpace = false;
                i++;
            }
            Clear();
        }

        /// <summary>The ODF element of a placeholder: bookmark, footnote, image or comment start/end.</summary>
        private XElement? Element(char c)
        {
            if (Bookmarks.Index(c) is var s and >= 0 && s < content.Bookmarks.Count)
            {
                var name = content.Bookmarks[s];
                var single = name;
                for (int n = 2; !_bookmarks.Add(single); n++)
                    single = $"{name} {n}";
                return new XElement(Text + "bookmark", new XAttribute(Text + "name", Cleaned(single)));
            }
            if (FootnoteMarkers.Index(c) is var footnote and >= 0 && footnote < content.Footnotes.Count)
            {
                _footnotes++;
                var body = new XElement(Text + "note-body",
                    content.Footnotes[footnote].ReplaceLineEndings("\n").Split('\n').Select(r => new XElement(Text + "p",
                        new XAttribute(Text + "style-name", "Footnote"), Cleaned(r))));
                return new XElement(Text + "note", new XAttribute(Text + "id", $"ftn{_footnotes}"), new XAttribute(Text + "note-class", "footnote"),
                    new XElement(Text + "note-citation", _footnotes), body);
            }
            if (Images.Index(c) is var m and >= 0 && m < content.Images.Count)
                return Frame(content.Images[m], m + 1);
            if (CommentMarkers.StartIndex(c) is var ci and >= 0 && ci < content.Comments.Count && _comments.TryGetValue(ci, out var state))
            {
                var comment = content.Comments[ci];
                var annotation = new XElement(Office + "annotation");
                if (state.HasEnd)
                    annotation.Add(new XAttribute(Office + "name", $"__Annotation__{ci + 1}"));
                if (comment.Author.Length > 0)
                    annotation.Add(new XElement(Dc + "creator", Cleaned(comment.Author)));
                if (comment.Date is DateTime date)
                    annotation.Add(new XElement(Dc + "date", date.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture)));
                annotation.Add(comment.Text.ReplaceLineEndings("\n").Split('\n').Select(r => new XElement(Text + "p", Cleaned(r))));
                return annotation;
            }
            if (CommentMarkers.EndIndex(c) is var cf and >= 0 && _comments.TryGetValue(cf, out var end) && end.HasEnd)
                return new XElement(Office + "annotation-end", new XAttribute(Office + "name", $"__Annotation__{cf + 1}"));
            return null;
        }

        private XElement Frame(EmbeddedImage image, int number)
        {
            var extension = image.Format == "jpeg" ? "jpg" : "png";
            var kind = image.Format == "jpeg" ? "image/jpeg" : "image/png";
            var path = $"Pictures/immagine{number}.{extension}";
            if (!Figures.Any(f => f.Path == path))
                Figures.Add((path, image.Data, kind));
            var frame = new XElement(Draw + "frame", new XAttribute(Draw + "style-name", "fr1"), new XAttribute(Draw + "name", $"Immagine {number}"),
                new XAttribute(Text + "anchor-type", "as-char"), new XAttribute(Svg + "width", Pt(image.Width)),
                new XAttribute(Svg + "height", Pt(image.Height)), new XAttribute(Draw + "z-index", 0),
                new XElement(Draw + "image", new XAttribute(XLink + "href", path), new XAttribute(XLink + "type", "simple"),
                    new XAttribute(XLink + "show", "embed"), new XAttribute(XLink + "actuate", "onLoad"), new XAttribute(Draw + "mime-type", kind)));
            if (image.Description.Length > 0)
                frame.Add(new XElement(Svg + "title", Cleaned(image.Description)), new XElement(Svg + "desc", Cleaned(image.Description)));
            return frame;
        }

        // ---------- styles.xml ----------

        public XDocument Styles()
        {
            var styles = new XElement(Office + "styles",
                new XElement(Style + "default-style", new XAttribute(Style + "family", "paragraph"),
                    new XElement(Style + "paragraph-properties", new XAttribute(Style + "writing-mode", "lr-tb")),
                    new XElement(Style + "text-properties", new XAttribute(Style + "font-name", character), new XAttribute(Fo + "font-size", Pt(baseSize)),
                        new XAttribute(Fo + "language", "it"), new XAttribute(Fo + "country", "IT"))),
                new XElement(Style + "style", new XAttribute(Style + "name", "Standard"), new XAttribute(Style + "family", "paragraph"),
                    new XAttribute(Style + "class", "text")));
            for (int level = 1; level <= 9; level++)
                styles.Add(new XElement(Style + "style", new XAttribute(Style + "name", $"Heading_20_{level}"),
                    new XAttribute(Style + "display-name", $"Heading {level}"), new XAttribute(Style + "family", "paragraph"),
                    new XAttribute(Style + "parent-style-name", "Standard"), new XAttribute(Style + "next-style-name", "Standard"),
                    new XAttribute(Style + "default-outline-level", level), new XAttribute(Style + "class", "text"),
                    new XElement(Style + "paragraph-properties", new XAttribute(Fo + "margin-top", "12pt"), new XAttribute(Fo + "margin-bottom", "6pt"),
                        new XAttribute(Fo + "keep-with-next", "always")),
                    new XElement(Style + "text-properties", new XAttribute(Fo + "font-size", Pt(DocumentContent.HeadingSize(level))),
                        new XAttribute(Fo + "font-weight", "bold"))));
            foreach (var (name, size) in new[] { ("Footnote", baseSize - 2), ("Header", baseSize - 1), ("Footer", baseSize - 1) })
                styles.Add(new XElement(Style + "style", new XAttribute(Style + "name", name), new XAttribute(Style + "family", "paragraph"),
                    new XAttribute(Style + "parent-style-name", "Standard"), new XAttribute(Style + "class", "extra"),
                    new XElement(Style + "text-properties", new XAttribute(Fo + "font-size", Pt(size)))));
            var schema = new XElement(Text + "outline-style", new XAttribute(Style + "name", "Outline"));
            for (int level = 1; level <= 10; level++)
                schema.Add(new XElement(Text + "outline-level-style", new XAttribute(Text + "level", level), new XAttribute(Style + "num-format", "")));
            styles.Add(schema);
            styles.Add(new XElement(Text + "notes-configuration", new XAttribute(Text + "note-class", "footnote"), new XAttribute(Style + "num-format", "1"),
                new XAttribute(Text + "start-value", 0), new XAttribute(Text + "footnotes-position", "page"),
                new XAttribute(Text + "start-numbering-at", "document")));

            var page = content.Page;
            var (width, height) = page.Effective;
            bool withHeader = page.Header.Length > 0, withFooter = page.Footer.Length > 0;

            var pageProperties = new XElement(Style + "page-layout-properties",
                new XAttribute(Fo + "page-width", Pt(width)), new XAttribute(Fo + "page-height", Pt(height)),
                new XAttribute(Style + "print-orientation", page.Horizontal ? "landscape" : "portrait"),
                new XAttribute(Fo + "margin-top", Pt(withHeader ? page.HeaderDistance : page.TopMargin)),
                new XAttribute(Fo + "margin-bottom", Pt(withFooter ? page.FooterDistance : page.BottomMargin)),
                new XAttribute(Fo + "margin-left", Pt(page.LeftMargin)), new XAttribute(Fo + "margin-right", Pt(page.RightMargin)));
            var layout = new XElement(Style + "page-layout", new XAttribute(Style + "name", "pm1"), pageProperties);
            if (withHeader)
                layout.Add(new XElement(Style + "header-style", new XElement(Style + "header-footer-properties", new XAttribute(Fo + "min-height", "0pt"),
                    new XAttribute(Fo + "margin-bottom", Pt(Math.Max(2, page.TopMargin - page.HeaderDistance - ZoneLine))))));
            if (withFooter)
                layout.Add(new XElement(Style + "footer-style", new XElement(Style + "header-footer-properties", new XAttribute(Fo + "min-height", "0pt"),
                    new XAttribute(Fo + "margin-top", Pt(Math.Max(2, page.BottomMargin - page.FooterDistance - ZoneLine))))));

            var master = new XElement(Style + "master-page", new XAttribute(Style + "name", "Standard"), new XAttribute(Style + "page-layout-name", "pm1"));
            if (withHeader)
            {
                master.Add(new XElement(Style + "header", ZoneWithVariables(page.Header, "Header")));
                if (page.SkipFirstPage)
                    master.Add(new XElement(Style + "header-first", new XElement(Text + "p", new XAttribute(Text + "style-name", "Header"))));
            }
            if (withFooter)
            {
                master.Add(new XElement(Style + "footer", ZoneWithVariables(page.Footer, "Footer")));
                if (page.SkipFirstPage)
                    master.Add(new XElement(Style + "footer-first", new XElement(Text + "p", new XAttribute(Text + "style-name", "Footer"))));
            }

            return new XDocument(new XElement(Office + "document-styles", Spaces(), new XAttribute(Office + "version", "1.3"),
                Characters(), styles,
                new XElement(Office + "automatic-styles", layout),
                new XElement(Office + "master-styles", master)));
        }

        /// <summary>A header or footer paragraph, with the variables as real fields.</summary>
        private static XElement ZoneWithVariables(string model, string style)
        {
            var p = new XElement(Text + "p", new XAttribute(Text + "style-name", style));
            foreach (var (text, field) in PageLayout.Pieces(model))
            {
                p.Add(field switch
                {
                    "PAGE" => new XElement(Text + "page-number", new XAttribute(Text + "select-page", "current"), "1"),
                    "NUMPAGES" => new XElement(Text + "page-count", "1"),
                    "TITLE" => new XElement(Text + "title"),
                    "DATE" => new XElement(Text + "date", new XAttribute(Text + "fixed", "false")),
                    _ => (object)Cleaned(text),
                });
            }
            return p;
        }
    }
}
