using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using DocumentFormat.OpenXml.Wordprocessing;
using Eleu.Documento;
using static Eleu.Tests.Supporto;

namespace Eleu.Tests;

public class DocxTests
{
    [Fact]
    public void ScritturaELettura_ConservanoIlDocumento()
    {
        var originale = Esempio();
        using var flusso = new MemoryStream();
        DocxFormato.Scrivi(originale, flusso);
        flusso.Position = 0;
        Assert.Equal(Descrivi(originale), Descrivi(DocxFormato.Leggi(flusso)));
    }

    [Fact]
    public void DocxScritto_RispettaLoSchemaOpenXml()
    {
        using var flusso = new MemoryStream();
        DocxFormato.Scrivi(Esempio(), flusso);
        flusso.Position = 0;
        using var documento = WordprocessingDocument.Open(flusso, false);
        var errori = new OpenXmlValidator().Validate(documento).Select(e => $"{e.Path?.XPath}: {e.Description}");
        Assert.Empty(errori);
    }

    [Fact]
    public void TitoliRiconosciutiDalNomeDelloStile_AncheConIdItaliano()
    {
        // Word in italiano salva lo stile "heading 1" con id "Titolo1".
        using var flusso = CreaDocx(corpo =>
        {
            corpo.Append(Paragrafo("Introduzione", stile: "Titolo1"));
            corpo.Append(Paragrafo("Testo"));
        }, stili: new Styles(new Style(new StyleName { Val = "heading 1" }) { StyleId = "Titolo1", Type = StyleValues.Paragraph }));

        var contenuto = DocxFormato.Leggi(flusso);
        Assert.Equal(1, contenuto.Paragrafi[0].LivelloTitolo);
        Assert.Equal(0, contenuto.Paragrafi[1].LivelloTitolo);
    }

    [Fact]
    public void ElencoNumerato_RestaUnElencoNumerato()
    {
        var numerazione = new Numbering(
            new AbstractNum(new Level(new NumberingFormat { Val = NumberFormatValues.Decimal }) { LevelIndex = 0 }) { AbstractNumberId = 5 },
            new NumberingInstance(new AbstractNumId { Val = 5 }) { NumberID = 7 });
        using var flusso = CreaDocx(corpo =>
        {
            corpo.Append(Paragrafo("uova", numero: 7));
            corpo.Append(Paragrafo("farina", numero: 7));
        }, numerazione: numerazione);

        var contenuto = DocxFormato.Leggi(flusso);
        Assert.Equal(["uova", "farina"], contenuto.Paragrafi.Select(p => p.Testo));
        Assert.All(contenuto.Paragrafi, p => Assert.True(p.Numerato));
        Assert.All(contenuto.Paragrafi, p => Assert.False(p.Elenco));
    }

    [Fact]
    public void AcapoManuale_DiventaNuovoParagrafo()
    {
        using var flusso = CreaDocx(corpo =>
            corpo.Append(new Paragraph(new Run(new Text("prima riga"), new Break(), new Text("seconda riga")))));
        Assert.Equal(["prima riga", "seconda riga"], DocxFormato.Leggi(flusso).Paragrafi.Select(p => p.Testo));
    }

    [Fact]
    public void CelleUniteSegnalateComeNonConservate()
    {
        using var flusso = CreaDocx(corpo =>
            corpo.Append(new Table(new TableRow(new TableCell(
                new TableCellProperties(new GridSpan { Val = 2 }), new Paragraph(new Run(new Text("cella"))))))));
        var contenuto = DocxFormato.Leggi(flusso);
        Assert.Equal(["celle unite o tabelle dentro le tabelle"], contenuto.ElementiNonConservati);
        Assert.Equal("cella", contenuto.Paragrafi[0].Testo);
        Assert.Equal(new PosizioneCella(0, 0, 0), contenuto.Paragrafi[0].Cella);
    }

    [Fact]
    public void DocumentoSemplice_NienteDaSegnalare()
    {
        using var flusso = new MemoryStream();
        DocxFormato.Scrivi(Esempio(), flusso);
        flusso.Position = 0;
        Assert.Empty(DocxFormato.Leggi(flusso).ElementiNonConservati);
    }

    private static Paragraph Paragrafo(string testo, string? stile = null, int? numero = null)
    {
        var proprieta = new ParagraphProperties();
        if (stile is not null)
            proprieta.Append(new ParagraphStyleId { Val = stile });
        if (numero is int n)
            proprieta.Append(new NumberingProperties(new NumberingLevelReference { Val = 0 }, new NumberingId { Val = n }));
        return new Paragraph(proprieta, new Run(new Text(testo)));
    }

    private static MemoryStream CreaDocx(Action<Body> riempi, Styles? stili = null, Numbering? numerazione = null)
    {
        var flusso = new MemoryStream();
        using (var documento = WordprocessingDocument.Create(flusso, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
        {
            var principale = documento.AddMainDocumentPart();
            var corpo = new Body();
            principale.Document = new Document(corpo);
            riempi(corpo);
            if (stili is not null)
                principale.AddNewPart<StyleDefinitionsPart>().Styles = stili;
            if (numerazione is not null)
                principale.AddNewPart<NumberingDefinitionsPart>().Numbering = numerazione;
        }
        flusso.Position = 0;
        return flusso;
    }
}
