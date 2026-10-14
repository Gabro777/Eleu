using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Eleu.Documents;

namespace Eleu.Tests;

public class OdtTests
{
    private static Paragraph P(string text, Action<Paragraph>? set = null)
    {
        var p = new Paragraph();
        p.Add(new Segment(text));
        set?.Invoke(p);
        return p;
    }

    private static DocumentContent Rich()
    {
        var c = new DocumentContent
        {
            Metadata = new Metadata("Ricette", "Anna Rossi", "Cucina", "uova, farina", new DateTime(2026, 1, 2, 3, 4, 0)),
            Page = new PageLayout(Horizontal: true, LeftMargin: 50, Header: "Pagina {pagina} di {pagine}", Footer: "{titolo}"),
        };
        c.Paragraphs.Add(P("Torta", p => p.HeadingLevel = 1));
        var formatted = new Paragraph { Alignment = Alignment.Justified, FirstLineIndent = 20, SpaceAfter = 6, LineSpacing = 1.5f };
        formatted.Add(new Segment("Servono  "));
        formatted.Add(new Segment("uova", Bold: true));
        formatted.Add(new Segment(" e "));
        formatted.Add(new Segment("farina", Italic: true, Underline: true, Size: 14, Font: "Arial", Color: 0xC00000));
        formatted.Add(new Segment("2", Position: TextPosition.Superscript));
        c.Paragraphs.Add(formatted);
        c.Paragraphs.Add(P("uova", p => p.Bulleted = true));
        c.Paragraphs.Add(P("farina", p => p.Bulleted = true));
        c.Paragraphs.Add(P("sbatti", p => p.Numbered = true));
        c.Paragraphs.Add(P("cuoci", p => p.Numbered = true));

        // A 2 x 2 table with the header row and the first column 3 cm wide.
        string[,] cells = { { "Cosa", "Quanto" }, { "Uova", "3" } };
        for (int r = 0; r < 2; r++)
            for (int col = 0; col < 2; col++)
                c.Paragraphs.Add(P(cells[r, col], p => p.Cell = new CellPosition(0, r, col, col == 0 ? 85.04f : 0, Header: r == 0)));

        c.Footnotes.Add("Meglio se fresche.\nDi gallina.");
        c.Bookmarks.Add("forno");
        c.Comments.Add(new Comment("Quanto zucchero?", "Luca", new DateTime(2026, 10, 10, 9, 30, 0)));
        c.Comments.Add(new Comment("Bella!", "Anna Rossi"));
        c.Images.Add(Images.FromData(ImagesTests.Bitmap(System.Drawing.Imaging.ImageFormat.Png), "Una torta al cioccolato"));
        c.Paragraphs.Add(P($"Inforna{FootnoteMarkers.Placeholder(0)} {Bookmarks.Placeholder(0)}a {CommentMarkers.Start(0)}180 gradi{CommentMarkers.End(0)}.{CommentMarkers.Start(1)}{CommentMarkers.End(1)}",
            p => p.PageBreak = true));
        c.Paragraphs.Add(P($"Ecco: {Images.Placeholder(0)}"));
        return c;
    }

    private static byte[] Odt(DocumentContent c)
    {
        using var stream = new MemoryStream();
        OdtFormat.Write(c, stream);
        return stream.ToArray();
    }

    private static DocumentContent Read(byte[] data)
    {
        using var stream = new MemoryStream(data);
        return OdtFormat.Read(stream);
    }

    [Fact]
    public void Zip_MimetypeFirstUncompressedAndWellFormedXml()
    {
        var data = Odt(Rich());
        Assert.Equal("PK", Encoding.ASCII.GetString(data, 0, 2));
        // In the local header of the first file: method 0 (stored), name "mimetype", then the type in clear.
        Assert.Equal(0, BitConverter.ToUInt16(data, 8));
        Assert.Equal("mimetype", Encoding.ASCII.GetString(data, 30, 8));
        Assert.Equal(OdtFormat.MimeType, Encoding.ASCII.GetString(data, 30 + 8 + BitConverter.ToUInt16(data, 28), OdtFormat.MimeType.Length));

        using var zip = new ZipArchive(new MemoryStream(data));
        Assert.Equal("mimetype", zip.Entries[0].FullName);
        foreach (var name in new[] { "content.xml", "styles.xml", "meta.xml", "META-INF/manifest.xml" })
        {
            using var s = zip.GetEntry(name)!.Open();
            Assert.NotNull(XDocument.Load(s).Root);
        }
        // Every file of the package is in the manifest.
        using var m = zip.GetEntry("META-INF/manifest.xml")!.Open();
        var listed = XDocument.Load(m).Descendants().Select(e => (string?)e.Attributes().FirstOrDefault(a => a.Name.LocalName == "full-path")).ToHashSet();
        Assert.All(zip.Entries.Where(e => e.FullName is not ("mimetype" or "META-INF/manifest.xml")), e => Assert.Contains(e.FullName, listed));
    }

