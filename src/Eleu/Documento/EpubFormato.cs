using System.IO.Compression;
using System.Net;
using System.Text;
using System.Xml.Linq;

namespace Eleu.Documento;

/// <summary>
/// EPUB 3, il formato dei libri digitali. In scrittura: un capitolo per ogni titolo del livello più alto, l'indice
/// navigabile (nav), le immagini con il testo alternativo, le note in un file a parte e i metadati di accessibilità
/// (schema.org) che i lettori e le biblioteche si aspettano. In lettura: i capitoli nell'ordine del libro, ognuno
/// su una pagina nuova.
/// </summary>
public static class EpubFormato
{
    private const string Opf = "http://www.idpf.org/2007/opf";
    private const string Dc = "http://purl.org/dc/elements/1.1/";

    public static void Scrivi(ContenutoDocumento contenuto, Stream flusso)
    {
        using var zip = new ZipArchive(flusso, ZipArchiveMode.Create, leaveOpen: true);
        // Il primo file deve essere "mimetype", non compresso.
        Scrivi(zip, "mimetype", "application/epub+zip", CompressionLevel.NoCompression);
        Scrivi(zip, "META-INF/container.xml",
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<container version=\"1.0\" xmlns=\"urn:oasis:names:tc:opendocument:xmlns:container\">" +
            "<rootfiles><rootfile full-path=\"OEBPS/content.opf\" media-type=\"application/oebps-package+xml\"/></rootfiles></container>");

        var capitoli = Capitoli(contenuto);
        var manifest = new List<(string Id, string Href, string Tipo, string? Proprieta)>();
        var titoli = new List<(int Livello, string Testo, string Href)>();
        string fileNote = contenuto.Note.Count > 0 ? "note.xhtml" : "";

        for (int c = 0; c < capitoli.Count; c++)
        {
            var (primo, paragrafi) = capitoli[c];
            var nome = $"capitolo{c + 1:D3}.xhtml";
            var parte = new ContenutoDocumento { Metadati = contenuto.Metadati };
            parte.Paragrafi.AddRange(paragrafi);
            parte.Segnalibri.AddRange(contenuto.Segnalibri);
            parte.Note.AddRange(contenuto.Note);
            parte.Immagini.AddRange(contenuto.Immagini);
            var html = HtmlFormato.Scrivi(parte, xhtml: true, fileImmagine: NomeImmagine, fileNote: fileNote.Length > 0 ? fileNote : null, primoParagrafo: primo);
            Scrivi(zip, "OEBPS/" + nome, html);
            manifest.Add(($"c{c + 1}", nome, "application/xhtml+xml", null));
            for (int i = 0; i < paragrafi.Count; i++)
                if (paragrafi[i].LivelloTitolo is > 0 and <= 3 && paragrafi[i].Testo.Trim().Length > 0)
                    titoli.Add((paragrafi[i].LivelloTitolo, paragrafi[i].Testo.Trim(), $"{nome}#t{primo + i}"));
        }

        if (fileNote.Length > 0)
        {
            var note = new StringBuilder();
            note.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<!DOCTYPE html>");
            note.AppendLine("<html xmlns=\"http://www.w3.org/1999/xhtml\" xmlns:epub=\"http://www.idpf.org/2007/ops\" lang=\"it\" xml:lang=\"it\">");
            note.AppendLine("<head><meta charset=\"utf-8\" /><title>Note</title></head><body>");
            note.AppendLine("<section epub:type=\"endnotes\" role=\"doc-endnotes\"><h1>Note</h1><ol>");
            for (int n = 0; n < contenuto.Note.Count; n++)
                note.Append($"<li id=\"nota-{n + 1}\" epub:type=\"endnote\">").Append(WebUtility.HtmlEncode(contenuto.Note[n]).Replace("\n", "<br />")).AppendLine("</li>");
            note.AppendLine("</ol></section></body></html>");
            Scrivi(zip, "OEBPS/" + fileNote, note.ToString());
            manifest.Add(("note", fileNote, "application/xhtml+xml", null));
            titoli.Add((1, "Note", fileNote));
        }

        for (int i = 0; i < contenuto.Immagini.Count; i++)
        {
            var voce = zip.CreateEntry("OEBPS/" + NomeImmagine(i), CompressionLevel.NoCompression);
            using (var s = voce.Open())
                s.Write(contenuto.Immagini[i].Dati);
            manifest.Add(($"img{i + 1}", NomeImmagine(i), contenuto.Immagini[i].Formato == "jpeg" ? "image/jpeg" : "image/png", null));
        }

        Scrivi(zip, "OEBPS/nav.xhtml", Indice(titoli, capitoli.Count > 0 ? manifest[0].Href : "nav.xhtml"));
        manifest.Insert(0, ("nav", "nav.xhtml", "application/xhtml+xml", "nav"));
        Scrivi(zip, "OEBPS/content.opf", Pacchetto(contenuto, manifest, capitoli.Count, fileNote.Length > 0));

        string NomeImmagine(int i) => $"immagini/immagine{i + 1:D3}.{(contenuto.Immagini[i].Formato == "jpeg" ? "jpg" : "png")}";
    }

