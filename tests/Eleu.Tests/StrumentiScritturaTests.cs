using Eleu.Correzione;
using Eleu.Documento;

namespace Eleu.Tests;

public class PuliziaTests
{
    private static string Pulisci(string testo, OpzioniPulizia opzioni) => Pulizia.Applica(testo, Pulizia.Modifiche(testo, opzioni));

    [Fact]
    public void SpaziDoppiEAiBordi() =>
        Assert.Equal("Uno due\nTre", Pulisci("  Uno   due  \nTre", new OpzioniPulizia()));

    [Fact]
    public void RigheVuoteRipetute_NeRestaUna() =>
        Assert.Equal("Uno\n\nDue", Pulisci("Uno\n\n\n\nDue", new OpzioniPulizia()));

    [Fact]
    public void Sillabazione_ParolaRicucita() =>
        Assert.Equal("ricostruire la casa", Pulisci("rico-\nstruire la casa", new OpzioniPulizia()));

    [Fact]
    public void ParagrafiSpezzati_SoloSeChiesto()
    {
        const string testo = "La casa era\ngrande. Poi\nArrivò lui.";
        Assert.Equal(testo, Pulisci(testo, new OpzioniPulizia()));
        Assert.Equal("La casa era grande. Poi\nArrivò lui.", Pulisci(testo, new OpzioniPulizia(ParagrafiSpezzati: true)));
    }

    [Fact]
    public void TabelleNonToccate()
    {
        var testo = string.Concat("a  b\n", StrutturaTesto.InizioRiga, "\n", "x  y", StrutturaTesto.FineCella, StrutturaTesto.FineRiga, "\n", "c  d");
        var pulito = Pulisci(testo, new OpzioniPulizia());
        Assert.StartsWith("a b\n", pulito);
        Assert.Contains("x  y", pulito);
        Assert.EndsWith("c d", pulito);
    }
}

public class SostituzioniMultipleTests
{
    [Fact]
    public void TutteInUnColpo_SenzaCatena()
    {
        const string testo = "il gatto e il cane";
        var modifiche = SostituzioniMultiple.Modifiche(testo,
            [new("gatto", "cane"), new("cane", "topo")], new OpzioniRicerca(ParolaIntera: true));
        // "gatto" diventa "cane" ma quel "cane" nuovo non diventa "topo": si cerca nel testo di partenza.
        Assert.Equal("il cane e il topo", Pulizia.Applica(testo, modifiche));
    }
}

public class StatisticheCompleteTests
{
    [Fact]
    public void Conta_ConFrasiEApostrofi()
    {
        var s = StatisticheComplete.Calcola("L'acqua è fresca. Bevo 2 bicchieri!\nPoi basta");
        Assert.Equal(9, s.Parole); // L acqua è fresca Bevo 2 bicchieri Poi basta
        Assert.Equal(3, s.Frasi);
        Assert.Equal(2, s.Paragrafi);
        Assert.Equal(1, s.Cifre);
        Assert.Equal(s.Caratteri - s.Spazi, s.CaratteriSenzaSpazi);
        Assert.NotNull(s.Gulpease);
    }

    [Fact]
    public void Gulpease_FrasiBreviFacili_FrasiLungheDifficili()
    {
        var facile = StatisticheComplete.Calcola("Il gatto dorme. Il cane abbaia. Io leggo.");
        var difficile = StatisticheComplete.Calcola(
            "L'implementazione dell'infrastruttura amministrativa regionale presuppone un'articolata concertazione interistituzionale.");
        Assert.True(facile.Gulpease > 80, $"facile: {facile.Gulpease}");
        Assert.True(difficile.Gulpease < 40, $"difficile: {difficile.Gulpease}");
        Assert.Equal("molto facile", facile.Giudizio);
    }
}

public class ConfrontoTests
{
    [Fact]
    public void Differenze_CambiatoAggiuntoTolto()
    {
        string[] qui = ["Titolo", "Il gatto dorme", "Fine", "Solo qui"];
        string[] altro = ["Titolo", "Nuovo in cima al corpo", "Il cane dorme", "Fine"];
        var differenze = Confronto.Differenze(qui, altro);
        Assert.Equal(3, differenze.Count);
        Assert.Contains(differenze, d => d.Tipo == TipoDifferenza.Cambiato && d.Qui == 1 && d.TestoAltro == "Il cane dorme"
            || d.Tipo == TipoDifferenza.Cambiato && d.Qui == 1);
        Assert.Contains(differenze, d => d.Tipo == TipoDifferenza.SoloQui && d.TestoQui == "Solo qui");
        Assert.Contains(differenze, d => d.Tipo == TipoDifferenza.SoloNellAltro || d.Tipo == TipoDifferenza.Cambiato && d.TestoAltro.StartsWith("Nuovo"));
    }

    [Fact]
    public void Uguali_NessunaDifferenza_ancheConSpaziDiversi() =>
        Assert.Empty(Confronto.Differenze(["Uno  due", "tre"], ["Uno due", "tre "]));

    [Fact]
    public void ParoleCambiate_DiceCosaCambia() =>
        Assert.Equal("Qui: «gatto»; nell'altro: «cane».", Confronto.ParoleCambiate("Il gatto dorme", "Il cane dorme"));
}
