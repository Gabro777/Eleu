using Eleu.Documents;
using Eleu.Editor;

namespace Eleu.Tests;

public class DescriptionsTests
{
    [Fact]
    public void Statistics_CountsWordsCharactersParagraphs()
    {
        var statistics = Statistics.Compute("Ciao, mondo!\n\nL'arte è bella.");
        Assert.Equal(new Statistics(5, 27, 2), statistics);
        Assert.Equal("5 parole, 27 caratteri, 2 paragrafi", statistics.Description());
    }

    [Fact]
    public void Statistics_Singulars()
    {
        Assert.Equal("1 parola, 1 carattere, 1 paragrafo", Statistics.Compute("a").Description());
    }

    private static readonly ParagraphFormat NormalParagraph =
        new(Alignment.Left, 0, 0, 0, 0, 0, 1f, ListKind.None);

    [Fact]
    public void Format_CenteredBoldHeading()
    {
        var character = new CharacterFormat("Segoe UI", 18, true, false, false, false, TextPosition.Normal,
            new ColorValue(null), new ColorValue(null), 0);
        Assert.Equal("Titolo 1, Segoe UI 18 punti, grassetto, centrato",
            FormatDescription.Describe(1, character, NormalParagraph with { Alignment = Alignment.Center }));
    }

    [Fact]
    public void Format_EverythingThatDiffersFromNormal()
    {
        var character = new CharacterFormat(null, null, null, false, true, true, TextPosition.Superscript,
            new ColorValue(0xC00000), new ColorValue(0xFFFF00), 1.5f);
        var paragraph = new ParagraphFormat(Alignment.Justified, 36, 0, -18, 6, 12, 1.5f, ListKind.Numbered);
        Assert.Equal(
            "Testo normale, caratteri misti, dimensioni miste, in parte grassetto, sottolineato, barrato, apice, " +
            "colore rosso scuro, sfondo giallo, spaziatura 1,5 punti, giustificato, elenco numerato, " +
            "interlinea una e mezza, rientro sinistro 1,3 centimetri, prima riga sporgente di 0,6 centimetri, " +
            "spazio prima 6 punti, spazio dopo 12 punti",
            FormatDescription.Describe(0, character, paragraph));
    }

    [Fact]
    public void Contrast_FollowsW3CRules()
    {
        Assert.Equal(21, Colors.Contrast(Colors.Black, Colors.White), 1);
        Assert.Equal("Contrasto 21:1, ottimo.", Colors.ContrastRating(null, null));
        Assert.EndsWith("insufficiente: difficile da leggere.", Colors.ContrastRating(0xFFFF00, null));
        Assert.EndsWith("buono.", Colors.ContrastRating(0x0070C0, null));
        Assert.Equal("rosso scuro", Colors.Name(0xC00000));
        Assert.Equal("personalizzato (123456)", Colors.Name(0x123456));
    }
}
