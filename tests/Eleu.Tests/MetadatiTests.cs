using Eleu.Documento;
using static Eleu.Tests.Supporto;

namespace Eleu.Tests;

public class MetadatiTests
{
    private static readonly Metadati Esempio = new("Ricette della nonna", "Anna Rossi", "Cucina {tradizionale}", "dolci, torte", new DateTime(2026, 3, 1, 10, 30, 0));

    [Fact]
    public void Rtf_ScrittiERiletti_AncheDopoIlCampoDiTesto()
    {
        string? rtf = null;
        EseguiSta(() =>
        {
            using var campo = new RichTextBox();
            campo.CreateControl();
            campo.Text = "testo";
            rtf = Esempio.InRtf(campo.Rtf!);
        });
        Assert.Equal(Esempio, Metadati.DaRtf(rtf!));
        // Rimetterli non li duplica.
        Assert.Equal(Esempio with { Titolo = "Altro" }, Metadati.DaRtf((Esempio with { Titolo = "Altro" }).InRtf(rtf!)));
    }

    [Fact]
    public void Docx_ScrittiERiletti()
    {
        var contenuto = new ContenutoDocumento { Metadati = Esempio };
        var p = new Paragrafo();
        p.Aggiungi(new Segmento("ciao"));
        contenuto.Paragrafi.Add(p);
        using var flusso = new MemoryStream();
        DocxFormato.Scrivi(contenuto, flusso);
        flusso.Position = 0;
        var letti = DocxFormato.Leggi(flusso).Metadati;
        Assert.Equal(Esempio with { Creato = null }, letti with { Creato = null });
        // Il fuso orario può spostare l'ora: basta che il giorno sia quello.
        Assert.Equal(Esempio.Creato!.Value.Date, letti.Creato!.Value.Date);
    }
}
