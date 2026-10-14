using System.IO.Compression;
using System.Net;
using System.Text;
using System.Xml.Linq;

namespace Eleu.Documents;

/// <summary>
/// EPUB 3, the e-book format. Writing: one chapter for every heading of the highest level, the navigable
/// table of contents (nav), images with alternative text, footnotes in a separate file and the accessibility metadata
/// (schema.org) that readers and libraries expect. Reading: the chapters in book order, each
/// on a new page.
/// </summary>
public static class EpubFormat
{
    private const string Opf = "http://www.idpf.org/2007/opf";
    private const string Dc = "http://purl.org/dc/elements/1.1/";

    public static void Write(DocumentContent content, Stream stream)
    {
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);
        // The first file must be "mimetype", uncompressed.
        Write(zip, "mimetype", "application/epub+zip", CompressionLevel.NoCompression);
        Write(zip, "META-INF/container.xml",
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<container version=\"1.0\" xmlns=\"urn:oasis:names:tc:opendocument:xmlns:container\">" +
            "<rootfiles><rootfile full-path=\"OEBPS/content.opf\" media-type=\"application/oebps-package+xml\"/></rootfiles></container>");

        var chapters = Chapters(content);
        var manifest = new List<(string Id, string Href, string Kind, string? Properties)>();
        var headings = new List<(int Level, string Text, string Href)>();
        string footnotesFile = content.Footnotes.Count > 0 ? "note.xhtml" : "";

        for (int c = 0; c < chapters.Count; c++)
        {
            var (first, paragraphs) = chapters[c];
            var name = $"capitolo{c + 1:D3}.xhtml";
            var part = new DocumentContent { Metadata = content.Metadata };
            part.Paragraphs.AddRange(paragraphs);
            part.Bookmarks.AddRange(content.Bookmarks);
            part.Footnotes.AddRange(content.Footnotes);
            part.Images.AddRange(content.Images);
            var html = HtmlFormat.Write(part, xhtml: true, imageFile: ImageName, footnotesFile: footnotesFile.Length > 0 ? footnotesFile : null, firstParagraph: first);
            Write(zip, "OEBPS/" + name, html);
            manifest.Add(($"c{c + 1}", name, "application/xhtml+xml", null));
            for (int i = 0; i < paragraphs.Count; i++)
                if (paragraphs[i].HeadingLevel is > 0 and <= 3 && paragraphs[i].Text.Trim().Length > 0)
                    headings.Add((paragraphs[i].HeadingLevel, paragraphs[i].Text.Trim(), $"{name}#t{first + i}"));
        }

        if (footnotesFile.Length > 0)
        {
            var footnotes = new StringBuilder();
            footnotes.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<!DOCTYPE html>");
            footnotes.AppendLine("<html xmlns=\"http://www.w3.org/1999/xhtml\" xmlns:epub=\"http://www.idpf.org/2007/ops\" lang=\"it\" xml:lang=\"it\">");
            footnotes.AppendLine("<head><meta charset=\"utf-8\" /><title>Note</title></head><body>");
            footnotes.AppendLine("<section epub:type=\"endnotes\" role=\"doc-endnotes\"><h1>Note</h1><ol>");
            for (int n = 0; n < content.Footnotes.Count; n++)
                footnotes.Append($"<li id=\"nota-{n + 1}\" epub:type=\"endnote\">").Append(WebUtility.HtmlEncode(content.Footnotes[n]).Replace("\n", "<br />")).AppendLine("</li>");
            footnotes.AppendLine("</ol></section></body></html>");
            Write(zip, "OEBPS/" + footnotesFile, footnotes.ToString());
            manifest.Add(("note", footnotesFile, "application/xhtml+xml", null));
            headings.Add((1, "Note", footnotesFile));
        }

        for (int i = 0; i < content.Images.Count; i++)
        {
            var entry = zip.CreateEntry("OEBPS/" + ImageName(i), CompressionLevel.NoCompression);
            using (var s = entry.Open())
                s.Write(content.Images[i].Data);
            manifest.Add(($"img{i + 1}", ImageName(i), content.Images[i].Format == "jpeg" ? "image/jpeg" : "image/png", null));
        }

        Write(zip, "OEBPS/nav.xhtml", Index(headings, chapters.Count > 0 ? manifest[0].Href : "nav.xhtml"));
        manifest.Insert(0, ("nav", "nav.xhtml", "application/xhtml+xml", "nav"));
        Write(zip, "OEBPS/content.opf", Package(content, manifest, chapters.Count, footnotesFile.Length > 0));

