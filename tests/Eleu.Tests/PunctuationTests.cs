using Eleu.Proofing;

namespace Eleu.Tests;

public class PunctuationTests
{
    private static List<(string Word, string? Proofing, string? Reason)> Errors(string text) =>
        Punctuation.Find(text).Select(e => (e.Word, e.Suggestion, e.Reason)).ToList();

    [Theory]
    [InlineData("Ciao , come stai?", " ,", ",")]
    [InlineData("Arrivo subito .", " .", ".")]
    [InlineData("Ciao,come stai?", ",c", ", c")]
    [InlineData("Fine.Poi riparto.", ".P", ". P")]
    [InlineData("Uno  due", "  ", " ")]
    [InlineData("Uno,, due", ",,", ",")]
    [InlineData("È tardi. andiamo", "andiamo", "Andiamo")]
    [InlineData("Ho visto il il gatto", "il il", "il")]
    [InlineData("Bevo l' acqua", "l' a", "l'a")]
    [InlineData("Non so perchè", "perchè", "perché")]
    [InlineData("Perchè no?", "Perchè", "Perché")]
    [InlineData("Aspetta un pò", "pò", "po'")]
    [InlineData("Qual'è il problema?", "Qual'è", "Qual è")]
    [InlineData("E' vero", "E'", "È")]
    public void Find_ErrorAndWithCorrection(string text, string word, string proofing)
    {
        var error = Assert.Single(Errors(text));
        Assert.Equal((word, proofing), (error.Word, error.Proofing));
        Assert.NotNull(error.Reason);
    }

    [Theory]
    [InlineData("Costa 3,5 euro e pesa 1.000 grammi.")]
    [InlineData("Visita www.sito.it per sapere.")]
    [InlineData("Uova, farina, zucchero ecc. e poi il latte.")]
    [InlineData("Ne parla A. Manzoni nel romanzo.")]
    [InlineData("Lo disse il dott. rossi.")]
    [InlineData("Aspetta... poi vedremo.")]
    [InlineData("Non è vero (lo so).")]
    [InlineData("L'acqua è fresca e il cane dorme.")]
    public void Find_NothingInCorrectText(string text) => Assert.Empty(Errors(text));

    [Fact]
    public void Brackets_UnclosedAndClosedWithoutOpening()
    {
        var errors = Errors("Primo (aperto\nSecondo) chiuso");
        Assert.Equal(["parentesi aperta e non chiusa", "parentesi chiusa senza essere aperta"], errors.Select(e => e.Reason));
        Assert.All(errors, e => Assert.Null(e.Proofing));
    }
}

public class SpokenTests
{
    [Theory]
    [InlineData(" ,", "spazio virgola")]
    [InlineData(" ", "uno spazio")]
    [InlineData("  ", "spazio spazio")]
    [InlineData(",,", "virgola virgola")]
    [InlineData("perché", "perché")]
    [InlineData("l' a", "l apostrofo spazio a")]
    [InlineData("il il", "il il")]
    [InlineData("Qual'è", "Qual apostrofo è")]
    public void Of_SignsToWords(string text, string expected) => Assert.Equal(expected, Spoken.Of(text));
}