    [Fact]
    public void RoundTrip_KeepsText()
    {
        var original = Rich();
        var read = Read(Odt(original));
        Assert.Equal(original.Paragraphs.Select(p => p.Text), read.Paragraphs.Select(p => p.Text));
        Assert.Empty(read.NotPreserved);
    }

    [Fact]
    public void RoundTrip_KeepsFormatting()
    {
        var read = Read(Odt(Rich()));
        Assert.Equal(1, read.Paragraphs[0].HeadingLevel);
        var p = read.Paragraphs[1];
        Assert.Equal(Alignment.Justified, p.Alignment);
        Assert.Equal(20, p.FirstLineIndent, 1);
        Assert.Equal(6, p.SpaceAfter, 1);
        Assert.Equal(1.5f, p.LineSpacing, 2);
        Assert.Contains(p.Segments, s => s.Text == "uova" && s.Bold);
        var flour = p.Segments.Single(s => s.Text == "farina");
        Assert.True(flour.Italic && flour.Underline);
        Assert.Equal(14f, flour.Size);
        Assert.Equal("Arial", flour.Font);
        Assert.Equal(0xC00000, flour.Color);
        Assert.Equal(TextPosition.Superscript, p.Segments.Single(s => s.Text == "2").Position);
        Assert.Equal("Servono  uova e farina2", p.Text); // two spaces stay two

        Assert.True(read.Paragraphs[2].Bulleted && read.Paragraphs[3].Bulleted);
        Assert.True(read.Paragraphs[4].Numbered && read.Paragraphs[5].Numbered);
        Assert.True(read.Paragraphs.Single(x => x.Text.StartsWith("Inforna")).PageBreak);
    }

    [Fact]
    public void RoundTrip_KeepsTableFootnotesBookmarksCommentsImages()
    {
        var read = Read(Odt(Rich()));
        var cells = read.Paragraphs.Where(p => p.Cell is not null).ToList();
        Assert.Equal(["Cosa", "Quanto", "Uova", "3"], cells.Select(p => p.Text));
        Assert.True(cells[0].Cell!.Header);
        Assert.False(cells[2].Cell!.Header);
        Assert.Equal(85.04f, cells[0].Cell!.Width, 1);
        Assert.Equal((1, 1), (cells[3].Cell!.Row, cells[3].Cell!.Column));

        Assert.Equal(["Meglio se fresche.\nDi gallina."], read.Footnotes);
        Assert.Equal(["forno"], read.Bookmarks);
        Assert.Equal("Quanto zucchero?", read.Comments[0].Text);
        Assert.Equal("Luca", read.Comments[0].Author);
        Assert.Equal(new DateTime(2026, 10, 10, 9, 30, 0), read.Comments[0].Date);
        Assert.Equal("Bella!", read.Comments[1].Text);
        Assert.Single(read.Images);
        Assert.Equal("Una torta al cioccolato", read.Images[0].Description);
    }

    [Fact]
    public void RoundTrip_KeepsPageAndProperties()
    {
        var original = Rich();
        var read = Read(Odt(original));
        Assert.True(read.Page.Horizontal);
        Assert.Equal(original.Page.Width, read.Page.Width, 0);
        Assert.Equal(50, read.Page.LeftMargin, 1);
        Assert.Equal("Pagina {pagina} di {pagine}", read.Page.Header);
        Assert.Equal("{titolo}", read.Page.Footer);
        Assert.Equal(original.Page.TopMargin, read.Page.TopMargin, 0);
        Assert.Equal(original.Metadata with { }, read.Metadata with { });
    }

