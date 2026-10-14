using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using Eleu.Braille;
using Eleu.Documents;
using Eleu.Editor;
using static Eleu.Tests.Support;

namespace Eleu.Tests;

public class PageTests
{
    private static readonly PageLayout Custom = new(
        PageLayout.Formats[1].Width, PageLayout.Formats[1].Height, Horizontal: true,
        LeftMargin: 72, RightMargin: 36, TopMargin: 54, BottomMargin: 90,
        HeaderDistance: 36, FooterDistance: 18,
        Header: "Relazione annuale: {titolo}", Footer: "Riservato - pagina {pagina} di {pagine}", SkipFirstPage: true);

    [Fact]
    public void Docx_KeepsPaperMarginsHeaderAndNumbers()
    {
        var content = Sample();
        content.Page = Custom;
        using var stream = new MemoryStream();
        DocxFormat.Write(content, stream);

        stream.Position = 0;
        using (var documents = WordprocessingDocument.Open(stream, false))
            Assert.Empty(new OpenXmlValidator().Validate(documents).Select(e => $"{e.Path?.XPath}: {e.Description}"));

        stream.Position = 0;
        var reread = DocxFormat.Read(stream);
        Assert.Equal(Custom, reread.Page);
        Assert.Equal("A5", reread.Page.Format?.Name);
        Assert.Empty(reread.NotPreserved);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("Pagina {pagina}", false)]
    [InlineData("Riservato - pagina {pagina} di {pagine}, {data}", true)]
    public void Rtf_KeepsThePage(string footer, bool skipFirst)
    {
        var page = Custom with { Footer = footer, SkipFirstPage = skipFirst };
        var rtf = RtfWriter.WithPage(RtfWriter.Write(Sample()), page);
        var reread = RtfReader.Read(rtf);
        Assert.Equal(page, reread.Page);
        // Header and footer do not end up in the document text.
        Assert.DoesNotContain(reread.Paragraphs, p => p.Text.Contains("Relazione annuale") || p.Text.Contains("Riservato"));
    }

    [Fact]
    public void WordRtf_WithPageInsideTheParagraph()
    {
        var content = RtfReader.Read(@"{\rtf1\ansi\pard Prima\par\pard Seconda\page Terza\par}");
        Assert.Equal(["Prima", "Seconda", "Terza"], content.Paragraphs.Select(p => p.Text));
        Assert.Equal([false, false, true], content.Paragraphs.Select(p => p.PageBreak));
    }

    [Fact]
    public void Pagination_SplitsLongTextsAndHonorsBreaks()
    {
        List<int> starts = [];
        int length = 0, pageBreakPosition = 0;
        RunSta(() =>
        {
            using var field = new RichTextBox();
            field.CreateControl();
            var paragraph = string.Concat(Enumerable.Repeat("Una riga di testo abbastanza lunga da occupare spazio sulla pagina. ", 8));
            var text = string.Join("\n", Enumerable.Repeat(paragraph, 40));
            pageBreakPosition = text.Length + 1;
            field.Text = text + "\n" + Pagination.PageBreak + "Dopo l'interruzione.";
            length = field.TextLength;
            starts = Pagination.PageStarts(field, new PageLayout());
        });

        Assert.True(starts.Count >= 4, $"Solo {starts.Count} pagine");
        Assert.Equal(0, starts[0]);
        Assert.Equal(starts.Order(), starts);
        Assert.Contains(pageBreakPosition, starts);
        Assert.DoesNotContain(pageBreakPosition + 1, starts); // no empty page with just the page break
        Assert.Equal(pageBreakPosition, starts[^1]); // the text after the break is on one page
        Assert.Equal(starts.Count, Pagination.PageOf(starts, length - 1));
        Assert.Equal(1, Pagination.PageOf(starts, 0));
    }

    [Fact]
    public void Braille_PageBreakStartsANewPage()
    {
        var pages = Paginator.Paginate(
            [new BrailleParagraph("⠁"), new BrailleParagraph("⠃", NewPage: true), new BrailleParagraph("⠉")],
            new PageFormat(20, 20));
        Assert.Equal(2, pages.Count);
        Assert.Single(pages[0]);
        Assert.Equal(2, pages[1].Count);
    }

    [Fact]
    public void Variables_ReplacedWhenPrinting()
    {
        var page = new PageLayout(Footer: "{titolo} - pagina {pagina} di {pagine}");
        Assert.Equal("Lettera - pagina 3 di 10", page.Compose(page.Footer, 3, 10, "Lettera", new DateTime(2026, 10, 9)));
        Assert.Equal("9 ottobre 2026", page.Compose("{data}", 1, 1, "", new DateTime(2026, 10, 9)));
        Assert.Equal("", (page with { SkipFirstPage = true }).Compose(page.Footer, 1, 10, "Lettera", DateTime.Today));
    }

    [Fact]
    public void Variables_SplitIntoTextAndFields()
    {
        Assert.Equal(
            [("Pagina ", null), ("{pagina}", "PAGE"), (" di ", null), ("{pagine}", "NUMPAGES")],
            PageLayout.Pieces("Pagina {pagina} di {pagine}"));
        Assert.Equal("{pagine}", PageLayout.FieldPlaceholder(" NUMPAGES \\* MERGEFORMAT "));
    }
}
