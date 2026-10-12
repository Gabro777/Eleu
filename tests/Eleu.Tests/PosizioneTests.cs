using Eleu.Documento;

namespace Eleu.Tests;

public class PosizioneTests
{
    [Fact]
    public void DocumentoVuoto_RigaUnoColonnaUno()
    {
        Assert.Equal(new Posizione(1, 1, 1), Posizione.Calcola("", 0));
    }

    [Fact]
    public void CursoreInMezzoAllaPrimaRiga()
    {
        Assert.Equal(new Posizione(1, 4, 1), Posizione.Calcola("ciao mondo", 3));
    }

    [Fact]
    public void CursoreAInizioSecondaRiga()
    {
        Assert.Equal(new Posizione(2, 1, 3), Posizione.Calcola("uno\ndue\ntre", 4));
    }

    [Fact]
    public void CursoreAFineDocumento()
    {
        Assert.Equal(new Posizione(3, 4, 3), Posizione.Calcola("uno\ndue\ntre", 11));
    }

    [Fact]
    public void RigaVuotaFinaleContaComeRiga()
    {
        Assert.Equal(new Posizione(2, 1, 2), Posizione.Calcola("uno\n", 4));
    }

    [Theory]
    [InlineData(-5)]
    [InlineData(999)]
    public void IndiceFuoriLimitiVieneRiportatoNelTesto(int indice)
    {
        var posizione = Posizione.Calcola("abc", indice);
        Assert.InRange(posizione.Colonna, 1, 4);
    }

    [Fact]
    public void Descrizione_InItaliano()
    {
        Assert.Equal("Riga 2 di 5, colonna 7", new Posizione(2, 7, 5).Descrizione());
    }
}
