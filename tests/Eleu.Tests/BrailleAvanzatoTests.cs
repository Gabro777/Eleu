using Eleu.Braille;

namespace Eleu.Tests;

public class BrailleAvanzatoTests
{
    private const char V = TraduttoreBraille.CellaVuota;

    // Per i test "traduzione" finta: ogni carattere diventa una cella piena, così si contano le celle.
    private static string Celle(string testo) => new(testo.Select(c => c == ' ' ? V : '⠿').ToArray());

    private static string Traduci(string testo) => "#" + testo; // numeri riconoscibili: "#3"

    private static List<ParagrafoBraille> Libro(int paragrafi, int paroleOgnuno = 10, bool conTitoli = true)
    {
        var lista = new List<ParagrafoBraille>();
        for (int i = 0; i < paragrafi; i++)
        {
            if (conTitoli && i % 3 == 0)
                lista.Add(new ParagrafoBraille(Celle($"Capitolo {i}"), Titolo: true, Livello: 1, PaginaStampa: i / 2 + 1));
            lista.Add(new ParagrafoBraille(Celle(string.Join(" ", Enumerable.Repeat("parola", paroleOgnuno))), PaginaStampa: i / 2 + 1));
        }
        return lista;
    }

    [Fact]
    public void NumeroInAlto_ConTitoloCorrente()
    {
        var formato = new FormatoPagina(20, 10) { Numero = NumeroPaginaBraille.InAltoADestra, Titolo = Celle("Ricette") };
        var pagine = Impaginatore.Impagina(Libro(6), formato, Traduci);
        Assert.True(pagine.Count > 1);
        for (int i = 0; i < pagine.Count; i++)
        {
            Assert.True(pagine[i].Count <= 10);
            Assert.Equal(20, pagine[i][0].Length);
            Assert.StartsWith(Celle("Ricette"), pagine[i][0]);
            Assert.EndsWith($"#{i + 1}", pagine[i][0]);
        }
    }

    [Fact]
    public void NumeroInFondo_SempreSullUltimaRiga()
    {
        var formato = new FormatoPagina(20, 8) { Numero = NumeroPaginaBraille.InBassoADestra };
        var pagine = Impaginatore.Impagina(Libro(5), formato, Traduci);
        for (int i = 0; i < pagine.Count; i++)
        {
            Assert.Equal(8, pagine[i].Count);
            Assert.EndsWith($"#{i + 1}", pagine[i][^1]);
        }
    }

    [Fact]
    public void Margine_SpostaIlTestoESiStaNellaRiga()
    {
        var formato = new FormatoPagina(20, 25) { MargineSinistro = 3 };
        var pagine = Impaginatore.Impagina(Libro(2, conTitoli: false), formato);
        Assert.All(pagine.SelectMany(p => p).Where(r => r.Length > 0), r =>
        {
            Assert.StartsWith(new string(V, 3), r);
            Assert.True(r.Length <= 20);
        });
    }

    [Fact]
    public void PagineStampa_UnaRigaConIlNumeroQuandoCambia()
    {
        var formato = new FormatoPagina(20, 40) { PagineStampa = true };
        var righe = Impaginatore.Impagina(Libro(6, 3, conTitoli: false), formato, Traduci).SelectMany(p => p).ToList();
        var segni = righe.Where(r => r.StartsWith(Impaginatore.Trattino)).ToList();
        Assert.Equal(2, segni.Count); // pagine a stampa 1, 2, 3: si segnano il passaggio a 2 e a 3
        Assert.EndsWith("#2", segni[0]);
        Assert.EndsWith("#3", segni[1]);
        Assert.All(segni, s => Assert.Equal(20, s.Length));
    }

    [Fact]
    public void Sommario_InCimaConLePagineGiuste()
    {
        var formato = new FormatoPagina(24, 8) { Sommario = true, Numero = NumeroPaginaBraille.InAltoADestra };
        var pagine = Impaginatore.Impagina(Libro(12), formato, Traduci);
        Assert.Contains("#Sommario", pagine[0][1]); // riga 0: numero di pagina; riga 1: "Sommario"

        // Per ogni voce del sommario, la pagina indicata comincia (o contiene) quel titolo.
        var voci = pagine.SelectMany(p => p.Skip(1)).Where(r => r.Contains(Impaginatore.Puntino)).ToList();
        Assert.Equal(4, voci.Count);
        for (int v = 0; v < voci.Count; v++)
        {
            int numero = int.Parse(voci[v][(voci[v].LastIndexOf('#') + 1)..]);
            Assert.Contains(pagine[numero - 1].Skip(1), r => r.StartsWith(Celle($"Capitolo {v * 3}")) && !r.Contains(Impaginatore.Puntino));
        }
    }
}

public class FinestraBrailleTests
{
    [Fact]
    public void Intervallo_SegueLePagineQuandoSiPrendevaTutto()
    {
        (int, int) prima = default, dopo = default;
        Supporto.EseguiSta(() =>
        {
            // Il sommario aggiunge una pagina: 5 senza, 6 con.
            using var finestra = new Interfaccia.FinestraBraille([], null, new FormatoPagina(), f => f.Sommario ? 6 : 5, conFile: true);
            prima = finestra.Intervallo;
            var casella = Tutti(finestra).OfType<CheckBox>().First(c => c.Text.Contains("mmario"));
            casella.Checked = true;
            dopo = finestra.Intervallo;
        });
        Assert.Equal((1, 5), prima);
        Assert.Equal((1, 6), dopo);

        static IEnumerable<Control> Tutti(Control radice) => radice.Controls.Cast<Control>().SelectMany(c => Tutti(c).Prepend(c));
    }
}
