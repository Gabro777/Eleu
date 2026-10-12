using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Eleu.Documento;

namespace Eleu.Tests;

public class OdtTests
{
    private static Paragrafo P(string testo, Action<Paragrafo>? imposta = null)
    {
        var p = new Paragrafo();
        p.Aggiungi(new Segmento(testo));
        imposta?.Invoke(p);
        return p;
    }

    private static ContenutoDocumento Ricco()
    {
        var c = new ContenutoDocumento
        {
            Metadati = new Metadati("Ricette", "Anna Rossi", "Cucina", "uova, farina", new DateTime(2026, 1, 2, 3, 4, 0)),
            Pagina = new ImpostazioniPagina(Orizzontale: true, MargineSinistro: 50, Intestazione: "Pagina {pagina} di {pagine}", PiePagina: "{titolo}"),
        };
        c.Paragrafi.Add(P("Torta", p => p.LivelloTitolo = 1));
        var formattato = new Paragrafo { Allineamento = Allineamento.Giustificato, RientroPrimaRiga = 20, SpazioDopo = 6, Interlinea = 1.5f };
        formattato.Aggiungi(new Segmento("Servono  "));
        formattato.Aggiungi(new Segmento("uova", Grassetto: true));
        formattato.Aggiungi(new Segmento(" e "));
        formattato.Aggiungi(new Segmento("farina", Corsivo: true, Sottolineato: true, Dimensione: 14, Carattere: "Arial", Colore: 0xC00000));
        formattato.Aggiungi(new Segmento("2", Posizione: PosizioneTesto.Apice));
        c.Paragrafi.Add(formattato);
        c.Paragrafi.Add(P("uova", p => p.Elenco = true));
        c.Paragrafi.Add(P("farina", p => p.Elenco = true));
        c.Paragrafi.Add(P("sbatti", p => p.Numerato = true));
        c.Paragrafi.Add(P("cuoci", p => p.Numerato = true));

        // Una tabella 2 x 2 con la riga di intestazione e la prima colonna larga 3 cm.
        string[,] celle = { { "Cosa", "Quanto" }, { "Uova", "3" } };
        for (int r = 0; r < 2; r++)
            for (int col = 0; col < 2; col++)
                c.Paragrafi.Add(P(celle[r, col], p => p.Cella = new PosizioneCella(0, r, col, col == 0 ? 85.04f : 0, Intestazione: r == 0)));

        c.Note.Add("Meglio se fresche.\nDi gallina.");
        c.Segnalibri.Add("forno");
        c.Commenti.Add(new Commento("Quanto zucchero?", "Luca", new DateTime(2026, 10, 10, 9, 30, 0)));
        c.Commenti.Add(new Commento("Bella!", "Anna Rossi"));
        c.Immagini.Add(Immagini.DaDati(ImmaginiTests.Bitmap(System.Drawing.Imaging.ImageFormat.Png), "Una torta al cioccolato"));
        c.Paragrafi.Add(P($"Inforna{Note.Segnaposto(0)} {Segnalibri.Segnaposto(0)}a {Commenti.Inizio(0)}180 gradi{Commenti.Fine(0)}.{Commenti.Inizio(1)}{Commenti.Fine(1)}",
            p => p.SaltoPagina = true));
        c.Paragrafi.Add(P($"Ecco: {Immagini.Segnaposto(0)}"));
        return c;
    }

    private static byte[] Odt(ContenutoDocumento c)
    {
        using var flusso = new MemoryStream();
        OdtFormato.Scrivi(c, flusso);
        return flusso.ToArray();
    }

    private static ContenutoDocumento Leggi(byte[] dati)
    {
        using var flusso = new MemoryStream(dati);
        return OdtFormato.Leggi(flusso);
    }

    [Fact]
    public void Zip_MimetypePrimoNonCompressoEXmlBenFormato()
    {
        var dati = Odt(Ricco());
        Assert.Equal("PK", Encoding.ASCII.GetString(dati, 0, 2));
        // Nell'intestazione locale del primo file: metodo 0 (non compresso), nome "mimetype", poi il tipo in chiaro.
        Assert.Equal(0, BitConverter.ToUInt16(dati, 8));
        Assert.Equal("mimetype", Encoding.ASCII.GetString(dati, 30, 8));
        Assert.Equal(OdtFormato.TipoMime, Encoding.ASCII.GetString(dati, 30 + 8 + BitConverter.ToUInt16(dati, 28), OdtFormato.TipoMime.Length));

        using var zip = new ZipArchive(new MemoryStream(dati));
        Assert.Equal("mimetype", zip.Entries[0].FullName);
        foreach (var nome in new[] { "content.xml", "styles.xml", "meta.xml", "META-INF/manifest.xml" })
        {
            using var s = zip.GetEntry(nome)!.Open();
            Assert.NotNull(XDocument.Load(s).Root);
        }
        // Ogni file del pacchetto è nel manifest.
        using var m = zip.GetEntry("META-INF/manifest.xml")!.Open();
        var elencati = XDocument.Load(m).Descendants().Select(e => (string?)e.Attributes().FirstOrDefault(a => a.Name.LocalName == "full-path")).ToHashSet();
        Assert.All(zip.Entries.Where(e => e.FullName is not ("mimetype" or "META-INF/manifest.xml")), e => Assert.Contains(e.FullName, elencati));
    }

