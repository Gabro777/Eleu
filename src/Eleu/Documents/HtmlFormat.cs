using System.Globalization;
using System.Net;
using System.Text;
using HtmlAgilityPack;

namespace Eleu.Documents;

/// <summary>
/// HTML reading and writing. Writing produces an accessible page: real headings (h1-h6), lists, tables
/// with a header first row, images with alternative text, footnotes linked to the reference and back,
/// Italian language. Reading takes what Eleu keeps and the rest becomes text.
/// </summary>
public static class HtmlFormat
{
    /// <summary>How to write images: null = inside the page (data:), otherwise the file path of image n (from 0).</summary>
    /// <param name="footnotesFile">If given, the footnotes are in that file (EPUB): references point there and they are not written here.</param>
    /// <param name="firstParagraph">The number of the first paragraph in the whole document, for heading ids ("t12").</param>
    public static string Write(DocumentContent content, bool xhtml = false, Func<int, string>? imageFile = null,
        string? footnotesFile = null, int firstParagraph = 0)
    {
        var h = new StringBuilder();
        string Close(string tag) => xhtml ? $"<{tag} />" : $"<{tag}>";
        var heading = content.Metadata.Title.Length > 0 ? content.Metadata.Title
            : content.Paragraphs.FirstOrDefault(p => p.HeadingLevel > 0)?.Text.Trim() ?? "Documento";

        if (xhtml)
        {
            h.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
            h.AppendLine("<!DOCTYPE html>");
            h.AppendLine("<html xmlns=\"http://www.w3.org/1999/xhtml\" xmlns:epub=\"http://www.idpf.org/2007/ops\" lang=\"it\" xml:lang=\"it\">");
        }
        else
        {
            h.AppendLine("<!DOCTYPE html>");
            h.AppendLine("<html lang=\"it\">");
        }
        h.AppendLine("<head>");
        h.AppendLine(xhtml ? "<meta charset=\"utf-8\" />" : "<meta charset=\"utf-8\">");
        h.Append("<title>").Append(Text(heading)).AppendLine("</title>");
        if (content.Metadata.Author.Length > 0)
            h.Append("<meta name=\"author\" content=\"").Append(Attribute(content.Metadata.Author)).AppendLine(xhtml ? "\" />" : "\">");
        h.AppendLine("<style>body{font-family:'Segoe UI',sans-serif;line-height:1.5;max-width:45em;margin:auto;padding:1em}table{border-collapse:collapse}td,th{border:1px solid;padding:.3em .6em;vertical-align:top}</style>");
        h.AppendLine("</head>");
        h.AppendLine("<body>");

        var paragraphs = content.Paragraphs;
        int footnoteNumber = 0;
        for (int i = 0; i < paragraphs.Count; i++)
        {
            var p = paragraphs[i];
            if (p.Cell is { } cell)
            {
                int end = i;
                while (end < paragraphs.Count && paragraphs[end].Cell?.Table == cell.Table)
                    end++;
                WriteTable(paragraphs.Skip(i).Take(end - i).ToList());
                i = end - 1;
                continue;
            }
            if (p.Bulleted || p.Numbered)
            {
                var tag = p.Numbered ? "ol" : "ul";
                h.Append('<').Append(tag).AppendLine(">");
                while (i < paragraphs.Count && paragraphs[i].Cell is null && (p.Numbered ? paragraphs[i].Numbered : paragraphs[i].Bulleted))
                {
                    h.Append("<li").Append(Style(paragraphs[i])).Append('>');
                    Content(paragraphs[i]);
                    h.AppendLine("</li>");
                    i++;
                }
                i--;
                h.Append("</").Append(tag).AppendLine(">");
                continue;
            }
            WriteParagraph(p, i);
        }

        if (content.Footnotes.Count > 0 && footnotesFile is null)
        {
            h.AppendLine(xhtml ? "<section epub:type=\"endnotes\" role=\"doc-endnotes\">" : "<section role=\"doc-endnotes\">");
            h.AppendLine("<h2>Note</h2>");
            h.AppendLine("<ol>");
            for (int n = 0; n < content.Footnotes.Count; n++)
            {
                h.Append($"<li id=\"nota-{n + 1}\"").Append(xhtml ? " epub:type=\"endnote\"" : "").Append('>');
                h.Append(Text(content.Footnotes[n]).Replace("\n", Close("br")));
                h.Append($" <a href=\"#rif-{n + 1}\" role=\"doc-backlink\" aria-label=\"Torna al testo\">↩</a></li>").AppendLine();
            }
            h.AppendLine("</ol>");
            h.AppendLine("</section>");
        }
        h.AppendLine("</body>");
        h.AppendLine("</html>");
        return h.ToString();

        void WriteParagraph(Paragraph p, int index)
        {
            var tag = p.HeadingLevel > 0 ? $"h{Math.Min(6, p.HeadingLevel)}" : "p";
            h.Append('<').Append(tag);
            if (p.HeadingLevel > 0)
                h.Append($" id=\"t{firstParagraph + index}\"");
            h.Append(Style(p)).Append('>');
            Content(p);
            if (p.Segments.Count == 0 || p.Text.Trim().Length == 0 && !p.Text.Any(EmbeddedMarker))
                h.Append(Close("br"));
            h.Append("</").Append(tag).AppendLine(">");
        }

        void WriteTable(List<Paragraph> cells)
        {
            var grid = Grid.From(cells, cells[0].Cell!.Table);
            h.AppendLine("<table>");
            for (int r = 0; r < grid.Rows.Count; r++)
            {
                h.AppendLine("<tr>");
                foreach (var cell in grid.Rows[r])
                {
                    var tag = r == 0 ? "th" : "td";
                    h.Append('<').Append(tag).Append(r == 0 ? " scope=\"col\"" : "").Append('>');
                    for (int k = 0; k < cell.Count; k++)
                    {
                        if (cell.Count > 1)
                            h.Append("<p>");
                        Content(cell[k]);
                        if (cell.Count > 1)
                            h.Append("</p>");
                    }
                    h.Append("</").Append(tag).AppendLine(">");
                }
                h.AppendLine("</tr>");
            }
            h.AppendLine("</table>");
        }

        void Content(Paragraph p)
        {
            foreach (var s in p.Segments)
            {
                var opened = new List<string>();
                void Open(string tag, string attributes = "")
                {
                    h.Append('<').Append(tag).Append(attributes).Append('>');
                    opened.Add(tag);
                }
                if (s.Bold && p.HeadingLevel == 0)
                    Open("strong");
                if (s.Italic)
                    Open("em");
                if (s.Underline)
                    Open("u");
                if (s.Strikethrough)
                    Open("s");
                if (s.Position == TextPosition.Superscript)
                    Open("sup");
                else if (s.Position == TextPosition.Subscript)
                    Open("sub");
                var style = new List<string>();
                if (s.Color is int color)
                    style.Add($"color:#{color:X6}");
                if (s.Background is int background)
                    style.Add($"background-color:#{background:X6}");
                if (s.Size is float size)
                    style.Add(string.Create(CultureInfo.InvariantCulture, $"font-size:{size}pt"));
                if (s.Font is string character)
                    style.Add($"font-family:'{character.Replace("'", "")}'");
                if (style.Count > 0)
                    Open("span", $" style=\"{Attribute(string.Join(";", style))}\"");

                foreach (var c in s.Text)
                {
                    if (Bookmarks.Index(c) is var bookmark and >= 0 && bookmark < content.Bookmarks.Count)
                        h.Append($"<a id=\"{Attribute(Again(content.Bookmarks[bookmark]))}\"></a>");
                    else if (FootnoteMarkers.Index(c) is var footnote and >= 0 && footnote < content.Footnotes.Count)
                    {
                        footnoteNumber = footnote + 1;
                        h.Append($"<a href=\"{footnotesFile}#nota-{footnoteNumber}\" id=\"rif-{footnoteNumber}\" role=\"doc-noteref\"")
                            .Append(xhtml ? " epub:type=\"noteref\"" : "").Append($"><sup>{footnoteNumber}</sup></a>");
                    }
                    else if (Images.Index(c) is var image and >= 0 && image < content.Images.Count)
                    {
                        var img = content.Images[image];
                        var src = imageFile?.Invoke(image) ?? $"data:image/{img.Format};base64,{Convert.ToBase64String(img.Data)}";
                        h.Append($"<img src=\"{Attribute(src)}\" alt=\"{Attribute(img.Description)}\"")
                            .Append(CultureInfo.InvariantCulture, $" width=\"{Math.Round(img.Width * 96 / 72)}\" height=\"{Math.Round(img.Height * 96 / 72)}\"")
                            .Append(xhtml ? " />" : ">");
                    }
                    else if (c == '\t')
                        h.Append(' ');
                    else if (c == Editor_PageBreak)
                        continue;
                    else
                        h.Append(Text(c.ToString()));
                }
                for (int k = opened.Count - 1; k >= 0; k--)
                    h.Append("</").Append(opened[k]).Append('>');
            }
        }
    }

    private const char Editor_PageBreak = '\f';

    private static bool EmbeddedMarker(char c) => c >= 0xE000 && c <= 0xF8FF;

    private static string Style(Paragraph p)
    {
        var style = new List<string>();
        if (p.Alignment != Alignment.Left)
            style.Add("text-align:" + p.Alignment switch { Alignment.Center => "center", Alignment.Right => "right", _ => "justify" });
        if (p.LeftIndent != 0 && !p.Bulleted && !p.Numbered)
            style.Add(string.Create(CultureInfo.InvariantCulture, $"margin-left:{p.LeftIndent}pt"));
        if (p.FirstLineIndent != 0 && !p.Bulleted && !p.Numbered)
            style.Add(string.Create(CultureInfo.InvariantCulture, $"text-indent:{p.FirstLineIndent}pt"));
        if (p.PageBreak)
            style.Add("page-break-before:always");
        return style.Count == 0 ? "" : $" style=\"{Attribute(string.Join(";", style))}\"";
    }

    /// <summary>A bookmark name as an HTML identifier (without spaces).</summary>
    public static string Again(string name) => string.Concat(name.Trim().Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_'));

    private static string Text(string text) => WebUtility.HtmlEncode(text).Replace("&#39;", "'");

    private static string Attribute(string text) => WebUtility.HtmlEncode(text);

    // ---------- Reading ----------

    /// <summary>
    /// The document from HTML (XHTML too, for example the chapters of an EPUB). <paramref name="resource"/> gives the
    /// data of an image from its address, if it is not inside the page.
    /// </summary>
    public static DocumentContent Read(string html, Func<string, byte[]?>? resource = null, DocumentContent? addTo = null)
    {
        var documents = new HtmlAgilityPack.HtmlDocument { OptionFixNestedTags = true };
        documents.LoadHtml(html);
        var content = addTo ?? new DocumentContent();
        var root = documents.DocumentNode.SelectSingleNode("//body") ?? documents.DocumentNode;
        if (addTo is null)
        {
            var heading = WebUtility.HtmlDecode(documents.DocumentNode.SelectSingleNode("//title")?.InnerText ?? "").Trim();
            var author = documents.DocumentNode.SelectSingleNode("//meta[@name='author']")?.GetAttributeValue("content", "") ?? "";
            content.Metadata = new Metadata(heading, WebUtility.HtmlDecode(author).Trim());
        }
        var reader = new HtmlReader(content, resource);
        reader.PrepareFootnotes(documents.DocumentNode);
        reader.Block(root);
        reader.End();
        return content;
    }

    private sealed class HtmlReader(DocumentContent content, Func<string, byte[]?>? resource)
    {
        private Paragraph? _current;
        private int _table = content.Paragraphs.Select(p => p.Cell?.Table ?? -1).DefaultIfEmpty(-1).Max() + 1;
        private CellPosition? _cell;
        private bool _pageBreak;

        private static readonly HashSet<string> Blocks = ["p", "div", "h1", "h2", "h3", "h4", "h5", "h6", "li", "ul", "ol", "table",
            "blockquote", "pre", "section", "article", "header", "footer", "main", "nav", "aside", "figure", "figcaption", "dl", "dt", "dd",
            "address", "hr", "tr", "td", "th", "tbody", "thead", "tfoot", "caption", "body", "html"];

        private Paragraph Current()
        {
            if (_current is null)
            {
                _current = new Paragraph { Cell = _cell, PageBreak = _pageBreak };
                _pageBreak = false;
                content.Paragraphs.Add(_current);
            }
            return _current;
        }

        public void End() => ParagraphEnd();

        private void ParagraphEnd()
        {
            _current?.RemoveTrailingSpaces();
            _current = null;
        }

        /// <summary>The page's footnotes: id of the footnote text → text (without the back link).</summary>
        private Dictionary<string, string>? _footnoteTexts;

        /// <summary>The elements containing the footnotes (the section at the end): not read as text.</summary>
        private readonly HashSet<HtmlNode> _footnoteSections = [];

        public void PrepareFootnotes(HtmlNode root)
        {
            _footnoteTexts = [];
            foreach (var reference in root.Descendants("a").Where(IsNoteReference))
            {
                var id = reference.GetAttributeValue("href", "").Split('#') is [.., var fragment] ? fragment : "";
                if (id.Length == 0 || root.SelectSingleNode($"//*[@id='{id.Replace("'", "")}']") is not { } footnote)
                    continue;
                var copy = footnote.CloneNode(true);
                foreach (var back in copy.Descendants("a").Where(a => a.GetAttributeValue("role", "") == "doc-backlink" || a.InnerText.Trim() is "↩" or "↑").ToList())
                    back.Remove();
                _footnoteTexts[id] = HtmlSpaces(WebUtility.HtmlDecode(copy.InnerText)).Trim();
                var section = footnote.Ancestors().FirstOrDefault(a => a.GetAttributeValue("role", "") is "doc-endnotes" or "doc-footnotes"
                    || a.GetAttributeValue("epub:type", "").Contains("notes")) ?? footnote;
                _footnoteSections.Add(section);
            }
        }

        private static bool IsNoteReference(HtmlNode a) =>
            a.GetAttributeValue("role", "") == "doc-noteref" || a.GetAttributeValue("epub:type", "") == "noteref";

        public void Block(HtmlNode node, Action<Paragraph>? prepare = null)
        {
            foreach (var child in node.ChildNodes)
            {
                if (child.NodeType == HtmlNodeType.Comment || child.Name is "script" or "style" or "head" or "title" or "noscript"
                    || _footnoteSections.Contains(child))
                    continue;
                if (child.NodeType == HtmlNodeType.Text || !Blocks.Contains(child.Name))
                {
                    Inline(child, new Segment(""), prepare);
                    continue;
                }
                bool jump = (child.GetAttributeValue("style", "") + child.GetAttributeValue("class", "")).Contains("page-break-before", StringComparison.OrdinalIgnoreCase);
                switch (child.Name)
                {
                    case "h1" or "h2" or "h3" or "h4" or "h5" or "h6":
                        ParagraphEnd();
                        _pageBreak |= jump;
                        int level = child.Name[1] - '0';
                        Block(child, p => p.HeadingLevel = level);
                        ParagraphEnd();
                        break;
                    case "ul" or "ol":
                        ParagraphEnd();
                        bool numbered = child.Name == "ol";
                        foreach (var item in child.ChildNodes.Where(n => n.Name == "li"))
                        {
                            Block(item, p =>
                            {
                                p.Bulleted = !numbered;
                                p.Numbered = numbered;
                            });
                            ParagraphEnd();
                        }
                        break;
                    case "table":
                        ParagraphEnd();
                        Table(child);
                        break;
                    case "hr":
                        ParagraphEnd();
                        break;
                    case "pre":
                        ParagraphEnd();
                        foreach (var row in WebUtility.HtmlDecode(child.InnerText).ReplaceLineEndings("\n").Split('\n'))
                        {
                            Current().Add(new Segment(row, Font: "Consolas"));
                            ParagraphEnd();
                        }
                        break;
                    case "blockquote":
                        ParagraphEnd();
                        Block(child, p => p.LeftIndent = 36);
                        ParagraphEnd();
                        break;
                    default:
                        ParagraphEnd();
                        _pageBreak |= jump;
                        Block(child, prepare + (p => p.Alignment = Alignment(child, p.Alignment)));
                        ParagraphEnd();
                        break;
                }
            }
        }

        private static Alignment Alignment(HtmlNode node, Alignment current)
        {
            var style = (node.GetAttributeValue("style", "") + ";" + node.GetAttributeValue("align", "")).ToLowerInvariant();
            return style.Contains("center") ? Documents.Alignment.Center
                : style.Contains("right") ? Documents.Alignment.Right
                : style.Contains("justify") ? Documents.Alignment.Justified
                : current;
        }

        private void Table(HtmlNode table)
        {
            int number = _table++;
            var rows = table.Descendants("tr").Where(r => r.Ancestors("table").First() == table).ToList();
            for (int r = 0; r < rows.Count; r++)
            {
                var cells = rows[r].ChildNodes.Where(n => n.Name is "td" or "th").ToList();
                for (int c = 0; c < cells.Count; c++)
                {
                    _cell = new CellPosition(number, r, c);
                    ParagraphEnd();
                    Block(cells[c]);
                    if (!content.Paragraphs.Any(p => p.Cell == _cell))
                        Current(); // empty cell: an empty paragraph
                    ParagraphEnd();
                }
            }
            _cell = null;
        }

        private void Inline(HtmlNode node, Segment format, Action<Paragraph>? prepare)
        {
            if (node.NodeType == HtmlNodeType.Text)
            {
                var text = HtmlSpaces(WebUtility.HtmlDecode(node.InnerText));
                if (text.Length == 0 || text == " " && _current is null)
                    return;
                var p = Current();
                prepare?.Invoke(p);
                if (p.Segments.Count == 0)
                    text = text.TrimStart();
                p.Add(format with { Text = text });
                return;
            }
            switch (node.Name)
            {
                case "br":
                    ParagraphEnd();
                    return;
                case "img":
                    var src = node.GetAttributeValue("src", "");
                    var data = ImageData(src);
                    if (data is null || content.Images.Count >= Images.Max)
                        return;
                    try
                    {
                        var image = Images.FromData(data, WebUtility.HtmlDecode(node.GetAttributeValue("alt", "")).Trim());
                        if (int.TryParse(node.GetAttributeValue("width", ""), out int width) && width > 0)
                            image = Images.WithWidth(image, width * 72f / 96f);
                        content.Images.Add(image);
                        var p = Current();
                        prepare?.Invoke(p);
                        p.Add(new Segment(Images.Placeholder(content.Images.Count - 1).ToString()));
                    }
                    catch (Exception e) when (e is ArgumentException or System.Runtime.InteropServices.ExternalException or OutOfMemoryException)
                    {
                    }
                    return;
                case "a" when IsNoteReference(node) && _footnoteTexts is not null
                    && _footnoteTexts.TryGetValue(node.GetAttributeValue("href", "").Split('#')[^1], out var footnoteText)
                    && content.Footnotes.Count < FootnoteMarkers.Max:
                    content.Footnotes.Add(footnoteText);
                    var withFootnote = Current();
                    prepare?.Invoke(withFootnote);
                    withFootnote.Add(new Segment(FootnoteMarkers.Placeholder(content.Footnotes.Count - 1).ToString()));
                    return;
                case "a" when node.ChildNodes.Count == 0 && (node.GetAttributeValue("id", "") is { Length: > 0 } || node.GetAttributeValue("name", "") is { Length: > 0 }):
                    var name = node.GetAttributeValue("id", "") is { Length: > 0 } id ? id : node.GetAttributeValue("name", "");
                    if (content.Bookmarks.Count < Bookmarks.Max)
                    {
                        content.Bookmarks.Add(name.Replace('_', ' '));
                        Current().Add(new Segment(Bookmarks.Placeholder(content.Bookmarks.Count - 1).ToString()));
                    }
                    return;
            }
            var @new = node.Name switch
            {
                "b" or "strong" => format with { Bold = true },
                "i" or "em" or "cite" or "dfn" => format with { Italic = true },
                "u" or "ins" => format with { Underline = true },
                "s" or "del" or "strike" => format with { Strikethrough = true },
                "sup" => format with { Position = TextPosition.Superscript },
                "sub" => format with { Position = TextPosition.Subscript },
                "code" or "kbd" or "samp" => format with { Font = "Consolas" },
                _ => format,
            };
            foreach (var child in node.ChildNodes)
            {
                if (child.NodeType != HtmlNodeType.Text && Blocks.Contains(child.Name))
                {
                    // A block inside an inline element (badly written HTML): treat it as a block.
                    ParagraphEnd();
                    Block(child.ParentNode == node ? WrapperOf(child) : child);
                    continue;
                }
                Inline(child, @new, prepare);
            }
        }

        private static HtmlNode WrapperOf(HtmlNode node)
        {
            var container = HtmlNode.CreateNode("<div></div>");
            container.AppendChild(node.CloneNode(true));
            return container;
        }

        private byte[]? ImageData(string src)
        {
            if (src.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                int comma = src.IndexOf(',');
                if (comma < 0 || !src[..comma].Contains(";base64", StringComparison.OrdinalIgnoreCase))
                    return null;
                try
                {
                    return Convert.FromBase64String(src[(comma + 1)..]);
                }
                catch (FormatException)
                {
                    return null;
                }
            }
            return resource?.Invoke(WebUtility.HtmlDecode(src));
        }

        /// <summary>As the browser does: line breaks and repeated spaces become a single space.</summary>
        private static string HtmlSpaces(string text)
        {
            var result = new StringBuilder(text.Length);
            bool space = false;
            foreach (var c in text)
            {
                if (c is ' ' or '\n' or '\r' or '\t')
                {
                    if (!space)
                        result.Append(' ');
                    space = true;
                }
                else
                {
                    result.Append(c == ' ' ? ' ' : c);
                    space = false;
                }
            }
            return result.ToString();
        }
    }
}