    /// <summary>I capitoli: si va a capo a ogni titolo del livello più alto presente; senza titoli, un capitolo solo.</summary>
    private static List<(int Primo, List<Paragrafo> Paragrafi)> Capitoli(ContenutoDocumento contenuto)
    {
        var livelli = contenuto.Paragrafi.Where(p => p.LivelloTitolo > 0 && p.Cella is null).Select(p => p.LivelloTitolo).ToList();
        int livello = livelli.Count == 0 ? 0 : livelli.Min();
        var capitoli = new List<(int, List<Paragrafo>)>();
        List<Paragrafo>? corrente = null;
        for (int i = 0; i < contenuto.Paragrafi.Count; i++)
        {
            var p = contenuto.Paragrafi[i];
            if (corrente is null || (livello > 0 && p.LivelloTitolo == livello && p.Cella is null && corrente.Count > 0))
            {
                corrente = [];
                capitoli.Add((i, corrente));
            }
            corrente.Add(p);
        }
        return capitoli;
    }

    private static string Indice(List<(int Livello, string Testo, string Href)> titoli, string primo)
    {
        var h = new StringBuilder();
        h.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<!DOCTYPE html>");
        h.AppendLine("<html xmlns=\"http://www.w3.org/1999/xhtml\" xmlns:epub=\"http://www.idpf.org/2007/ops\" lang=\"it\" xml:lang=\"it\">");
        h.AppendLine("<head><meta charset=\"utf-8\" /><title>Indice</title></head><body>");
        h.AppendLine("<nav epub:type=\"toc\" role=\"doc-toc\" id=\"toc\"><h1>Indice</h1>");
        if (titoli.Count == 0)
            titoli = [(1, "Inizio", primo)];
        // Elenchi annidati secondo il livello dei titoli.
        int aperti = 0;
        int livelloPrecedente = 0;
        foreach (var (livello, testo, href) in titoli)
        {
            int l = Math.Clamp(livello, 1, aperti + 1);
            if (l > livelloPrecedente)
            {
                h.Append("<ol>");
                aperti++;
            }
            else
            {
                h.Append("</li>");
                for (; livelloPrecedente > l; livelloPrecedente--)
                {
                    h.Append("</ol></li>");
                    aperti--;
                }
            }
            h.Append($"<li><a href=\"{WebUtility.HtmlEncode(href)}\">{WebUtility.HtmlEncode(testo)}</a>");
            livelloPrecedente = l;
        }
        h.Append("</li>");
        for (; aperti > 1; aperti--)
            h.Append("</ol></li>");
        h.AppendLine("</ol>");
        h.AppendLine("</nav></body></html>");
        return h.ToString();
    }

