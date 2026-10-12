using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using Eleu.Braille;
using Eleu.Documento;
using Eleu.Editor;
using static Eleu.Tests.Supporto;

namespace Eleu.Tests;

public class PaginaTests
{
    private static readonly ImpostazioniPagina Personalizzata = new(
        ImpostazioniPagina.Formati[1].Larghezza, ImpostazioniPagina.Formati[1].Altezza, Orizzontale: true,
        MargineSinistro: 72, MargineDestro: 36, MargineSopra: 54, MargineSotto: 90,
        DistanzaIntestazione: 36, DistanzaPiede: 18,
        Intestazione: "Relazione annuale: {titolo}", PiePagina: "Riservato - pagina {pagina} di {pagine}", EscludiPrimaPagina: true);

    [Fact]
    public void Docx_ConservaFoglioMarginiIntestazioneENumeri()
    {
        var contenuto = Esempio();
        contenuto.Pagina = Personalizzata;
        using var flusso = new MemoryStream();
        DocxFormato.Scrivi(contenuto, flusso);

        flusso.Position = 0;
        using (var documento = WordprocessingDocument.Open(flusso, false))
            Assert.Empty(new OpenXmlValidator().Validate(documento).Select(e => $"{e.Path?.XPath}: {e.Description}"));

        flusso.Position = 0;
        var riletto = DocxFormato.Leggi(flusso);
        Assert.Equal(Personalizzata, riletto.Pagina);
        Assert.Equal("A5", riletto.Pagina.Formato?.Nome);
        Assert.Empty(riletto.ElementiNonConservati);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("Pagina {pagina}", false)]
    [InlineData("Riservato - pagina {pagina} di {pagine}, {data}", true)]
    public void Rtf_ConservaLaPagina(string piede, bool escludiPrima)
    {
        var pagina = Personalizzata with { PiePagina = piede, EscludiPrimaPagina = escludiPrima };
        var rtf = RtfScrittore.ConPagina(RtfScrittore.Scrivi(Esempio()), pagina);
        var riletto = RtfLettore.Leggi(rtf);
        Assert.Equal(pagina, riletto.Pagina);
        // Intestazione e piè di pagina non finiscono nel testo del documento.
        Assert.DoesNotContain(riletto.Paragrafi, p => p.Testo.Contains("Relazione annuale") || p.Testo.Contains("Riservato"));
    }

    [Fact]
    public void RtfDiWord_ConPageDentroIlParagrafo()
    {
        var contenuto = RtfLettore.Leggi(@"{\rtf1\ansi\pard Prima\par\pard Seconda\page Terza\par}");
        Assert.Equal(["Prima", "Seconda", "Terza"], contenuto.Paragrafi.Select(p => p.Testo));
        Assert.Equal([false, false, true], contenuto.Paragrafi.Select(p => p.SaltoPagina));
    }

    [Fact]
    public void Paginatore_DivideITestiLunghiERispettaLeInterruzioni()
    {
        List<int> inizi = [];
        int lunghezza = 0, posizioneInterruzione = 0;
        EseguiSta(() =>
        {
            using var campo = new RichTextBox();
            campo.CreateControl();
            var paragrafo = string.Concat(Enumerable.Repeat("Una riga di testo abbastanza lunga da occupare spazio sulla pagina. ", 8));
            var testo = string.Join("\n", Enumerable.Repeat(paragrafo, 40));
            posizioneInterruzione = testo.Length + 1;
            campo.Text = testo + "\n" + Paginatore.SaltoPagina + "Dopo l'interruzione.";
            lunghezza = campo.TextLength;
            inizi = Paginatore.InizioPagine(campo, new ImpostazioniPagina());
        });

        Assert.True(inizi.Count >= 4, $"Solo {inizi.Count} pagine");
        Assert.Equal(0, inizi[0]);
        Assert.Equal(inizi.Order(), inizi);
        Assert.Contains(posizioneInterruzione, inizi);
        Assert.DoesNotContain(posizioneInterruzione + 1, inizi); // niente pagina vuota con il solo salto pagina
        Assert.Equal(posizioneInterruzione, inizi[^1]); // il testo dopo l'interruzione sta in una pagina
        Assert.Equal(inizi.Count, Paginatore.PaginaDi(inizi, lunghezza - 1));
        Assert.Equal(1, Paginatore.PaginaDi(inizi, 0));
    }

    [Fact]
    public void Braille_InterruzioneDiPaginaIniziaUnaPaginaNuova()
    {
        var pagine = Impaginatore.Impagina(
            [new ParagrafoBraille("⠁"), new ParagrafoBraille("⠃", NuovaPagina: true), new ParagrafoBraille("⠉")],
            new FormatoPagina(20, 20));
        Assert.Equal(2, pagine.Count);
        Assert.Single(pagine[0]);
        Assert.Equal(2, pagine[1].Count);
    }

    [Fact]
    public void Variabili_SostituiteInStampa()
    {
        var pagina = new ImpostazioniPagina(PiePagina: "{titolo} - pagina {pagina} di {pagine}");
        Assert.Equal("Lettera - pagina 3 di 10", pagina.Componi(pagina.PiePagina, 3, 10, "Lettera", new DateTime(2026, 10, 9)));
        Assert.Equal("9 ottobre 2026", pagina.Componi("{data}", 1, 1, "", new DateTime(2026, 10, 9)));
        Assert.Equal("", (pagina with { EscludiPrimaPagina = true }).Componi(pagina.PiePagina, 1, 10, "Lettera", DateTime.Today));
    }

    [Fact]
    public void Variabili_DiviseInTestoECampi()
    {
        Assert.Equal(
            [("Pagina ", null), ("{pagina}", "PAGE"), (" di ", null), ("{pagine}", "NUMPAGES")],
            ImpostazioniPagina.Pezzi("Pagina {pagina} di {pagine}"));
        Assert.Equal("{pagine}", ImpostazioniPagina.SegnapostoDelCampo(" NUMPAGES \\* MERGEFORMAT "));
    }
}
