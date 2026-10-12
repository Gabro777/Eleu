using System.IO.Compression;
using Eleu.Documento;

namespace Eleu.Tests;

public class HtmlEpubTests
{
    /// <summary>Un documento con un po' di tutto.</summary>
    private static ContenutoDocumento Esempio()
    {
        var c = new ContenutoDocumento { Metadati = new Metadati("Ricette", "Anna Rossi") };
        c.Segnalibri.Add("dolci");
        c.Note.Add("Meglio se fresche.");
        c.Immagini.Add(Immagini.DaDati(ImmaginiTests.Bitmap(System.Drawing.Imaging.ImageFormat.Png), "Una torta"));
        Paragrafo P(string testo, int titolo = 0, bool elenco = false, PosizioneCella? cella = null, bool grassetto = false)
        {
            var p = new Paragrafo { LivelloTitolo = titolo, Elenco = elenco, Cella = cella };
            p.Aggiungi(new Segmento(testo, Grassetto: grassetto));
            c.Paragrafi.Add(p);
            return p;
        }
        P("Primi", 1);
        P($"Servono uova{Note.Segnaposto(0)} e farina.").Aggiungi(new Segmento(" Molto", Grassetto: true));
        P("uova", elenco: true);
        P("farina", elenco: true);
        P($"{Segnalibri.Segnaposto(0)}Dolci", 1);
        P($"Ecco {Immagini.Segnaposto(0)} la torta.");
        P("Nome", cella: new PosizioneCella(0, 0, 0));
        P("Età", cella: new PosizioneCella(0, 0, 1));
        P("Anna", cella: new PosizioneCella(0, 1, 0));
        P("40", cella: new PosizioneCella(0, 1, 1));
        P("Fine.");
        return c;
    }

    private static string Descrivi(ContenutoDocumento c) => string.Join(" | ", c.Paragrafi
        .Where(p => p.Testo.Length > 0 || p.Cella is not null)
        .Select(p => $"{(p.LivelloTitolo > 0 ? $"T{p.LivelloTitolo} " : "")}{(p.Elenco ? "• " : "")}{(p.Cella is { } x ? $"[{x.Riga},{x.Colonna}] " : "")}{p.Testo}"));

    [Fact]
    public void Html_ScrittoERiletto()
    {
        var originale = Esempio();
        var html = HtmlFormato.Scrivi(originale);
        Assert.Contains("<html lang=\"it\">", html);
        Assert.Contains("alt=\"Una torta\"", html);
        Assert.Contains("<th scope=\"col\">Nome</th>", html);
        Assert.Contains("role=\"doc-noteref\"", html);

        var letto = HtmlFormato.Leggi(html);
        Assert.Equal(new Metadati("Ricette", "Anna Rossi"), letto.Metadati);
        Assert.Equal(Descrivi(Senza(originale)), Descrivi(Senza(letto)).Replace(" | Note | Meglio se fresche. ↩", ""));
        Assert.Single(letto.Immagini);
        Assert.Equal("Una torta", letto.Immagini[0].Descrizione);
        Assert.Contains(letto.Paragrafi.SelectMany(p => p.Segmenti), s => s.Grassetto && s.Testo.Contains("Molto"));
        Assert.Contains("dolci", letto.Segnalibri);
        Assert.Equal(["Meglio se fresche."], letto.Note); // il richiamo torna nota, la sezione delle note non è testo
        Assert.DoesNotContain(letto.Paragrafi, p => p.Testo.Contains("Meglio"));
    }

    /// <summary>Il testo senza i segnaposto (che in HTML diventano richiami, immagini e ancore).</summary>
    private static ContenutoDocumento Senza(ContenutoDocumento c)
    {
        var r = new ContenutoDocumento();
        foreach (var p in c.Paragrafi)
        {
            var n = p.CopiaProprieta();
            n.Cella = p.Cella is { } x ? new PosizioneCella(x.Tabella, x.Riga, x.Colonna) : null;
            n.Aggiungi(new Segmento(string.Concat(p.Testo.Where(ch => ch is < (char)0xE000 or > (char)0xF8FF)).Replace("  ", " ")));
            r.Paragrafi.Add(n);
        }
        return r;
    }

    [Fact]
    public void Html_DaUnaPaginaWeb()
    {
        var letto = HtmlFormato.Leggi("""
            <html><head><title>Pagina</title><script>x()</script></head><body>
            <h2>Titolo</h2><div>Primo <b>grassetto</b><br>a capo</div>
            <ol><li>uno</li><li>due</li></ol><p>&nbsp;Spazi   e&amp;segni&nbsp;</p></body></html>
            """);
        Assert.Equal("T2 Titolo | Primo grassetto | a capo | uno | due | Spazi e&segni", Descrivi(letto));
        Assert.True(letto.Paragrafi.First(p => p.Testo == "uno").Numerato);
    }

    [Fact]
    public void Epub_StrutturaGiustaERiletto()
    {
        using var flusso = new MemoryStream();
        EpubFormato.Scrivi(Esempio(), flusso);

        flusso.Position = 0;
        using (var zip = new ZipArchive(flusso, ZipArchiveMode.Read, leaveOpen: true))
        {
            Assert.Equal("mimetype", zip.Entries[0].FullName);
            Assert.Equal(zip.Entries[0].Length, zip.Entries[0].CompressedLength); // non compresso
            var nomi = zip.Entries.Select(e => e.FullName).ToList();
            Assert.Contains("OEBPS/capitolo001.xhtml", nomi);
            Assert.Contains("OEBPS/capitolo002.xhtml", nomi); // un capitolo per Titolo 1
            Assert.Contains("OEBPS/note.xhtml", nomi);
            Assert.Contains("OEBPS/nav.xhtml", nomi);
            Assert.Contains("OEBPS/immagini/immagine001.png", nomi);
            using var opf = new StreamReader(zip.GetEntry("OEBPS/content.opf")!.Open());
            var pacchetto = opf.ReadToEnd();
            Assert.Contains("schema:accessibilityFeature\">alternativeText", pacchetto);
            Assert.Contains("<dc:creator>Anna Rossi</dc:creator>", pacchetto);
            // Ogni capitolo è XML valido.
            foreach (var voce in zip.Entries.Where(e => e.FullName.EndsWith(".xhtml")))
            {
                using var s = new StreamReader(voce.Open());
                System.Xml.Linq.XDocument.Parse(s.ReadToEnd());
            }
        }

        flusso.Position = 0;
        var letto = EpubFormato.Leggi(flusso);
        Assert.Equal("Ricette", letto.Metadati.Titolo);
        var testo = Descrivi(Senza(letto));
        Assert.StartsWith("T1 Primi | Servono uova", testo);
        Assert.Contains("T1 Dolci", testo);
        Assert.Contains("[1,1] 40", testo);
        Assert.Contains("Meglio se fresche.", testo); // il capitolo delle note
        Assert.Single(letto.Immagini);
        Assert.True(letto.Paragrafi.First(p => p.Testo.Contains("Dolci")).SaltoPagina);
    }
}
