using Eleu.Braille;

namespace Eleu.Tests;

/// <summary>Interlinea doppia, fronte e retro e righe libere per la piega, nella pagina Braille.</summary>
public class BrailleStampaTests
{
    private const char V = TraduttoreBraille.CellaVuota;

    private static string Celle(string testo) => new(testo.Select(c => c == ' ' ? V : '⠿').ToArray());

    private static string Traduci(string testo) => "#" + testo;

    /// <summary>Tanti paragrafi da una riga ciascuno, per riempire più pagine.</summary>
    private static List<ParagrafoBraille> Righe(int quante) =>
        Enumerable.Range(0, quante).Select(_ => new ParagrafoBraille(Celle("uno due tre"))).ToList();

    private static bool Vuota(string riga) => riga.Trim(V).Length == 0;

    [Fact]
    public void InterlineaDoppia_UnaRigaVuotaTraLeRighe()
    {
        var formato = new FormatoPagina(20, 25) { InterlineaDoppia = true };
        var pagina = Impaginatore.Impagina(Righe(5), formato)[0];
        // testo, vuota, testo, vuota...: nessuna riga di testo attaccata alla successiva.
        for (int i = 0; i + 1 < pagina.Count; i++)
            Assert.False(!Vuota(pagina[i]) && !Vuota(pagina[i + 1]), $"righe {i} e {i + 1} attaccate");
        Assert.Equal(5, pagina.Count(r => !Vuota(r)));
    }

    [Fact]
    public void InterlineaDoppia_PiuPagine()
    {
        var singola = Impaginatore.Impagina(Righe(30), new FormatoPagina(20, 25)).Count;
        var doppia = Impaginatore.Impagina(Righe(30), new FormatoPagina(20, 25) { InterlineaDoppia = true }).Count;
        Assert.True(doppia > singola);
    }

    [Theory]
    [InlineData(27, NumeroPaginaBraille.InAltoADestra)]
    [InlineData(28, NumeroPaginaBraille.InBassoADestra)]
    [InlineData(25, NumeroPaginaBraille.Nessuno)]
    public void Piega_DueRigheVuoteAMetaPagina(int righe, NumeroPaginaBraille numero)
    {
        var formato = new FormatoPagina(20, righe) { PiegaAMeta = true, Numero = numero };
        var pagine = Impaginatore.Impagina(Righe(100), formato, Traduci);
        var (prima, seconda) = formato.RighePiega;
        Assert.Equal(righe / 2 - 1, prima);
        foreach (var pagina in pagine.Take(pagine.Count - 1)) // l'ultima può finire prima della piega
        {
            Assert.Equal(righe, pagina.Count);
            Assert.True(Vuota(pagina[prima]) && Vuota(pagina[seconda]));
            Assert.False(Vuota(pagina[prima - 1]));
            Assert.False(Vuota(pagina[seconda + 1]));
        }
    }

    [Fact]
    public void Piega_IlTestoNonSiPerde()
    {
        var senza = Impaginatore.Impagina(Righe(60), new FormatoPagina(20, 27)).SelectMany(p => p).Count(r => !Vuota(r));
        var con = Impaginatore.Impagina(Righe(60), new FormatoPagina(20, 27) { PiegaAMeta = true }).SelectMany(p => p).Count(r => !Vuota(r));
        Assert.Equal(60, senza);
        Assert.Equal(60, con);
    }

    [Fact]
    public void FronteRetro_OgniPaginaNumerata_SulRetroMargineANumeroADestraDelMargine()
    {
        var formato = new FormatoPagina(20, 10) { Numero = NumeroPaginaBraille.InAltoADestra, MargineSinistro = 3, FronteRetro = true };
        var pagine = Impaginatore.Impagina(Righe(40), formato, Traduci);
        Assert.True(pagine.Count >= 4);
        for (int i = 0; i < pagine.Count; i++)
        {
            var testa = pagine[i][0];
            var numero = "#" + (i + 1);
            Assert.Contains(numero, testa);
            bool retro = i % 2 == 1;
            if (retro)
            {
                // Sul retro: niente margine a sinistra, il numero finisce 3 celle prima del bordo destro.
                Assert.Equal(20 - 3, testa.Length);
                Assert.EndsWith(numero, testa);
                Assert.StartsWith(new string(V, 2) + "⠿", pagine[i][1]); // solo il rientro del paragrafo
            }
            else
            {
                Assert.Equal(20, testa.Length);
                Assert.EndsWith(numero, testa);
                Assert.StartsWith(new string(V, 5) + "⠿", pagine[i][1]); // margine e rientro
            }
        }
    }

    [Fact]
    public void SenzaFronteRetro_MargineSempreASinistra()
    {
        var formato = new FormatoPagina(20, 10) { MargineSinistro = 2 };
        var pagine = Impaginatore.Impagina(Righe(40), formato);
        Assert.All(pagine, p => Assert.StartsWith(new string(V, 2), p[0]));
    }
}
