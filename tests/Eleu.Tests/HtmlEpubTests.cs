using System.IO.Compression;
using Eleu.Documents;

namespace Eleu.Tests;

public class HtmlEpubTests
{
    /// <summary>A document with a bit of everything.</summary>
    private static DocumentContent Sample()
    {
        var c = new DocumentContent { Metadata = new Metadata("Ricette", "Anna Rossi") };
        c.Bookmarks.Add("dolci");
        c.Footnotes.Add("Meglio se fresche.");
        c.Images.Add(Images.FromData(ImagesTests.Bitmap(System.Drawing.Imaging.ImageFormat.Png), "Una torta"));
        Paragraph P(string text, int heading = 0, bool list = false, CellPosition? cell = null, bool bold = false)
        {
            var p = new Paragraph { HeadingLevel = heading, Bulleted = list, Cell = cell };
            p.Add(new Segment(text, Bold: bold));
            c.Paragraphs.Add(p);
            return p;
        }
        P("Primi", 1);
        P($"Servono uova{FootnoteMarkers.Placeholder(0)} e farina.").Add(new Segment(" Molto", Bold: true));
        P("uova", list: true);
        P("farina", list: true);
        P($"{Bookmarks.Placeholder(0)}Dolci", 1);
        P($"Ecco {Images.Placeholder(0)} la torta.");
        P("Nome", cell: new CellPosition(0, 0, 0));
        P("Età", cell: new CellPosition(0, 0, 1));
        P("Anna", cell: new CellPosition(0, 1, 0));
        P("40", cell: new CellPosition(0, 1, 1));
        P("Fine.");
        return c;
    }

    private static string Describe(DocumentContent c) => string.Join(" | ", c.Paragraphs
        .Where(p => p.Text.Length > 0 || p.Cell is not null)
        .Select(p => $"{(p.HeadingLevel > 0 ? $"T{p.HeadingLevel} " : "")}{(p.Bulleted ? "• " : "")}{(p.Cell is { } x ? $"[{x.Row},{x.Column}] " : "")}{p.Text}"));

    [Fact]
    public void Html_WrittenAndReadBack()
    {
        var original = Sample();
        var html = HtmlFormat.Write(original);
        Assert.Contains("<html lang=\"it\">", html);
        Assert.Contains("alt=\"Una torta\"", html);
        Assert.Contains("<th scope=\"col\">Nome</th>", html);
        Assert.Contains("role=\"doc-noteref\"", html);

        var read = HtmlFormat.Read(html);
        Assert.Equal(new Metadata("Ricette", "Anna Rossi"), read.Metadata);
        Assert.Equal(Describe(Without(original)), Describe(Without(read)).Replace(" | Note | Meglio se fresche. ↩", ""));
        Assert.Single(read.Images);
        Assert.Equal("Una torta", read.Images[0].Description);
        Assert.Contains(read.Paragraphs.SelectMany(p => p.Segments), s => s.Bold && s.Text.Contains("Molto"));
        Assert.Contains("dolci", read.Bookmarks);
        Assert.Equal(["Meglio se fresche."], read.Footnotes); // the reference comes back as a footnote, the footnotes section is not text
        Assert.DoesNotContain(read.Paragraphs, p => p.Text.Contains("Meglio"));
    }

    /// <summary>The text without the placeholders (which in HTML become references, images and anchors).</summary>
    private static DocumentContent Without(DocumentContent c)
    {
        var r = new DocumentContent();
        foreach (var p in c.Paragraphs)
        {
            var n = p.CopyProperties();
            n.Cell = p.Cell is { } x ? new CellPosition(x.Table, x.Row, x.Column) : null;
            n.Add(new Segment(string.Concat(p.Text.Where(ch => ch is < (char)0xE000 or > (char)0xF8FF)).Replace("  ", " ")));
            r.Paragraphs.Add(n);
        }
        return r;
    }

    [Fact]
    public void Html_FromAWebPage()
    {
        var read = HtmlFormat.Read("""
            <html><head><title>Pagina</title><script>x()</script></head><body>
            <h2>Titolo</h2><div>Primo <b>grassetto</b><br>a capo</div>
            <ol><li>uno</li><li>due</li></ol><p>&nbsp;Spazi   e&amp;segni&nbsp;</p></body></html>
            """);
        Assert.Equal("T2 Titolo | Primo grassetto | a capo | uno | due | Spazi e&segni", Describe(read));
        Assert.True(read.Paragraphs.First(p => p.Text == "uno").Numbered);
    }

    [Fact]
    public void Epub_CorrectStructureAndReadBack()
    {
        using var stream = new MemoryStream();
        EpubFormat.Write(Sample(), stream);

        stream.Position = 0;
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true))
        {
            Assert.Equal("mimetype", zip.Entries[0].FullName);
            Assert.Equal(zip.Entries[0].Length, zip.Entries[0].CompressedLength); // uncompressed
            var names = zip.Entries.Select(e => e.FullName).ToList();
            Assert.Contains("OEBPS/capitolo001.xhtml", names);
            Assert.Contains("OEBPS/capitolo002.xhtml", names); // one chapter per Heading 1
            Assert.Contains("OEBPS/note.xhtml", names);
            Assert.Contains("OEBPS/nav.xhtml", names);
            Assert.Contains("OEBPS/immagini/immagine001.png", names);
            using var opf = new StreamReader(zip.GetEntry("OEBPS/content.opf")!.Open());
            var package = opf.ReadToEnd();
            Assert.Contains("schema:accessibilityFeature\">alternativeText", package);
            Assert.Contains("<dc:creator>Anna Rossi</dc:creator>", package);
            // Every chapter is valid XML.
            foreach (var entry in zip.Entries.Where(e => e.FullName.EndsWith(".xhtml")))
            {
                using var s = new StreamReader(entry.Open());
                System.Xml.Linq.XDocument.Parse(s.ReadToEnd());
            }
        }

        stream.Position = 0;
        var read = EpubFormat.Read(stream);
        Assert.Equal("Ricette", read.Metadata.Title);
        var text = Describe(Without(read));
        Assert.StartsWith("T1 Primi | Servono uova", text);
        Assert.Contains("T1 Dolci", text);
        Assert.Contains("[1,1] 40", text);
        Assert.Contains("Meglio se fresche.", text); // the footnotes chapter
        Assert.Single(read.Images);
        Assert.True(read.Paragraphs.First(p => p.Text.Contains("Dolci")).PageBreak);
    }
}
