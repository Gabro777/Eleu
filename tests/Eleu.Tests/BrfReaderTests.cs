using System.Text;
using Eleu.Braille;
using Eleu.Import;

namespace Eleu.Tests;

/// <summary>Opening BRF files: the page layout is undone and the text comes back as paragraphs.</summary>
public class BrfReaderTests
{
    private static readonly string[] Text =
    [
        "Il viaggio",
        "La città è bella e ci sono 12 case lungo il fiume, con le finestre aperte sul mare e i giardini pieni di fiori.",
        "Il secondo paragrafo è lungo abbastanza da andare a capo più volte: parla del porto, delle barche e dei pescatori che tornano la sera.",
        "Poi una frase breve.",
    ];

    private static List<BrailleParagraph> Paragraphs() =>
        [.. Text.Select((t, i) => new BrailleParagraph(BrailleTranslator.Translate(t), IsHeading: i == 0, Level: i == 0 ? 1 : 0))];

    private static List<string> RoundTrip(PageFormat format)
    {
        var pages = Paginator.Paginate(Paragraphs(), format, BrailleTranslator.Translate);
        return BrfReader.Read(BrfFormat.Byte(pages), BrailleTranslator.BackTranslate).Paragraphs;
    }

    public static TheoryData<string> Formats() =>
    [
        "semplice", "numero in alto", "numero in fondo", "titolo e numero", "margine", "fronte e retro",
        "interlinea doppia", "piega", "pagine a stampa", "sommario", "tutto",
    ];

    private static PageFormat Format(string name) => name switch
    {
        "semplice" => new PageFormat(32, 12),
        "numero in alto" => new PageFormat(32, 12) { Number = BraillePageNumber.TopRight },
        "numero in fondo" => new PageFormat(32, 12) { Number = BraillePageNumber.BottomRight },
        "titolo e numero" => new PageFormat(32, 12) { Number = BraillePageNumber.TopRight, Title = BrailleTranslator.Translate("Racconti") },
        "margine" => new PageFormat(32, 12) { LeftMargin = 3 },
        "fronte e retro" => new PageFormat(32, 8) { LeftMargin = 3, DoubleSided = true, Number = BraillePageNumber.BottomRight },
        "interlinea doppia" => new PageFormat(32, 12) { DoubleSpacing = true },
        "piega" => new PageFormat(32, 12) { FoldInHalf = true },
        "pagine a stampa" => new PageFormat(32, 12) { PrintPages = true },
        "sommario" => new PageFormat(32, 12) { TableOfContents = true },
        _ => new PageFormat(32, 10)
        {
            Number = BraillePageNumber.TopRight, Title = BrailleTranslator.Translate("Racconti"), LeftMargin = 2,
            DoubleSided = true, FoldInHalf = true, TableOfContents = true,
        },
    };

    [Theory]
    [MemberData(nameof(Formats))]
    public void PaginatedBraille_GivesBackTheParagraphs(string format)
    {
        Assert.Equal(Text, RoundTrip(Format(format)));
    }

    [Fact]
    public void Brf_LowercaseLettersAndLfOnly_AreReadToo()
    {
        var pages = Paginator.Paginate(Paragraphs(), new PageFormat(32, 12), BrailleTranslator.Translate);
        var brf = BrfFormat.Text(pages).ToLowerInvariant().Replace("\r\n", "\n");
        Assert.Equal(Text, BrfReader.Read(Encoding.ASCII.GetBytes(brf), BrailleTranslator.BackTranslate).Paragraphs);
    }

    [Fact]
    public async Task OpeningABrfFile_GivesTheText()
    {
        var path = Path.Combine(Path.GetTempPath(), $"eleu-test-{Guid.NewGuid():N}.brf");
        try
        {
            var pages = Paginator.Paginate(Paragraphs(), new PageFormat(32, 12) { Number = BraillePageNumber.TopRight }, BrailleTranslator.Translate);
            File.WriteAllBytes(path, BrfFormat.Byte(pages));
            Assert.True(Importer.IsImportable(path));

            var result = await Importer.ImportAsync(path, null, CancellationToken.None);

            Assert.False(result.WithOcr);
            Assert.Equal(pages.Count, result.Pages);
            Assert.Equal(Text, result.Paragraphs);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