    [Fact]
    public void AndataERitorno_ConservaTesto()
    {
        var originale = Ricco();
        var letto = Leggi(Odt(originale));
        Assert.Equal(originale.Paragrafi.Select(p => p.Testo), letto.Paragrafi.Select(p => p.Testo));
        Assert.Empty(letto.ElementiNonConservati);
    }

    [Fact]
    public void AndataERitorno_ConservaFormattazione()
    {
        var letto = Leggi(Odt(Ricco()));
        Assert.Equal(1, letto.Paragrafi[0].LivelloTitolo);
        var p = letto.Paragrafi[1];
        Assert.Equal(Allineamento.Giustificato, p.Allineamento);
        Assert.Equal(20, p.RientroPrimaRiga, 1);
        Assert.Equal(6, p.SpazioDopo, 1);
        Assert.Equal(1.5f, p.Interlinea, 2);
        Assert.Contains(p.Segmenti, s => s.Testo == "uova" && s.Grassetto);
        var farina = p.Segmenti.Single(s => s.Testo == "farina");
        Assert.True(farina.Corsivo && farina.Sottolineato);
        Assert.Equal(14f, farina.Dimensione);
        Assert.Equal("Arial", farina.Carattere);
        Assert.Equal(0xC00000, farina.Colore);
        Assert.Equal(PosizioneTesto.Apice, p.Segmenti.Single(s => s.Testo == "2").Posizione);
        Assert.Equal("Servono  uova e farina2", p.Testo); // due spazi restano due

        Assert.True(letto.Paragrafi[2].Elenco && letto.Paragrafi[3].Elenco);
        Assert.True(letto.Paragrafi[4].Numerato && letto.Paragrafi[5].Numerato);
        Assert.True(letto.Paragrafi.Single(x => x.Testo.StartsWith("Inforna")).SaltoPagina);
    }

    [Fact]
    public void AndataERitorno_ConservaTabellaNoteSegnalibriCommentiImmagini()
    {
        var letto = Leggi(Odt(Ricco()));
        var celle = letto.Paragrafi.Where(p => p.Cella is not null).ToList();
        Assert.Equal(["Cosa", "Quanto", "Uova", "3"], celle.Select(p => p.Testo));
        Assert.True(celle[0].Cella!.Intestazione);
        Assert.False(celle[2].Cella!.Intestazione);
        Assert.Equal(85.04f, celle[0].Cella!.Larghezza, 1);
        Assert.Equal((1, 1), (celle[3].Cella!.Riga, celle[3].Cella!.Colonna));

        Assert.Equal(["Meglio se fresche.\nDi gallina."], letto.Note);
        Assert.Equal(["forno"], letto.Segnalibri);
        Assert.Equal("Quanto zucchero?", letto.Commenti[0].Testo);
        Assert.Equal("Luca", letto.Commenti[0].Autore);
        Assert.Equal(new DateTime(2026, 10, 10, 9, 30, 0), letto.Commenti[0].Data);
        Assert.Equal("Bella!", letto.Commenti[1].Testo);
        Assert.Single(letto.Immagini);
        Assert.Equal("Una torta al cioccolato", letto.Immagini[0].Descrizione);
    }

    [Fact]
    public void AndataERitorno_ConservaPaginaEProprieta()
    {
        var originale = Ricco();
        var letto = Leggi(Odt(originale));
        Assert.True(letto.Pagina.Orizzontale);
        Assert.Equal(originale.Pagina.Larghezza, letto.Pagina.Larghezza, 0);
        Assert.Equal(50, letto.Pagina.MargineSinistro, 1);
        Assert.Equal("Pagina {pagina} di {pagine}", letto.Pagina.Intestazione);
        Assert.Equal("{titolo}", letto.Pagina.PiePagina);
        Assert.Equal(originale.Pagina.MargineSopra, letto.Pagina.MargineSopra, 0);
        Assert.Equal(originale.Metadati with { }, letto.Metadati with { });
    }

