namespace Eleu.Tests;

public class ParoleTests
{
    [Fact]
    public void Frequenze_DallaPiuUsata_SenzaParoleCorte()
    {
        var f = FinestraPrincipale.Frequenze("Il gatto e il cane. Il Gatto dorme; il gatto mangia, il cane no.");
        Assert.Equal(("gatto", 3), f[0]);
        Assert.Equal(("cane", 2), f[1]);
        Assert.DoesNotContain(f, x => x.Parola is "il" or "e" or "no");
    }

    [Theory]
    [InlineData("20AC", "€")]
    [InlineData("U+20ac", "€")]
    [InlineData("0xE8", "è")]
    [InlineData("1F600", "😀")]
    public void Simbolo_DaCodice(string codice, string atteso) =>
        Assert.Equal(atteso, Interfaccia.FinestraSimboli.DaCodice(codice));

    [Theory]
    [InlineData("ciao")]
    [InlineData("D800")]
    [InlineData("1F")]
    [InlineData("")]
    public void Simbolo_CodiceNonValido(string codice) =>
        Assert.Null(Interfaccia.FinestraSimboli.DaCodice(codice));

    [Fact]
    public void Simboli_TuttiConUnNome() =>
        Assert.All(Interfaccia.FinestraSimboli.Simboli, s => Assert.False(string.IsNullOrWhiteSpace(s.Nome)));
}
