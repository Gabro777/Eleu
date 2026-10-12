using Eleu.Documento;

namespace Eleu.Tests;

public class RicercaTests
{
    private static readonly OpzioniRicerca Normale = new();

    [Fact]
    public void AvantiTrovaLaPrimaDopoIlCursore()
    {
        Assert.Equal(8, Ricerca.Trova("gatto e cane gatto", "cane", 3, true, Normale));
        Assert.Equal(13, Ricerca.Trova("gatto e cane gatto", "gatto", 1, true, Normale));
    }

    [Fact]
    public void IndietroTrovaLUltimaPrimaDelCursore()
    {
        const string testo = "gatto e cane gatto";
        Assert.Equal(0, Ricerca.Trova(testo, "gatto", 13, false, Normale));
        Assert.Equal(13, Ricerca.Trova(testo, "gatto", testo.Length, false, Normale));
        Assert.Equal(-1, Ricerca.Trova(testo, "gatto", 0, false, Normale));
    }

    [Fact]
    public void MaiuscoleIgnorateSalvoRichiesta()
    {
        Assert.Equal(0, Ricerca.Trova("Città", "CITTÀ", 0, true, Normale));
        Assert.Equal(-1, Ricerca.Trova("Città", "CITTÀ", 0, true, new OpzioniRicerca(MaiuscoleMinuscole: true)));
    }

    [Fact]
    public void ParolaIntera()
    {
        var opzioni = new OpzioniRicerca(ParolaIntera: true);
        Assert.Equal(10, Ricerca.Trova("caneide e cane", "cane", 0, true, opzioni));
        Assert.Equal(0, Ricerca.Trova("caneide e cane", "cane", 0, true, Normale));
        Assert.Equal(-1, Ricerca.Trova("caneide", "cane", 7, false, opzioni));
    }

    [Fact]
    public void TrovaTutte_SenzaSovrapposizioni()
    {
        Assert.Equal([0, 2], Ricerca.TrovaTutte("aaaa", "aa", Normale));
        Assert.Empty(Ricerca.TrovaTutte("abc", "", Normale));
    }

    [Fact]
    public void Corrisponde_RispettaLeOpzioni()
    {
        Assert.True(Ricerca.Corrisponde("Roma", "roma", Normale));
        Assert.False(Ricerca.Corrisponde("Roma", "roma", new OpzioniRicerca(MaiuscoleMinuscole: true)));
    }
}

public class RicercaJollyTests
{
    private static readonly OpzioniRicerca Jolly = new(CaratteriJolly: true);

    private static string? Trovato(string testo, string cerca, OpzioniRicerca opzioni, int da = 0, bool avanti = true) =>
        Ricerca.TrovaCorrispondenza(testo, cerca, da, avanti, opzioni) is var (inizio, lunghezza) ? testo.Substring(inizio, lunghezza) : null;

    [Fact]
    public void PuntoInterrogativo_UnCarattere() => Assert.Equal("gatto", Trovato("il gatto", "g?tto", Jolly));

    [Fact]
    public void AsteriscoInFondo_CompletaLaParola() => Assert.Equal("casale", Trovato("una casale grande", "cas*", Jolly));

    [Fact]
    public void AsteriscoInMezzo_IlMenoPossibile() => Assert.Equal("cane e il gatto", Trovato("il cane e il gatto e il gatto", "c*gatto", Jolly));

    [Fact]
    public void Cancelletto_UnaCifra() => Assert.Equal("2026", Trovato("anno 2026", "20##", Jolly));

    [Fact]
    public void AltriCaratteriSonoNormali() => Assert.Equal("(a+b)", Trovato("x (a+b) y", "(a+b)", Jolly));

    [Fact]
    public void ParolaInteraEAllIndietro()
    {
        var opzioni = Jolly with { ParolaIntera = true };
        Assert.Equal("gatta", Trovato("gattara gatta", "gatt?", opzioni));
        Assert.Equal("gatto", Trovato("gatto gatta", "gatt?", Jolly, da: 6, avanti: false));
    }

    [Fact]
    public void TutteECorrisponde()
    {
        Assert.Equal([(0, 4), (5, 6)], Ricerca.TrovaTutteCorrispondenze("casa casale", "cas*", Jolly));
        Assert.True(Ricerca.Corrisponde("Casale", "cas*", Jolly));
        Assert.False(Ricerca.Corrisponde("casale!", "cas*", Jolly));
    }
}
