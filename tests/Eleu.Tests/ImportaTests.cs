using System.Drawing;
using System.Drawing.Imaging;
using Eleu.Correzione;
using Eleu.Importa;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace Eleu.Tests;

public class PuliziaTestoTests
{
    [Fact]
    public void RicuceLeParoleDiviseEUnisceLeRighe()
    {
        IReadOnlyList<string> pagina = ["Il gatto cammina sul tet-", "to della casa e guarda", "il cielo.", "", "Nuovo paragrafo qui."];
        Assert.Equal(["Il gatto cammina sul tetto della casa e guarda il cielo.", "Nuovo paragrafo qui."],
            PuliziaTesto.Paragrafi([pagina]));
    }

    [Fact]
    public void TogliNumeriDiPaginaEIntestazioniRipetute()
    {
        IReadOnlyList<string> Pagina(int n, string testo) => ["Rivista di cucina - numero 7", testo, $"- {n} -"];
        var paragrafi = PuliziaTesto.Paragrafi([
            Pagina(1, "La pasta si cuoce in acqua salata e"),
            Pagina(2, "si scola quando è al dente, poi si condisce."),
            Pagina(3, "Fine della ricetta."),
        ]);
        Assert.Equal(["La pasta si cuoce in acqua salata e si scola quando è al dente, poi si condisce. Fine della ricetta."], paragrafi);
    }

    [Fact]
    public void RigaLungaCheFinisceColPunto_NonChiudeIlParagrafo()
    {
        IReadOnlyList<string> pagina = ["Questa riga è lunga e finisce proprio con un punto.", "Questa invece continua lo stesso paragrafo e va", "avanti."];
        Assert.Single(PuliziaTesto.Paragrafi([pagina]));
    }

    [Theory]
    [InlineData("12")]
    [InlineData("Pag. 3")]
    [InlineData("pagina 4 di 20")]
    [InlineData("5/20")]
    public void NumeriDiPaginaRiconosciuti(string numero)
    {
        IReadOnlyList<string> pagina = ["Testo della pagina che non va toccato.", numero];
        Assert.Equal(["Testo della pagina che non va toccato."], PuliziaTesto.Paragrafi([pagina]));
    }
}

public class ErroriProbabiliTests
{
    private static readonly HashSet<string> Dizionario = ["come", "casa", "stai", "il", "maggio", "una", "frase"];

    private static List<ErroreProbabile> Trova(string testo) => ErroriProbabili.Trova(
        testo,
        t => System.Text.RegularExpressions.Regex.Matches(t, @"\p{L}+")
            .Where(m => !Dizionario.Contains(m.Value.ToLowerInvariant()))
            .Select(m => (m.Index, m.Length)),
        p => p == "frse" ? ["frase"] : [],
        p => Dizionario.Contains(p));

    [Fact]
    public void CifreAlPostoDiLettere_ConSuggerimento()
    {
        var errore = Assert.Single(Trova("c0me stai"));
        Assert.Equal(new ErroreProbabile(0, 4, "c0me", "come"), errore);
    }

    [Fact]
    public void CaratteriSpuriDellOcr()
    {
        Assert.Equal("casa", Assert.Single(Trova("ca|sa")).Suggerimento);
    }

    [Fact]
    public void OrdinaliENumeriNonSonoErrori()
    {
        Assert.Empty(Trova("il 1° maggio 2026"));
    }

    [Fact]
    public void ParoleFuoriDizionario_InOrdine()
    {
        var errori = Trova("una frse c0me");
        Assert.Equal(["frse", "c0me"], errori.Select(e => e.Parola));
        Assert.Null(errori[0].Suggerimento); // chiesto al dizionario solo quando ci si ferma su quell'errore
        Assert.Equal("come", errori[1].Suggerimento);
    }

    [Theory]
    [InlineData("dlvano", new[] { "davano" }, "divano")]        // generato con lo scambio l→i e trovato nel dizionario
    [InlineData("rnare", new[] { "rare", "mare" }, "mare")]     // tra i suggerimenti, quello con uno scambio rn→m
    [InlineData("Clue", new[] { "Clue", "Due" }, "Due")]
    [InlineData("xyz", new[] { "casa", "cosa" }, "casa")]       // nessuno scambio: il primo del dizionario
    public void MigliorSuggerimento_PreferisceGliScambiTipiciDellOcr(string parola, string[] suggerimenti, string atteso)
    {
        Assert.Equal(atteso, ErroriProbabili.MigliorSuggerimento(parola, suggerimenti, p => p == "divano"));
    }

    [Fact]
    public void DueIMaiuscoleAInizioFrase_SonoLarticoloIl()
    {
        var errore = Assert.Single(Trova("II gatto dorme. II cane abbaia."), e => e.Inizio == 0);
        Assert.Equal("Il", errore.Suggerimento);
        Assert.Contains(Trova("II gatto dorme. II cane abbaia."), e => e.Inizio == 16 && e.Suggerimento == "Il");
        Assert.DoesNotContain(Trova("Enrico II è nato"), e => e.Suggerimento == "Il");
    }

