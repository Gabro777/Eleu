using Eleu.Correzione;

namespace Eleu.Tests;

public class PunteggiaturaTests
{
    private static List<(string Parola, string? Correzione, string? Motivo)> Errori(string testo) =>
        Punteggiatura.Trova(testo).Select(e => (e.Parola, e.Suggerimento, e.Motivo)).ToList();

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
    public void Trova_ErroreEConCorrezione(string testo, string parola, string correzione)
    {
        var errore = Assert.Single(Errori(testo));
        Assert.Equal((parola, correzione), (errore.Parola, errore.Correzione));
        Assert.NotNull(errore.Motivo);
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
    public void Trova_NienteSuTestoCorretto(string testo) => Assert.Empty(Errori(testo));

    [Fact]
    public void Parentesi_NonChiuseEChiuseSenzaAprire()
    {
        var errori = Errori("Primo (aperto\nSecondo) chiuso");
        Assert.Equal(["parentesi aperta e non chiusa", "parentesi chiusa senza essere aperta"], errori.Select(e => e.Motivo));
        Assert.All(errori, e => Assert.Null(e.Correzione));
    }
}

public class ParlatoTests
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
    public void Di_SegniAParole(string testo, string atteso) => Assert.Equal(atteso, Parlato.Di(testo));
}