    /// <summary>A document as LibreOffice writes it: named styles, spans, spaces, lists with their style, numbered headings.</summary>
    [Fact]
    public void Read_DocumentAsLibreOfficeWritesIt()
    {
        const string content = """
            <?xml version="1.0" encoding="UTF-8"?>
            <office:document-content xmlns:office="urn:oasis:names:tc:opendocument:xmlns:office:1.0" xmlns:style="urn:oasis:names:tc:opendocument:xmlns:style:1.0" xmlns:text="urn:oasis:names:tc:opendocument:xmlns:text:1.0" xmlns:table="urn:oasis:names:tc:opendocument:xmlns:table:1.0" xmlns:fo="urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0" xmlns:dc="http://purl.org/dc/elements/1.1/" office:version="1.3">
            <office:automatic-styles>
            <style:style style:name="P1" style:family="paragraph" style:parent-style-name="Standard"><style:paragraph-properties fo:text-align="center"/></style:style>
            <style:style style:name="T1" style:family="text"><style:text-properties fo:font-weight="bold"/></style:style>
            <text:list-style style:name="L1"><text:list-level-style-number text:level="1" style:num-suffix="." style:num-format="1"/></text:list-style>
            </office:automatic-styles>
            <office:body><office:text>
            <text:h text:style-name="Heading_20_2" text:outline-level="2"><text:number>1.1</text:number>Ingredienti</text:h>
            <text:p text:style-name="P1">Uno <text:span text:style-name="T1">due</text:span>   tre<text:s text:c="2"/>quattro<text:tab/>cinque<text:line-break/>sei</text:p>
            <text:soft-page-break/>
            <text:list text:style-name="L1"><text:list-item><text:p>primo</text:p></text:list-item><text:list-item><text:p>secondo</text:p>
            <text:list><text:list-item><text:p>dentro</text:p></text:list-item></text:list></text:list-item></text:list>
            <text:p>Nota<text:note text:id="ftn1" text:note-class="footnote"><text:note-citation>1</text:note-citation><text:note-body><text:p>Testo della nota</text:p></text:note-body></text:note> e <office:annotation><dc:creator>Anna</dc:creator><text:p>Sì</text:p></office:annotation>commento.</text:p>
            <table:table table:name="T"><table:table-column table:number-columns-repeated="2"/><table:table-row><table:table-cell table:number-columns-spanned="2"><text:p>unita</text:p></table:table-cell><table:covered-table-cell/></table:table-row></table:table>
            </office:text></office:body></office:document-content>
            """;
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var s = zip.CreateEntry("content.xml").Open();
            s.Write(Encoding.UTF8.GetBytes(content.Trim()));
        }
        stream.Position = 0;
        var read = OdtFormat.Read(stream);

        Assert.Equal("Ingredienti", read.Paragraphs[0].Text);
        Assert.Equal(2, read.Paragraphs[0].HeadingLevel);
        Assert.Equal("Uno due tre  quattro\tcinque", read.Paragraphs[1].Text);
        Assert.Equal(Alignment.Center, read.Paragraphs[1].Alignment);
        Assert.True(read.Paragraphs[1].Segments.Single(s => s.Text == "due").Bold);
        Assert.Equal("sei", read.Paragraphs[2].Text);
        Assert.True(read.Paragraphs[3].Numbered && read.Paragraphs[4].Numbered);
        Assert.Equal("dentro", read.Paragraphs[5].Text);
        Assert.True(read.Paragraphs[5].Numbered);
        Assert.Equal(["Testo della nota"], read.Footnotes);
        Assert.Equal([new Comment("Sì", "Anna")], read.Comments);
        Assert.Equal($"Nota{FootnoteMarkers.Placeholder(0)} e {CommentMarkers.Start(0)}{CommentMarkers.End(0)}commento.", read.Paragraphs[6].Text);
        Assert.Contains(read.NotPreserved, e => e.Contains("celle unite"));
    }

    [Fact]
    public void Read_NonOdtFile_Error()
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
            zip.CreateEntry("altro.txt");
        stream.Position = 0;
        Assert.Throws<InvalidDataException>(() => OdtFormat.Read(stream));
    }

    [Theory]
    [InlineData("2cm", 56.69)]
    [InlineData("12pt", 12)]
    [InlineData("0.5in", 36)]
    [InlineData("10mm", 28.346)]
    public void Points_FromMeasures(string measure, double expected) => Assert.Equal(expected, OdtFormat.Points(measure)!.Value, 1);
}