    [Fact]
    public void Successivo_AvantiEIndietro()
    {
        var errori = Trova("una frse c0me");
        Assert.Equal("frse", ErroriProbabili.Successivo(errori, 0, avanti: true)!.Parola);
        Assert.Equal("c0me", ErroriProbabili.Successivo(errori, 5, avanti: true)!.Parola);
        Assert.Null(ErroriProbabili.Successivo(errori, 14, avanti: true));
        Assert.Equal("frse", ErroriProbabili.Successivo(errori, 9, avanti: false)!.Parola);
    }
}

/// <summary>Prove con i componenti reali di Windows. Se una macchina non li ha (es. senza italiano), non c'è niente da verificare.</summary>
public class ImportaRealeTests
{
    [Fact]
    public void ControlloOrtograficoItaliano()
    {
        var correttore = ControlloOrtografico.Crea("it-IT");
        if (correttore is null)
            return;

        var sconosciute = correttore.ParoleSconosciute("Questa è una frse sbaglaita.").ToList();
        Assert.Equal([(13, 4), (18, 9)], sconosciute);
        Assert.Contains("frase", correttore.Suggerimenti("frse"));
        Assert.True(correttore.Corretta("perché"));
    }

    [Fact]
    public async Task PdfConTesto_LettoEPulito()
    {
        var percorso = FileTemporaneo(".pdf");
        try
        {
            var costruttore = new PdfDocumentBuilder();
            var carattere = costruttore.AddStandard14Font(Standard14Font.Helvetica);
            string[][] pagine =
            [
                ["Notiziario del circolo", "Questo testo di prova serve a controllare che le parole divi-", "se a fine riga vengano ricucite correttamente.", "1"],
                ["Notiziario del circolo", "Seconda pagina con un altro paragrafo di prova abbastanza lungo.", "2"],
            ];
            foreach (var righe in pagine)
            {
                var pagina = costruttore.AddPage(595, 842);
                for (int i = 0; i < righe.Length; i++)
                    pagina.AddText(righe[i], 11, new PdfPoint(60, 780 - i * 20), carattere);
            }
            File.WriteAllBytes(percorso, costruttore.Build());

            var risultato = await Importatore.ImportaAsync(percorso, null, CancellationToken.None);

            Assert.False(risultato.ConOcr);
            Assert.Equal(2, risultato.Pagine);
            var testo = string.Join("\n", risultato.Paragrafi);
            Assert.Contains("parole divise a fine riga vengano ricucite", testo);
            Assert.DoesNotContain("Notiziario", testo);
            Assert.Contains("Seconda pagina", testo);
        }
        finally
        {
            File.Delete(percorso);
        }
    }

    [Fact]
    public async Task ImmagineEPdfScansionato_LettiConOcr()
    {
        if (!Windows.Media.Ocr.OcrEngine.AvailableRecognizerLanguages.Any())
            return;

        var png = FileTemporaneo(".png");
        var pdf = FileTemporaneo(".pdf");
        try
        {
            using (var immagine = new Bitmap(1600, 400))
            using (var g = Graphics.FromImage(immagine))
            using (var carattere = new Font("Arial", 40))
            {
                g.Clear(Color.White);
                g.DrawString("Il gatto dorme sul divano.", carattere, Brushes.Black, 40, 60);
                g.DrawString("Domani piove in montagna.", carattere, Brushes.Black, 40, 200);
                immagine.Save(png, ImageFormat.Png);
            }

            var daImmagine = await Importatore.ImportaAsync(png, null, CancellationToken.None);
            Assert.True(daImmagine.ConOcr);
            Assert.Contains("gatto dorme sul divano", string.Join(" ", daImmagine.Paragrafi));

            // Un PDF che contiene solo l'immagine, come una scansione: niente testo, si deve passare dall'OCR.
            var costruttore = new PdfDocumentBuilder();
            costruttore.AddPage(595, 842).AddPng(File.ReadAllBytes(png), new PdfRectangle(new PdfPoint(40, 600), new PdfPoint(555, 729)));
            File.WriteAllBytes(pdf, costruttore.Build());

            var avanzamenti = new List<AvanzamentoImportazione>();
            var daPdf = await Importatore.ImportaAsync(pdf, new ProgressoSincrono(avanzamenti.Add), CancellationToken.None);
            Assert.True(daPdf.ConOcr);
            Assert.Equal([new AvanzamentoImportazione(1, 1, true)], avanzamenti);
            Assert.Contains("piove in montagna", string.Join(" ", daPdf.Paragrafi));
        }
        finally
        {
            File.Delete(png);
            File.Delete(pdf);
        }
    }

    private static string FileTemporaneo(string estensione) => Path.Combine(Path.GetTempPath(), $"eleu-test-{Guid.NewGuid():N}{estensione}");

    private sealed class ProgressoSincrono(Action<AvanzamentoImportazione> azione) : IProgress<AvanzamentoImportazione>
    {
        public void Report(AvanzamentoImportazione valore) => azione(valore);
    }
}
