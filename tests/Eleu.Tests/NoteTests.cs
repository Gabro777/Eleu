using Eleu.Documento;
using static Eleu.Tests.Supporto;

namespace Eleu.Tests;

public class NoteTests
{
    // Negli RTF dei test "|" sta per la barra rovescia.
    private static string Rtf(string testo) => testo.Replace('|', '\\');

    [Fact]
    public void InSegnaposto_NotaDiventaUnCarattereEIlSuoTesto()
    {
        var testi = new List<string>();
        var rtf = Segnalibri.InSegnaposto(Rtf(
            "{|rtf1 Uova{|super|chftn}{|footnote|pard|plain{|super|chftn} Meglio se {|b fresche}.|par Di gallina.} e farina.}"), []);
        rtf = Note.InSegnaposto(rtf, testi);

        Assert.Equal(["Meglio se fresche.\nDi gallina."], testi);
        Assert.DoesNotContain("footnote", rtf);
        Assert.DoesNotContain("chftn", rtf);
    }

    [Fact]
    public void AttraversoIlCampoDiTesto_NotaTornaComeNotaRtf()
    {
        string? letto = null, riscritto = null;
        var testi = new List<string>();
        EseguiSta(() =>
        {
            using var campo = new RichTextBox();
            campo.CreateControl();
            campo.Rtf = Note.InSegnaposto(Rtf("{|rtf1 Uova{|super|chftn}{|footnote {|super|chftn} Fresche.} e farina.}"), testi);
            letto = campo.Text;
            riscritto = Note.DaSegnaposto(campo.Rtf!, testi);
        });

        Assert.Equal("Uova" + Note.Segnaposto(0) + " e farina.", letto);
        Assert.Contains(Rtf("{|footnote"), riscritto);
        // Riletto, il testo della nota è lo stesso.
        var ancora = new List<string>();
        Note.InSegnaposto(riscritto!, ancora);
        Assert.Equal(["Fresche."], ancora);
    }

    [Fact]
    public void Docx_NoteConservate()
    {
        var contenuto = new ContenutoDocumento();
        contenuto.Note.AddRange(["Prima nota.", "Seconda,\nsu due righe."]);
        var p = new Paragrafo();
        p.Aggiungi(new Segmento($"Uova{Note.Segnaposto(0)} e farina{Note.Segnaposto(1)}."));
        contenuto.Paragrafi.Add(p);

        using var flusso = new MemoryStream();
        DocxFormato.Scrivi(contenuto, flusso);
        flusso.Position = 0;
        var errori = new DocumentFormat.OpenXml.Validation.OpenXmlValidator()
            .Validate(DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Open(flusso, false));
        Assert.Empty(errori);

        flusso.Position = 0;
        var riletto = DocxFormato.Leggi(flusso);
        Assert.Equal(["Prima nota.", "Seconda,\nsu due righe."], riletto.Note);
        Assert.Equal($"Uova{Note.Segnaposto(0)} e farina{Note.Segnaposto(1)}.", riletto.Paragrafi[0].Testo);
        Assert.Empty(riletto.ElementiNonConservati);
    }
}
