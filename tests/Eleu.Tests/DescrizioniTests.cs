using Eleu.Documento;
using Eleu.Editor;

namespace Eleu.Tests;

public class DescrizioniTests
{
    [Fact]
    public void Statistiche_ContaParoleCaratteriParagrafi()
    {
        var statistiche = Statistiche.Calcola("Ciao, mondo!\n\nL'arte è bella.");
        Assert.Equal(new Statistiche(5, 27, 2), statistiche);
        Assert.Equal("5 parole, 27 caratteri, 2 paragrafi", statistiche.Descrizione());
    }

    [Fact]
    public void Statistiche_Singolari()
    {
        Assert.Equal("1 parola, 1 carattere, 1 paragrafo", Statistiche.Calcola("a").Descrizione());
    }

    private static readonly FormatoParagrafo ParagrafoNormale =
        new(Allineamento.Sinistra, 0, 0, 0, 0, 0, 1f, TipoElenco.Nessuno);

    [Fact]
    public void Formato_TitoloCentratoGrassetto()
    {
        var carattere = new FormatoCarattere("Segoe UI", 18, true, false, false, false, PosizioneTesto.Normale,
            new ValoreColore(null), new ValoreColore(null), 0);
        Assert.Equal("Titolo 1, Segoe UI 18 punti, grassetto, centrato",
            DescrizioneFormato.Descrivi(1, carattere, ParagrafoNormale with { Allineamento = Allineamento.Centro }));
    }

    [Fact]
    public void Formato_TuttoQuelloCheSiDiscostaDalNormale()
    {
        var carattere = new FormatoCarattere(null, null, null, false, true, true, PosizioneTesto.Apice,
            new ValoreColore(0xC00000), new ValoreColore(0xFFFF00), 1.5f);
        var paragrafo = new FormatoParagrafo(Allineamento.Giustificato, 36, 0, -18, 6, 12, 1.5f, TipoElenco.Numerato);
        Assert.Equal(
            "Testo normale, caratteri misti, dimensioni miste, in parte grassetto, sottolineato, barrato, apice, " +
            "colore rosso scuro, sfondo giallo, spaziatura 1,5 punti, giustificato, elenco numerato, " +
            "interlinea una e mezza, rientro sinistro 1,3 centimetri, prima riga sporgente di 0,6 centimetri, " +
            "spazio prima 6 punti, spazio dopo 12 punti",
            DescrizioneFormato.Descrivi(0, carattere, paragrafo));
    }

    [Fact]
    public void Contrasto_SecondoLeRegoleW3C()
    {
        Assert.Equal(21, Colori.Contrasto(Colori.Nero, Colori.Bianco), 1);
        Assert.Equal("Contrasto 21:1, ottimo.", Colori.GiudizioContrasto(null, null));
        Assert.EndsWith("insufficiente: difficile da leggere.", Colori.GiudizioContrasto(0xFFFF00, null));
        Assert.EndsWith("buono.", Colori.GiudizioContrasto(0x0070C0, null));
        Assert.Equal("rosso scuro", Colori.Nome(0xC00000));
        Assert.Equal("personalizzato (123456)", Colori.Nome(0x123456));
    }
}