        string ImageName(int i) => $"immagini/immagine{i + 1:D3}.{(content.Images[i].Format == "jpeg" ? "jpg" : "png")}";
    }

    /// <summary>The chapters: a new one at every heading of the highest level present; without headings, a single chapter.</summary>
    private static List<(int First, List<Paragraph> Paragraphs)> Chapters(DocumentContent content)
    {
        var levels = content.Paragraphs.Where(p => p.HeadingLevel > 0 && p.Cell is null).Select(p => p.HeadingLevel).ToList();
        int level = levels.Count == 0 ? 0 : levels.Min();
        var chapters = new List<(int, List<Paragraph>)>();
        List<Paragraph>? current = null;
        for (int i = 0; i < content.Paragraphs.Count; i++)
        {
            var p = content.Paragraphs[i];
            if (current is null || (level > 0 && p.HeadingLevel == level && p.Cell is null && current.Count > 0))
            {
                current = [];
                chapters.Add((i, current));
            }
            current.Add(p);
        }
        return chapters;
    }

    private static string Index(List<(int Level, string Text, string Href)> headings, string first)
    {
        var h = new StringBuilder();
        h.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<!DOCTYPE html>");
        h.AppendLine("<html xmlns=\"http://www.w3.org/1999/xhtml\" xmlns:epub=\"http://www.idpf.org/2007/ops\" lang=\"it\" xml:lang=\"it\">");
        h.AppendLine("<head><meta charset=\"utf-8\" /><title>Indice</title></head><body>");
        h.AppendLine("<nav epub:type=\"toc\" role=\"doc-toc\" id=\"toc\"><h1>Indice</h1>");
        if (headings.Count == 0)
            headings = [(1, "Inizio", first)];
        // Lists nested according to the heading level.
        int opened = 0;
        int previousLevel = 0;
        foreach (var (level, text, href) in headings)
        {
            int l = Math.Clamp(level, 1, opened + 1);
            if (l > previousLevel)
            {
                h.Append("<ol>");
                opened++;
            }
            else
            {
                h.Append("</li>");
                for (; previousLevel > l; previousLevel--)
                {
                    h.Append("</ol></li>");
                    opened--;
                }
            }
            h.Append($"<li><a href=\"{WebUtility.HtmlEncode(href)}\">{WebUtility.HtmlEncode(text)}</a>");
            previousLevel = l;
        }
        h.Append("</li>");
        for (; opened > 1; opened--)
            h.Append("</ol></li>");
        h.AppendLine("</ol>");
        h.AppendLine("</nav></body></html>");
        return h.ToString();
    }

    private static string Package(DocumentContent content, List<(string Id, string Href, string Kind, string? Properties)> manifest, int chapters, bool footnotes)
    {
        XNamespace opf = Opf, dc = Dc;
        var m = content.Metadata;
        var heading = m.Title.Length > 0 ? m.Title : content.Paragraphs.FirstOrDefault(p => p.HeadingLevel > 0)?.Text.Trim() ?? "Documento";
        bool images = content.Images.Count > 0;
        var metadata = new XElement(opf + "metadata",
            new XAttribute(XNamespace.Xmlns + "dc", Dc),
            new XElement(dc + "identifier", new XAttribute("id", "id-libro"), $"urn:uuid:{Guid.NewGuid()}"),
            new XElement(dc + "title", heading),
            new XElement(dc + "language", "it"),
            new XElement(opf + "meta", new XAttribute("property", "dcterms:modified"), DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")),
            // Accessibility: how the book can be read and what it offers.
            new XElement(opf + "meta", new XAttribute("property", "schema:accessMode"), "textual"),
            images ? new XElement(opf + "meta", new XAttribute("property", "schema:accessMode"), "visual") : null,
            new XElement(opf + "meta", new XAttribute("property", "schema:accessModeSufficient"), "textual"),
            new XElement(opf + "meta", new XAttribute("property", "schema:accessibilityFeature"), "structuralNavigation"),
            new XElement(opf + "meta", new XAttribute("property", "schema:accessibilityFeature"), "tableOfContents"),
            images ? new XElement(opf + "meta", new XAttribute("property", "schema:accessibilityFeature"), "alternativeText") : null,
            new XElement(opf + "meta", new XAttribute("property", "schema:accessibilityHazard"), "none"),
            new XElement(opf + "meta", new XAttribute("property", "schema:accessibilitySummary"),
                "Libro con titoli, indice navigabile" + (images ? " e descrizioni delle immagini" : "") + ", creato con Eleu."));
        if (m.Author.Length > 0)
            metadata.Add(new XElement(dc + "creator", m.Author));
        if (m.Subject.Length > 0)
            metadata.Add(new XElement(dc + "description", m.Subject));
        if (m.Keywords.Length > 0)
            metadata.Add(new XElement(dc + "subject", m.Keywords));
        if (m.Created is DateTime created)
            metadata.Add(new XElement(dc + "date", created.ToString("yyyy-MM-dd")));

        var package = new XElement(opf + "package",
            new XAttribute("version", "3.0"), new XAttribute("unique-identifier", "id-libro"), new XAttribute(XNamespace.Xml + "lang", "it"),
            metadata,
            new XElement(opf + "manifest", manifest.Select(v => new XElement(opf + "item",
                new XAttribute("id", v.Id), new XAttribute("href", v.Href), new XAttribute("media-type", v.Kind),
                v.Properties is null ? null : new XAttribute("properties", v.Properties)))),
            new XElement(opf + "spine",
                Enumerable.Range(1, chapters).Select(c => new XElement(opf + "itemref", new XAttribute("idref", $"c{c}"))),
                footnotes ? new XElement(opf + "itemref", new XAttribute("idref", "note")) : null));
        return new XDocument(new XDeclaration("1.0", "utf-8", null), package).Declaration + "\n" + package;
    }

    private static void Write(ZipArchive zip, string name, string text, CompressionLevel level = CompressionLevel.Optimal)
    {
        var entry = zip.CreateEntry(name, level);
        using var s = entry.Open();
        var data = new UTF8Encoding(false).GetBytes(text);
        s.Write(data);
    }

    // ---------- Reading ----------

    public static DocumentContent Read(Stream stream)
    {
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        string ReadText(string name)
        {
            using var reader = new StreamReader(zip.GetEntry(name)?.Open() ?? throw new InvalidDataException($"Nel libro manca {name}."));
            return reader.ReadToEnd();
        }

        var container = XDocument.Parse(ReadText("META-INF/container.xml"));
        var opfPath = container.Descendants().First(e => e.Name.LocalName == "rootfile").Attribute("full-path")!.Value;
        var folder = opfPath.Contains('/') ? opfPath[..(opfPath.LastIndexOf('/') + 1)] : "";
        var package = XDocument.Parse(ReadText(opfPath));

        string? Meta(string name) => package.Descendants().FirstOrDefault(e => e.Name.LocalName == name && e.Name.NamespaceName == Dc)?.Value.Trim();
        var content = new DocumentContent
        {
            Metadata = new Metadata(Meta("title") ?? "", Meta("creator") ?? "", Meta("description") ?? "", Meta("subject") ?? "",
                DateTime.TryParse(Meta("date"), out var date) ? date : null),
        };

        var elements = package.Descendants().Where(e => e.Name.LocalName == "item")
            .ToDictionary(e => e.Attribute("id")!.Value, e => (Href: e.Attribute("href")!.Value, Kind: e.Attribute("media-type")?.Value ?? "", Properties: e.Attribute("properties")?.Value ?? ""));
        var spine = package.Descendants().Where(e => e.Name.LocalName == "itemref").Select(e => e.Attribute("idref")!.Value);

        bool first = true;
        foreach (var id in spine)
        {
            if (!elements.TryGetValue(id, out var element) || !element.Kind.Contains("html") || element.Properties.Contains("nav"))
                continue;
            var path = Combine(folder, WebUtility.UrlDecode(element.Href));
            int before = content.Paragraphs.Count;
            HtmlFormat.Read(ReadText(path), src =>
            {
                var entry = zip.GetEntry(Combine(path[..(path.LastIndexOf('/') + 1)], WebUtility.UrlDecode(src.Split('#')[0])));
                if (entry is null)
                    return null;
                using var s = entry.Open();
                using var copy = new MemoryStream();
                s.CopyTo(copy);
                return copy.ToArray();
            }, content);
            // Every chapter starts on a new page.
            if (!first && content.Paragraphs.Count > before)
                content.Paragraphs[before].PageBreak = true;
            first = false;
        }
        return content;
    }

    /// <summary>A path inside the book, resolving "../" and "./".</summary>
    private static string Combine(string folder, string relative)
    {
        var parts = new List<string>();
        foreach (var part in (folder + relative).Split('/'))
        {
            if (part is "" or ".")
                continue;
            if (part == ".." && parts.Count > 0)
                parts.RemoveAt(parts.Count - 1);
            else if (part != "..")
                parts.Add(part);
        }
        return string.Join('/', parts);
    }
}