    private static string Pacchetto(ContenutoDocumento contenuto, List<(string Id, string Href, string Tipo, string? Proprieta)> manifest, int capitoli, bool note)
    {
        XNamespace opf = Opf, dc = Dc;
        var m = contenuto.Metadati;
        var titolo = m.Titolo.Length > 0 ? m.Titolo : contenuto.Paragrafi.FirstOrDefault(p => p.LivelloTitolo > 0)?.Testo.Trim() ?? "Documento";
        bool immagini = contenuto.Immagini.Count > 0;
        var metadati = new XElement(opf + "metadata",
            new XAttribute(XNamespace.Xmlns + "dc", Dc),
            new XElement(dc + "identifier", new XAttribute("id", "id-libro"), $"urn:uuid:{Guid.NewGuid()}"),
            new XElement(dc + "title", titolo),
            new XElement(dc + "language", "it"),
            new XElement(opf + "meta", new XAttribute("property", "dcterms:modified"), DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")),
            // Accessibilità: come si legge il libro e cosa offre.
            new XElement(opf + "meta", new XAttribute("property", "schema:accessMode"), "textual"),
            immagini ? new XElement(opf + "meta", new XAttribute("property", "schema:accessMode"), "visual") : null,
            new XElement(opf + "meta", new XAttribute("property", "schema:accessModeSufficient"), "textual"),
            new XElement(opf + "meta", new XAttribute("property", "schema:accessibilityFeature"), "structuralNavigation"),
            new XElement(opf + "meta", new XAttribute("property", "schema:accessibilityFeature"), "tableOfContents"),
            immagini ? new XElement(opf + "meta", new XAttribute("property", "schema:accessibilityFeature"), "alternativeText") : null,
            new XElement(opf + "meta", new XAttribute("property", "schema:accessibilityHazard"), "none"),
            new XElement(opf + "meta", new XAttribute("property", "schema:accessibilitySummary"),
                "Libro con titoli, indice navigabile" + (immagini ? " e descrizioni delle immagini" : "") + ", creato con Eleu."));
        if (m.Autore.Length > 0)
            metadati.Add(new XElement(dc + "creator", m.Autore));
        if (m.Oggetto.Length > 0)
            metadati.Add(new XElement(dc + "description", m.Oggetto));
        if (m.ParoleChiave.Length > 0)
            metadati.Add(new XElement(dc + "subject", m.ParoleChiave));
        if (m.Creato is DateTime creato)
            metadati.Add(new XElement(dc + "date", creato.ToString("yyyy-MM-dd")));

        var pacchetto = new XElement(opf + "package",
            new XAttribute("version", "3.0"), new XAttribute("unique-identifier", "id-libro"), new XAttribute(XNamespace.Xml + "lang", "it"),
            metadati,
            new XElement(opf + "manifest", manifest.Select(v => new XElement(opf + "item",
                new XAttribute("id", v.Id), new XAttribute("href", v.Href), new XAttribute("media-type", v.Tipo),
                v.Proprieta is null ? null : new XAttribute("properties", v.Proprieta)))),
            new XElement(opf + "spine",
                Enumerable.Range(1, capitoli).Select(c => new XElement(opf + "itemref", new XAttribute("idref", $"c{c}"))),
                note ? new XElement(opf + "itemref", new XAttribute("idref", "note")) : null));
        return new XDocument(new XDeclaration("1.0", "utf-8", null), pacchetto).Declaration + "\n" + pacchetto;
    }

    private static void Scrivi(ZipArchive zip, string nome, string testo, CompressionLevel livello = CompressionLevel.Optimal)
    {
        var voce = zip.CreateEntry(nome, livello);
        using var s = voce.Open();
        var dati = new UTF8Encoding(false).GetBytes(testo);
        s.Write(dati);
    }

    // ---------- Lettura ----------

    public static ContenutoDocumento Leggi(Stream flusso)
    {
        using var zip = new ZipArchive(flusso, ZipArchiveMode.Read, leaveOpen: true);
        string LeggiTesto(string nome)
        {
            using var lettore = new StreamReader(zip.GetEntry(nome)?.Open() ?? throw new InvalidDataException($"Nel libro manca {nome}."));
            return lettore.ReadToEnd();
        }

        var contenitore = XDocument.Parse(LeggiTesto("META-INF/container.xml"));
        var percorsoOpf = contenitore.Descendants().First(e => e.Name.LocalName == "rootfile").Attribute("full-path")!.Value;
        var cartella = percorsoOpf.Contains('/') ? percorsoOpf[..(percorsoOpf.LastIndexOf('/') + 1)] : "";
        var pacchetto = XDocument.Parse(LeggiTesto(percorsoOpf));

        string? Meta(string nome) => pacchetto.Descendants().FirstOrDefault(e => e.Name.LocalName == nome && e.Name.NamespaceName == Dc)?.Value.Trim();
        var contenuto = new ContenutoDocumento
        {
            Metadati = new Metadati(Meta("title") ?? "", Meta("creator") ?? "", Meta("description") ?? "", Meta("subject") ?? "",
                DateTime.TryParse(Meta("date"), out var data) ? data : null),
        };

        var elementi = pacchetto.Descendants().Where(e => e.Name.LocalName == "item")
            .ToDictionary(e => e.Attribute("id")!.Value, e => (Href: e.Attribute("href")!.Value, Tipo: e.Attribute("media-type")?.Value ?? "", Proprieta: e.Attribute("properties")?.Value ?? ""));
        var spina = pacchetto.Descendants().Where(e => e.Name.LocalName == "itemref").Select(e => e.Attribute("idref")!.Value);

        bool primo = true;
        foreach (var id in spina)
        {
            if (!elementi.TryGetValue(id, out var elemento) || !elemento.Tipo.Contains("html") || elemento.Proprieta.Contains("nav"))
                continue;
            var percorso = Combina(cartella, WebUtility.UrlDecode(elemento.Href));
            int prima = contenuto.Paragrafi.Count;
            HtmlFormato.Leggi(LeggiTesto(percorso), src =>
            {
                var voce = zip.GetEntry(Combina(percorso[..(percorso.LastIndexOf('/') + 1)], WebUtility.UrlDecode(src.Split('#')[0])));
                if (voce is null)
                    return null;
                using var s = voce.Open();
                using var copia = new MemoryStream();
                s.CopyTo(copia);
                return copia.ToArray();
            }, contenuto);
            // Ogni capitolo comincia su una pagina nuova.
            if (!primo && contenuto.Paragrafi.Count > prima)
                contenuto.Paragrafi[prima].SaltoPagina = true;
            primo = false;
        }
        return contenuto;
    }

    /// <summary>Un percorso dentro il libro, risolvendo "../" e "./".</summary>
    private static string Combina(string cartella, string relativo)
    {
        var parti = new List<string>();
        foreach (var parte in (cartella + relativo).Split('/'))
        {
            if (parte is "" or ".")
                continue;
            if (parte == ".." && parti.Count > 0)
                parti.RemoveAt(parti.Count - 1);
            else if (parte != "..")
                parti.Add(parte);
        }
        return string.Join('/', parti);
    }
}
