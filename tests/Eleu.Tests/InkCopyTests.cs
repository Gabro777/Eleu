using System.Drawing;
using Eleu.Braille;
using UglyToad.PdfPig;

namespace Eleu.Tests;

/// <summary>The Braille "in ink": dots and letters under every line, one A4 sheet per Braille page.</summary>
public class InkCopyTests
{
    private static List<List<string>> Pages() =>
        Paginator.Paginate(
            [
                new BrailleParagraph(BrailleTranslator.Translate("Il viaggio"), IsHeading: true, Level: 1),
                new BrailleParagraph(BrailleTranslator.Translate("La città è bella e ci sono 12 case lungo il fiume, con le finestre aperte sul mare.")),
                new BrailleParagraph(BrailleTranslator.Translate("Il secondo paragrafo va su un'altra pagina."), NewPage: true),
            ],
            new PageFormat(32, 27) { Number = BraillePageNumber.TopRight }, BrailleTranslator.Translate);

    [Fact]
    public void Pdf_OneA4SheetPerBraillePage()
    {
        var pages = Pages();
        var pdf = InkCopy.Pdf(pages, 32, 27, BrailleTranslator.BackTranslate, "Prova");
        using var document = PdfDocument.Open(pdf);
        Assert.Equal(pages.Count, document.NumberOfPages);
        foreach (var page in document.GetPages())
        {
            Assert.Equal(595, page.Width, 1);
            Assert.Equal(842, page.Height, 1);
            Assert.Single(page.GetImages());
        }
    }

    [Fact]
    public void Page_DotsAreBlack_TheRestIsLight()
    {
        var page = Pages()[0];
        using var image = InkCopy.Page(page, 32, 27, BrailleTranslator.BackTranslate, "Prova");

        // The sheet is mostly white, with black dots and letters.
        int dark = 0, total = 0;
        for (int y = 0; y < image.Height; y += 4)
            for (int x = 0; x < image.Width; x += 4, total++)
                if (image.GetPixel(x, y).GetBrightness() < 0.3f)
                    dark++;
        Assert.InRange(dark * 100.0 / total, 0.2, 15);
    }

    [Fact]
    public void BrailleDialog_InkCopyIsOffAtFirst()
    {
        bool ink = true;
        Support.RunSta(() =>
        {
            using var window = new UI.BrailleDialog(["Stampante"], null, new PageFormat(), _ => 1);
            ink = window.InkCopy;
        });
        Assert.False(ink);
    }
}
