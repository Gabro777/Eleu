using Eleu.Braille;

namespace Eleu.Tests;

public class TastieraBrailleTests
{
    private static readonly TastieraBraille Tastiera = new(TraduttoreBraille.Traduci);

    /// <summary>Preme insieme i tasti dei punti indicati (per esempio "125") e li lascia: restituisce cosa si scrive.</summary>
    private static string? Cella(TastieraBraille tastiera, string punti)
    {
        Keys[] tasti = [Keys.F, Keys.D, Keys.S, Keys.J, Keys.K, Keys.L];
        var premuti = punti.Select(p => tasti[p - '1']).ToList();
        foreach (var t in premuti)
            tastiera.Premi(t);
        string? scritto = null;
        foreach (var t in premuti)
            scritto = tastiera.Rilascia(t) ?? scritto;
        return scritto;
    }

    private static string Cella(string punti) =>
        ((char)(TraduttoreBraille.CellaVuota + punti.Sum(p => 1 << (p - '1')))).ToString();

    [Theory]
    [InlineData("1", "a")]
    [InlineData("12", "b")]
    [InlineData("14", "c")]
    [InlineData("1345", "n")]
    [InlineData("2345", "t")]
    public void Lettere(string punti, string lettera) => Assert.Equal(lettera, Cella(new TastieraBraille(TraduttoreBraille.Traduci), punti));

    [Fact]
    public void FinoAlRilascioDiTuttiNonSiScrive()
    {
        var tastiera = new TastieraBraille(TraduttoreBraille.Traduci);
        tastiera.Premi(Keys.F);
        tastiera.Premi(Keys.D);
        Assert.Null(tastiera.Rilascia(Keys.F)); // D è ancora premuto
        Assert.Equal("b", tastiera.Rilascia(Keys.D));
    }

    [Fact]
    public void OgniCarattereSiRiscriveDalleSueCelle()
    {
        // Per ogni carattere: le celle che gli dà liblouis, battute una alla volta, devono ridare quel carattere.
        foreach (var c in "abcxyzàèéìòù.,;:!?ABZ0123456789")
        {
            var tastiera = new TastieraBraille(TraduttoreBraille.Traduci);
            string? scritto = null;
            foreach (var cella in TraduttoreBraille.Traduci(c.ToString()))
            {
                int bit = cella - TraduttoreBraille.CellaVuota;
                var punti = string.Concat(Enumerable.Range(0, 6).Where(i => (bit & (1 << i)) != 0).Select(i => (char)('1' + i)));
                scritto = Cella(tastiera, punti);
            }
            Assert.True(scritto == c.ToString() || char.ToLower(scritto?[0] ?? ' ') == char.ToLower(c), $"«{c}» è tornato «{scritto}»");
        }
    }

    [Fact]
    public void Numeri_UnSegnoPerTutteLeCifre_PoiDiNuovoLettere()
    {
        var tastiera = new TastieraBraille(TraduttoreBraille.Traduci);
        var segno = TraduttoreBraille.Traduci("1")[0] - TraduttoreBraille.CellaVuota;
        var puntiSegno = string.Concat(Enumerable.Range(0, 6).Where(i => (segno & (1 << i)) != 0).Select(i => (char)('1' + i)));
        var scritto = new List<string?> { Cella(tastiera, puntiSegno), Cella(tastiera, "1"), Cella(tastiera, "12") };
        tastiera.Azzera(); // come la barra spaziatrice: finisce il numero
        scritto.Add(Cella(tastiera, "1"));
        Assert.Equal([null, "1", "2", "a"], scritto);

        // Il segno senza cifre dopo era un "#" (se il Braille italiano lo usa per "#").
        Assert.Null(Cella(tastiera, puntiSegno));
        var dopo = Cella(tastiera, "2345");
        Assert.EndsWith("t", dopo);
    }

    [Fact]
    public void CellaSenzaCarattere_SiScriveComeCella()
    {
        // Una cella che non è nessun carattere (né l'inizio di uno) resta la cella Braille.
        var tastiera = new TastieraBraille(TraduttoreBraille.Traduci);
        var tutte = Enumerable.Range(1, 63).Select(b => ((char)(TraduttoreBraille.CellaVuota + b)).ToString());
        var senza = tutte.FirstOrDefault(c => !"abcdefghijklmnopqrstuvwxyzàèéìòùç.,;:!?'\"()-/*+=<>@#%&_0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ"
            .Any(x => TraduttoreBraille.Traduci(x.ToString()).StartsWith(c)));
        if (senza is null)
            return;
        int bit = senza[0] - TraduttoreBraille.CellaVuota;
        var punti = string.Concat(Enumerable.Range(0, 6).Where(i => (bit & (1 << i)) != 0).Select(i => (char)('1' + i)));
        Assert.Equal(senza, Cella(tastiera, punti));
    }
}