    /// <summary>Un documento come lo scrive LibreOffice: stili con nome, span, spazi, elenchi con il loro stile, titoli numerati.</summary>
    [Fact]
    public void Leggi_DocumentoComeLoScriveLibreOffice()
    {
        const string contenuto = """
            <?xml version="1.0" encoding="UTF-8"?>
            <office:document-content xmlns:office="urn:oasis:names:tc:opendocument:xmlns:office:1.0" xmlns:style="urn:oasis:names:tc:opendocument:xmlns:style:1.0" xmlns:text="urn:oasis:names:tc:opendocument:xmlns:text:1.0" xmlns:table="urn:oasis:names:tc:opendocument:xmlns:table:1.0" xmlns:fo="urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0" xmlns:dc="http://purl.org/dc/elements/1.1/" office:version="1.3">
            <office:automatic-styles>
            <style:style style:name="P1" style:family="paragraph" style:parent-style-name="Standard"><style:paragraph-properties fo:text-align="center"/></style:style>
            <style:style style:name="T1" style:family="text"><style:text-properties fo:font-weight="bold"/></style:style>
            <text:list-style style:name="L1"><text:list-level-style-number text:level="1" style:num-suffix="." style:num-format="1"/></text:list-style>
            </office:automatic-styles>
            <office:body><office:text>
            <text:h text:style-name="Heading_20_2" text:outline-level="2"><text:number>1.1</text:number>Ingredienti</text:h>
            <text:p text:style-name="P1">Uno <text:span text:style-name="T1">due</text:span>   tre<text:s text:c="2"/>quattro<text:tab/>cinque<text:line-break/>sei</text:p>
            <text:soft-page-break/>
            <text:list text:style-name="L1"><text:list-item><text:p>primo</text:p></text:list-item><text:list-item><text:p>secondo</text:p>
            <text:list><text:list-item><text:p>dentro</text:p></text:list-item></text:list></text:list-item></text:list>
            <text:p>Nota<text:note text:id="ftn1" text:note-class="footnote"><text:note-citation>1</text:note-citation><text:note-body><text:p>Testo della nota</text:p></text:note-body></text:note> e <office:annotation><dc:creator>Anna</dc:creator><text:p>Sì</text:p></office:annotation>commento.</text:p>
            <table:table table:name="T"><table:table-column table:number-columns-repeated="2"/><table:table-row><table:table-cell table:number-columns-spanned="2"><text:p>unita</text:p></table:table-cell><table:covered-table-cell/></table:table-row></table:table>
            </office:text></office:body></office:document-content>
            """;
        using var flusso = new MemoryStream();
        using (var zip = new ZipArchive(flusso, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var s = zip.CreateEntry("content.xml").Open();
            s.Write(Encoding.UTF8.GetBytes(contenuto.Trim()));
        }
        flusso.Position = 0;
        var letto = OdtFormato.Leggi(flusso);

        Assert.Equal("Ingredienti", letto.Paragrafi[0].Testo);
        Assert.Equal(2, letto.Paragrafi[0].LivelloTitolo);
        Assert.Equal("Uno due tre  quattro\tcinque", letto.Paragrafi[1].Testo);
        Assert.Equal(Allineamento.Centro, letto.Paragrafi[1].Allineamento);
        Assert.True(letto.Paragrafi[1].Segmenti.Single(s => s.Testo == "due").Grassetto);
        Assert.Equal("sei", letto.Paragrafi[2].Testo);
        Assert.True(letto.Paragrafi[3].Numerato && letto.Paragrafi[4].Numerato);
        Assert.Equal("dentro", letto.Paragrafi[5].Testo);
        Assert.True(letto.Paragrafi[5].Numerato);
        Assert.Equal(["Testo della nota"], letto.Note);
        Assert.Equal([new Commento("Sì", "Anna")], letto.Commenti);
        Assert.Equal($"Nota{Note.Segnaposto(0)} e {Commenti.Inizio(0)}{Commenti.Fine(0)}commento.", letto.Paragrafi[6].Testo);
        Assert.Contains(letto.ElementiNonConservati, e => e.Contains("celle unite"));
    }

    [Fact]
    public void Leggi_FileNonOdt_Errore()
    {
        using var flusso = new MemoryStream();
        using (var zip = new ZipArchive(flusso, ZipArchiveMode.Create, leaveOpen: true))
            zip.CreateEntry("altro.txt");
        flusso.Position = 0;
        Assert.Throws<InvalidDataException>(() => OdtFormato.Leggi(flusso));
    }

    [Theory]
    [InlineData("2cm", 56.69)]
    [InlineData("12pt", 12)]
    [InlineData("0.5in", 36)]
    [InlineData("10mm", 28.346)]
    public void Punti_DalleMisure(string misura, double atteso) => Assert.Equal(atteso, OdtFormato.Punti(misura)!.Value, 1);
}
