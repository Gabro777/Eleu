using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using Eleu.Documento;
using static Eleu.Tests.Supporto;

namespace Eleu.Tests;

public class CommentiTests
{
    private static readonly DateTime Quando = new(2026, 10, 10, 18, 30, 0);

    /// <summary>"Le uova [fresche] sono buone" con un commento su "fresche" e uno su un punto, in fondo.</summary>
    private static ContenutoDocumento ConCommenti()
    {
        var contenuto = new ContenutoDocumento();
        contenuto.Commenti.Add(new Commento("Meglio di giornata.", "Anna Rossi", Quando));
        contenuto.Commenti.Add(new Commento("Manca il punto?\nControlla.", "Luca"));
        var p = new Paragrafo();
        p.Aggiungi(new Segmento($"Le uova {Commenti.Inizio(0)}fresche{Commenti.Fine(0)} sono buone{Commenti.Inizio(1)}{Commenti.Fine(1)}"));
        contenuto.Paragrafi.Add(p);
        return contenuto;
    }

    [Fact]
    public void Segnaposto_InizioEFineDistinti()
    {
        Assert.Equal(0, Commenti.IndiceInizio(Commenti.Inizio(0)));
        Assert.Equal(-1, Commenti.IndiceFine(Commenti.Inizio(0)));
        Assert.Equal(Commenti.Massimo - 1, Commenti.IndiceFine(Commenti.Fine(Commenti.Massimo - 1)));
        // Nessuna sovrapposizione con le immagini né oltre l'area a uso privato.
        Assert.Equal(-1, Immagini.Indice(Commenti.Inizio(0)));
        Assert.True(Commenti.Fine(Commenti.Massimo - 1) <= '');
        Assert.Equal("AR", new Commento("x", "Anna Rossi").Iniziali);
    }

    [Fact]
    public void Dttm_AndataERitorno() => Assert.Equal(Quando, Commenti.DaDttm(Commenti.Dttm(Quando)));

    [Fact]
    public void Docx_CommentiConservatiEValidi()
    {
        using var flusso = new MemoryStream();
        DocxFormato.Scrivi(ConCommenti(), flusso);
        flusso.Position = 0;
        using (var documento = WordprocessingDocument.Open(flusso, false))
        {
            Assert.Empty(new OpenXmlValidator().Validate(documento).Select(e => $"{e.Path?.XPath}: {e.Description}"));
            Assert.Equal(2, documento.MainDocumentPart!.WordprocessingCommentsPart!.Comments!.ChildElements.Count);
        }

        flusso.Position = 0;
        var riletto = DocxFormato.Leggi(flusso);
        Assert.Equal(ConCommenti().Paragrafi[0].Testo, riletto.Paragrafi[0].Testo);
        Assert.Equal("Meglio di giornata.", riletto.Commenti[0].Testo);
        Assert.Equal("Anna Rossi", riletto.Commenti[0].Autore);
        Assert.Equal(Quando, riletto.Commenti[0].Data);
        Assert.Equal("Manca il punto?\nControlla.", riletto.Commenti[1].Testo);
        Assert.Empty(riletto.ElementiNonConservati);
        // Per il Braille e il testo semplice i commenti non ci sono.
        Assert.Equal("Le uova fresche sono buone", Leggibile.Da(riletto).Paragrafi[0].Testo);
    }

    [Fact]
    public void Docx_ConRevisioni_AvvisaCheSonoAccettate()
    {
        using var flusso = new MemoryStream();
        DocxFormato.Scrivi(ConCommenti(), flusso);
        using (var documento = WordprocessingDocument.Open(flusso, true))
        {
            var corpo = documento.MainDocumentPart!.Document!.Body!;
            corpo.GetFirstChild<DocumentFormat.OpenXml.Wordprocessing.Paragraph>()!.Append(
                new DocumentFormat.OpenXml.Wordprocessing.InsertedRun(new DocumentFormat.OpenXml.Wordprocessing.Run(
                    new DocumentFormat.OpenXml.Wordprocessing.Text(" aggiunto"))) { Id = "9", Author = "Luca" },
                new DocumentFormat.OpenXml.Wordprocessing.DeletedRun(new DocumentFormat.OpenXml.Wordprocessing.Run(
                    new DocumentFormat.OpenXml.Wordprocessing.DeletedText(" tolto"))) { Id = "10", Author = "Luca" });
        }
        flusso.Position = 0;
        var letto = DocxFormato.Leggi(flusso);
        Assert.EndsWith(" aggiunto", letto.Paragrafi[0].Testo);
        Assert.DoesNotContain("tolto", letto.Paragrafi[0].Testo);
        Assert.Contains(letto.ElementiNonConservati, e => e.Contains("revisioni"));
    }

    [Fact]
    public void Rtf_AndataERitornoAttraversoIlCampoDiTesto()
    {
        var originali = ConCommenti().Commenti;
        var testo = ConCommenti().Paragrafi[0].Testo;
        string? letto = null;
        var riletti = new List<Commento>();
        EseguiSta(() =>
        {
            using var campo = new RichTextBox();
            campo.CreateControl();
            campo.Text = testo;
            var rtf = Commenti.DaSegnaposto(campo.Rtf!, originali);
            Assert.Contains(@"\annotation", rtf);
            campo.Rtf = Commenti.InSegnaposto(rtf, riletti);
            letto = campo.Text;
        });
        Assert.Equal(testo, letto);
        Assert.Equal(2, riletti.Count);
        Assert.Equal(originali[0], riletti[0]);
        Assert.Equal(originali[1] with { Data = null }, riletti[1] with { Data = null });
    }

    [Fact]
    public void Rtf_DiWord_SenzaIntervallo_DiventaCommentoSuUnPunto()
    {
        // Come lo scrive Word: iniziali, autore, segno di richiamo e gruppo dell'annotazione.
        var rtf = @"{\rtf1\ansi Ciao{\*\atnid AR}{\*\atnauthor Anna}\chatn {\*\annotation{\*\atnref 7}\pard\plain {\chatn }Bella frase.} mondo.}";
        var commenti = new List<Commento>();
        var convertito = Commenti.InSegnaposto(rtf, commenti);
        Assert.Equal([new Commento("Bella frase.", "Anna")], commenti);
        Assert.Equal("Ciao" + Commenti.Inizio(0) + Commenti.Fine(0) + " mondo.", RtfLettore.Leggi(convertito).Paragrafi[0].Testo);
    }
}
