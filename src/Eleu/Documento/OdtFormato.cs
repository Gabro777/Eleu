using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Eleu.Documento;

/// <summary>
/// Lettura dei documenti OpenDocument (.odt), quelli di LibreOffice: uno zip con il testo in content.xml, stili e
/// pagina in styles.xml, proprietà in meta.xml e le immagini in Pictures. Si legge ciò che c'è nel modello di Eleu:
/// formattazione dei caratteri e dei paragrafi, titoli, elenchi, tabelle semplici, note, segnalibri, commenti e
/// immagini con il testo alternativo. La scrittura è in OdtFormato.Scrittura.cs.
/// </summary>
public static partial class OdtFormato
{
    public const string TipoMime = "application/vnd.oasis.opendocument.text";

    /// <summary>L'altezza di una riga di intestazione o piè di pagina, in punti.</summary>
    private const float RigaZona = 14f;

    private static readonly XNamespace Office = "urn:oasis:names:tc:opendocument:xmlns:office:1.0";
    private static readonly XNamespace Style = "urn:oasis:names:tc:opendocument:xmlns:style:1.0";
    private static readonly XNamespace Text = "urn:oasis:names:tc:opendocument:xmlns:text:1.0";
    private static readonly XNamespace Table = "urn:oasis:names:tc:opendocument:xmlns:table:1.0";
    private static readonly XNamespace Draw = "urn:oasis:names:tc:opendocument:xmlns:drawing:1.0";
    private static readonly XNamespace Fo = "urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0";
    private static readonly XNamespace XLink = "http://www.w3.org/1999/xlink";
    private static readonly XNamespace Dc = "http://purl.org/dc/elements/1.1/";
    private static readonly XNamespace Meta = "urn:oasis:names:tc:opendocument:xmlns:meta:1.0";
    private static readonly XNamespace Svg = "urn:oasis:names:tc:opendocument:xmlns:svg-compatible:1.0";
    private static readonly XNamespace Manifest = "urn:oasis:names:tc:opendocument:xmlns:manifest:1.0";

    /// <summary>Uno stile: famiglia, genitore, le proprietà di testo e di paragrafo, l'elenco e il livello di titolo.</summary>
    private sealed record StileOdt(
        string Famiglia,
        string? Genitore,
        bool Automatico,
        Dictionary<string, string> Testo,
        Dictionary<string, string> Paragrafo,
        string? Elenco,
        int LivelloTitolo,
        float LarghezzaColonna);

    public static ContenutoDocumento Leggi(Stream flusso)
    {
        using var zip = new ZipArchive(flusso, ZipArchiveMode.Read, leaveOpen: true);
        var contenutoXml = Xml(zip, "content.xml") ?? throw new InvalidDataException("Il file non è un documento OpenDocument: manca content.xml.");
        var stiliXml = Xml(zip, "styles.xml");
        var metaXml = Xml(zip, "meta.xml");

        var stili = new Dictionary<string, StileOdt>();
        var elenchi = new Dictionary<string, XElement>();
        foreach (var radice in new[] { stiliXml?.Root?.Element(Office + "styles"), stiliXml?.Root?.Element(Office + "automatic-styles"),
                     contenutoXml.Root?.Element(Office + "automatic-styles") })
            if (radice is not null)
                LeggiStili(radice, radice.Name.LocalName == "automatic-styles", stili, elenchi);

        var contenuto = new ContenutoDocumento();
        if (stiliXml is not null)
            contenuto.Pagina = LeggiPagina(stiliXml, stili);
        if (metaXml?.Root?.Element(Office + "meta") is { } meta)
            contenuto.Metadati = LeggiMetadati(meta);

        var corpo = contenutoXml.Root?.Element(Office + "body")?.Element(Office + "text");
        if (corpo is null)
            return contenuto;
        var lettore = new Lettore(contenuto, stili, elenchi, zip,
            corpo.Descendants(Office + "annotation-end").Select(x => (string?)x.Attribute(Office + "name")).OfType<string>().ToHashSet());
        lettore.Blocchi(corpo.Elements(), null);
        lettore.Avvisi();
        return contenuto;
    }

    private static XDocument? Xml(ZipArchive zip, string nome)
    {
        var voce = zip.GetEntry(nome);
        if (voce is null)
            return null;
        using var flusso = voce.Open();
        return XDocument.Load(flusso, LoadOptions.PreserveWhitespace);
    }

    private static void LeggiStili(XElement radice, bool automatici, Dictionary<string, StileOdt> stili, Dictionary<string, XElement> elenchi)
    {
        foreach (var e in radice.Elements())
        {
            if (e.Name == Text + "list-style" && (string?)e.Attribute(Style + "name") is string nomeElenco)
            {
                elenchi[nomeElenco] = e;
                continue;
            }
            if (e.Name != Style + "style" || (string?)e.Attribute(Style + "name") is not string nome)
                continue;
            static Dictionary<string, string> Proprieta(XElement? p) =>
                p?.Attributes().ToDictionary(a => a.Name.NamespaceName + "|" + a.Name.LocalName, a => a.Value) ?? [];
            stili[nome] = new StileOdt(
                (string?)e.Attribute(Style + "family") ?? "",
                (string?)e.Attribute(Style + "parent-style-name"),
                automatici,
                Proprieta(e.Element(Style + "text-properties")),
                Proprieta(e.Element(Style + "paragraph-properties")),
                (string?)e.Attribute(Style + "list-style-name"),
                int.TryParse((string?)e.Attribute(Style + "default-outline-level"), out int livello) ? livello : 0,
                Punti((string?)e.Element(Style + "table-column-properties")?.Attribute(Style + "column-width")) ?? 0);
        }
    }

    private static string Chiave(XNamespace ns, string nome) => ns.NamespaceName + "|" + nome;

    /// <summary>Una misura ODF ("2cm", "12pt", "0.5in", "10mm") in punti; null se non si capisce.</summary>
    public static float? Punti(string? misura)
    {
        if (string.IsNullOrWhiteSpace(misura))
            return null;
        misura = misura.Trim();
        int i = 0;
        while (i < misura.Length && (char.IsDigit(misura[i]) || misura[i] is '.' or '-' or '+'))
            i++;
        if (!float.TryParse(misura[..i], NumberStyles.Float, CultureInfo.InvariantCulture, out float valore))
            return null;
        return misura[i..].ToLowerInvariant() switch
        {
            "pt" => valore,
            "cm" => valore * 72f / 2.54f,
            "mm" => valore * 72f / 25.4f,
            "in" or "inch" => valore * 72f,
            "pc" => valore * 12f,
            "px" => valore * 0.75f,
            _ => null,
        };
    }

    private static int? Colore(string? valore) =>
        valore is { Length: 7 } && valore[0] == '#' && int.TryParse(valore[1..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int c)
            ? c
            : null;

    private static ImpostazioniPagina LeggiPagina(XDocument stiliXml, Dictionary<string, StileOdt> stili)
    {
        var pagina = new ImpostazioniPagina();
        var maestra = stiliXml.Root?.Element(Office + "master-styles")?.Elements(Style + "master-page").FirstOrDefault();
        var nomeLayout = (string?)maestra?.Attribute(Style + "page-layout-name");
        var layout = stiliXml.Root?.Element(Office + "automatic-styles")?.Elements(Style + "page-layout")
            .FirstOrDefault(l => (string?)l.Attribute(Style + "name") == nomeLayout);
        var p = layout?.Element(Style + "page-layout-properties");
        if (p is null)
            return pagina;

        float larghezza = Punti((string?)p.Attribute(Fo + "page-width")) ?? pagina.Larghezza;
        float altezza = Punti((string?)p.Attribute(Fo + "page-height")) ?? pagina.Altezza;
        bool orizzontale = (string?)p.Attribute(Style + "print-orientation") == "landscape" || larghezza > altezza;
        float sopra = Punti((string?)p.Attribute(Fo + "margin-top")) ?? pagina.MargineSopra;
        float sotto = Punti((string?)p.Attribute(Fo + "margin-bottom")) ?? pagina.MargineSotto;

        // In ODF l'intestazione sta dentro il margine: il testo comincia dopo l'intestazione e il suo spazio.
        string intestazione = maestra?.Element(Style + "header") is { } h ? TestoConVariabili(h) : "";
        string piede = maestra?.Element(Style + "footer") is { } f ? TestoConVariabili(f) : "";
        // Lo spazio della zona: la sua altezza (almeno una riga) più la distanza dal testo.
        float AltezzaZona(string nome)
        {
            var z = layout!.Element(Style + nome)?.Element(Style + "header-footer-properties");
            float minima = Punti((string?)z?.Attribute(Fo + "min-height")) ?? 0;
            float distanza = Punti((string?)z?.Attribute(Fo + (nome == "header-style" ? "margin-bottom" : "margin-top"))) ?? 0;
            return Math.Max(minima, RigaZona) + distanza;
        }
        float distanzaIntestazione = pagina.DistanzaIntestazione, distanzaPiede = pagina.DistanzaPiede;
        if (intestazione.Length > 0)
        {
            distanzaIntestazione = sopra;
            sopra += AltezzaZona("header-style");
        }
        if (piede.Length > 0)
        {
            distanzaPiede = sotto;
            sotto += AltezzaZona("footer-style");
        }

        return pagina with
        {
            Larghezza = Math.Min(larghezza, altezza),
            Altezza = Math.Max(larghezza, altezza),
            Orizzontale = orizzontale,
            MargineSinistro = Punti((string?)p.Attribute(Fo + "margin-left")) ?? pagina.MargineSinistro,
            MargineDestro = Punti((string?)p.Attribute(Fo + "margin-right")) ?? pagina.MargineDestro,
            MargineSopra = sopra,
            MargineSotto = sotto,
            DistanzaIntestazione = distanzaIntestazione,
            DistanzaPiede = distanzaPiede,
            Intestazione = intestazione,
            PiePagina = piede,
            EscludiPrimaPagina = maestra?.Element(Style + "header-first") is { } primo && !primo.Descendants(Text + "p").Any(x => x.Value.Length > 0),
        };
    }

    /// <summary>Il testo di intestazione o piè di pagina, con i campi come variabili di Eleu ({pagina}, {pagine}...).</summary>
    private static string TestoConVariabili(XElement zona)
    {
        var testo = new StringBuilder();
        void Leggi(XNode nodo)
        {
            switch (nodo)
            {
                case XText t:
                    testo.Append(t.Value);
                    break;
                case XElement e when e.Name == Text + "page-number":
                    testo.Append("{pagina}");
                    break;
                case XElement e when e.Name == Text + "page-count":
                    testo.Append("{pagine}");
                    break;
                case XElement e when e.Name == Text + "title":
                    testo.Append("{titolo}");
                    break;
                case XElement e when e.Name == Text + "date":
                    testo.Append("{data}");
                    break;
                case XElement e when e.Name == Text + "s":
                    testo.Append(' ', (int?)e.Attribute(Text + "c") ?? 1);
                    break;
                case XElement e when e.Name == Text + "tab":
                    testo.Append('\t');
                    break;
                case XElement e:
                    if (e.Name == Text + "p" && testo.Length > 0)
                        testo.Append(' ');
                    foreach (var figlio in e.Nodes())
                        Leggi(figlio);
                    break;
            }
        }
        Leggi(zona);
        return testo.ToString().Trim();
    }

    private static Metadati LeggiMetadati(XElement meta)
    {
        string Campo(XName nome) => meta.Element(nome)?.Value.Trim() ?? "";
        var autore = Campo(Meta + "initial-creator");
        if (autore.Length == 0)
            autore = Campo(Dc + "creator");
        DateTime? creato = DateTime.TryParse(Campo(Meta + "creation-date"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var data) ? data : null;
        return new Metadati(Campo(Dc + "title"), autore, Campo(Dc + "subject"),
            string.Join(", ", meta.Elements(Meta + "keyword").Select(k => k.Value.Trim()).Where(k => k.Length > 0)), creato);
    }

    /// <summary>Legge il corpo del documento, paragrafo per paragrafo, tenendo conto di elenchi e tabelle.</summary>
    private sealed class Lettore(ContenutoDocumento contenuto, Dictionary<string, StileOdt> stili, Dictionary<string, XElement> elenchi, ZipArchive zip,
        HashSet<string> finiCommenti)
    {
        private readonly Dictionary<string, int> _commentiAperti = [];
        private int _tabelle;
        private bool _saltoInSospeso;
        private bool _immaginiPerse, _celleUnite, _tabelleAnnidate, _noteChiusura, _caselleTesto;

        /// <summary>Paragrafi, titoli, elenchi, tabelle e sezioni, in ordine. <paramref name="cella"/>: dove stanno, se in tabella.</summary>
        public void Blocchi(IEnumerable<XElement> elementi, PosizioneCella? cella, (string? Stile, int Livello)? elenco = null)
        {
            foreach (var e in elementi)
            {
                if (e.Name == Text + "p" || e.Name == Text + "h")
                    Paragrafo(e, cella, elenco);
                else if (e.Name == Text + "list")
                    Elenco(e, cella, elenco);
                else if (e.Name == Table + "table")
                {
                    if (cella is not null)
                    {
                        // Una tabella dentro una tabella: il suo testo resta nella cella esterna.
                        _tabelleAnnidate = true;
                        Blocchi(e.Descendants().Where(x => x.Name == Text + "p" || x.Name == Text + "h"), cella);
                    }
                    else
                        Tabella(e);
                }
                else if (e.Name == Text + "section" || e.Name == Text + "index-body" || e.Name == Text + "table-of-content"
                         || e.Name == Text + "alphabetical-index" || e.Name == Text + "illustration-index" || e.Name == Text + "bibliography"
                         || e.Name == Text + "user-index" || e.Name == Text + "object-index" || e.Name == Text + "table-index")
                    Blocchi(e.Elements(), cella, elenco);
                else if (e.Name == Text + "soft-page-break")
                    continue;
                else if (e.Name == Draw + "frame")
                {
                    var conImmagine = new Paragrafo { Cella = cella };
                    Cornice(e, conImmagine);
                    if (conImmagine.Segmenti.Count > 0)
                        contenuto.Paragrafi.Add(conImmagine);
                }
            }
        }

        /// <summary>A fine lettura: cosa del file Eleu non ha potuto conservare.</summary>
        public void Avvisi()
        {
            if (_celleUnite || _tabelleAnnidate)
                contenuto.ElementiNonConservati.Add("celle unite o tabelle dentro le tabelle");
            if (_immaginiPerse)
                contenuto.ElementiNonConservati.Add("immagini o disegni che Eleu non sa leggere");
            if (_noteChiusura)
                contenuto.ElementiNonConservati.Add("note di chiusura (diventano note a piè di pagina)");
            if (_caselleTesto)
                contenuto.ElementiNonConservati.Add("caselle di testo");
        }

        private void Elenco(XElement lista, PosizioneCella? cella, (string? Stile, int Livello)? esterno)
        {
            var stile = (string?)lista.Attribute(Text + "style-name") ?? esterno?.Stile;
            var dentro = (stile, (esterno?.Livello ?? 0) + 1);
            foreach (var voce in lista.Elements().Where(x => x.Name == Text + "list-item" || x.Name == Text + "list-header"))
                Blocchi(voce.Elements(), cella, dentro);
        }

        /// <summary>Numerato o puntato: lo dice lo stile dell'elenco per quel livello.</summary>
        private bool Numerato(string? stileElenco, int livello)
        {
            if (stileElenco is null || !elenchi.TryGetValue(stileElenco, out var definizione))
                return false;
            var livelli = definizione.Elements().Where(x => x.Name.LocalName.StartsWith("list-level-style", StringComparison.Ordinal)).ToList();
            var scelto = livelli.FirstOrDefault(x => (int?)x.Attribute(Text + "level") == livello) ?? livelli.FirstOrDefault();
            return scelto?.Name == Text + "list-level-style-number" && (string?)scelto.Attribute(Style + "num-format") is not ("" or null);
        }

        private void Tabella(XElement tabella)
        {
            int numero = _tabelle++;
            // Larghezze delle colonne, anche quelle ripetute (table:number-columns-repeated).
            var larghezze = new List<float>();
            foreach (var colonna in tabella.Descendants(Table + "table-column").Where(x => x.Ancestors(Table + "table").First() == tabella))
            {
                int volte = Math.Clamp((int?)colonna.Attribute(Table + "number-columns-repeated") ?? 1, 1, 64);
                float larghezza = (string?)colonna.Attribute(Table + "style-name") is string nome && stili.TryGetValue(nome, out var stile)
                    ? stile.LarghezzaColonna
                    : 0;
                for (int i = 0; i < volte; i++)
                    larghezze.Add(larghezza);
            }

            int riga = 0;
            foreach (var r in tabella.Descendants(Table + "table-row").Where(x => x.Ancestors(Table + "table").First() == tabella))
            {
                bool intestazione = r.Parent?.Name == Table + "table-header-rows";
                int colonna = 0;
                foreach (var c in r.Elements())
                {
                    if (c.Name == Table + "covered-table-cell")
                    {
                        _celleUnite = true;
                        continue;
                    }
                    if (c.Name != Table + "table-cell")
                        continue;
                    if ((int?)c.Attribute(Table + "number-columns-spanned") > 1 || (int?)c.Attribute(Table + "number-rows-spanned") > 1)
                        _celleUnite = true;
                    int volte = Math.Clamp((int?)c.Attribute(Table + "number-columns-repeated") ?? 1, 1, 64);
                    for (int v = 0; v < volte; v++)
                    {
                        var posizione = new PosizioneCella(numero, riga, colonna, colonna < larghezze.Count ? larghezze[colonna] : 0, intestazione);
                        int prima = contenuto.Paragrafi.Count;
                        Blocchi(c.Elements(), posizione);
                        if (contenuto.Paragrafi.Count == prima)
                            contenuto.Paragrafi.Add(new Paragrafo { Cella = posizione });
                        colonna++;
                    }
                }
                riga++;
            }
        }

        private void Paragrafo(XElement p, PosizioneCella? cella, (string? Stile, int Livello)? elenco)
        {
            var nomeStile = (string?)p.Attribute(Text + "style-name");
            var paragrafo = new Paragrafo { Cella = cella };
            if (p.Name == Text + "h")
                paragrafo.LivelloTitolo = Math.Clamp((int?)p.Attribute(Text + "outline-level") ?? 1, 1, 9);
            else if (LivelloDaStile(nomeStile) is int livello and > 0)
                paragrafo.LivelloTitolo = Math.Min(livello, 9);

            // Un "salto di pagina dopo" del paragrafo precedente vale per questo.
            bool saltoDalPrecedente = _saltoInSospeso;
            _saltoInSospeso = false;
            ProprietaParagrafo(nomeStile, paragrafo);
            if (saltoDalPrecedente)
                paragrafo.SaltoPagina = true;

            if (elenco is { } inElenco && inElenco.Livello > 0 && paragrafo.LivelloTitolo == 0)
            {
                if (Numerato(inElenco.Stile ?? StileElencoDi(nomeStile), inElenco.Livello))
                    paragrafo.Numerato = true;
                else
                    paragrafo.Elenco = true;
                paragrafo.RientroSinistro = 0;
                paragrafo.RientroPrimaRiga = 0;
            }

            var formatoParagrafo = FormatoTesto(nomeStile, new Segmento(""));
            var corrente = paragrafo;
            Inline(p.Nodes(), formatoParagrafo, ref corrente);
            corrente.TogliSpaziFinali();
            contenuto.Paragrafi.Add(corrente);
        }

        private int? LivelloDaStile(string? nome)
        {
            for (int giri = 0; nome is not null && stili.TryGetValue(nome, out var stile) && giri < 20; giri++)
            {
                if (stile.LivelloTitolo > 0)
                    return stile.LivelloTitolo;
                nome = stile.Genitore;
            }
            return null;
        }

        private string? StileElencoDi(string? nome)
        {
            for (int giri = 0; nome is not null && stili.TryGetValue(nome, out var stile) && giri < 20; giri++)
            {
                if (stile.Elenco is not null)
                    return stile.Elenco;
                nome = stile.Genitore;
            }
            return null;
        }

        /// <summary>
        /// Allineamento, rientri, spazi, interlinea e salto di pagina: dalla formattazione diretta (stili automatici) e,
        /// per l'allineamento, anche dagli stili con nome come "Centrato".
        /// </summary>
        private void ProprietaParagrafo(string? nome, Paragrafo paragrafo)
        {
            var catena = Catena(nome).ToList();
            string? Valore(XNamespace ns, string attributo, bool soloAutomatici = true) =>
                catena.Where(s => !soloAutomatici || s.Automatico).Select(s => s.Paragrafo.GetValueOrDefault(Chiave(ns, attributo))).FirstOrDefault(v => v is not null);

            paragrafo.Allineamento = Valore(Fo, "text-align", soloAutomatici: false) switch
            {
                "center" => Allineamento.Centro,
                "end" or "right" => Allineamento.Destra,
                "justify" => Allineamento.Giustificato,
                _ => Allineamento.Sinistra,
            };
            paragrafo.RientroSinistro = Punti(Valore(Fo, "margin-left")) ?? 0;
            paragrafo.RientroDestro = Punti(Valore(Fo, "margin-right")) ?? 0;
            paragrafo.RientroPrimaRiga = Punti(Valore(Fo, "text-indent")) ?? 0;
            paragrafo.SpazioPrima = Punti(Valore(Fo, "margin-top")) ?? 0;
            paragrafo.SpazioDopo = Punti(Valore(Fo, "margin-bottom")) ?? 0;
            if (Valore(Fo, "line-height") is string interlinea && interlinea.EndsWith('%')
                && float.TryParse(interlinea[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out float percento) && percento > 0)
                paragrafo.Interlinea = percento / 100f;
            if (Valore(Fo, "break-before", soloAutomatici: false) == "page")
                paragrafo.SaltoPagina = true;
            if (Valore(Fo, "break-after", soloAutomatici: false) == "page")
                _saltoInSospeso = true;
        }

        /// <summary>Lo stile e i suoi genitori, dal più vicino.</summary>
        private IEnumerable<StileOdt> Catena(string? nome)
        {
            for (int giri = 0; nome is not null && stili.TryGetValue(nome, out var stile) && giri < 20; giri++)
            {
                yield return stile;
                nome = stile.Genitore;
            }
        }

        /// <summary>
        /// La formattazione dei caratteri secondo lo stile, sopra quella data. Degli stili di paragrafo con nome si
        /// prendono solo grassetto, corsivo e simili, non carattere e dimensione: quelli li decide Eleu.
        /// </summary>
        private Segmento FormatoTesto(string? nome, Segmento base_)
        {
            var catena = Catena(nome).Reverse().ToList(); // dal più lontano: il più vicino vince
            var f = base_;
            foreach (var stile in catena)
            {
                bool completo = stile.Automatico || stile.Famiglia != "paragraph";
                var t = stile.Testo;
                if (t.GetValueOrDefault(Chiave(Fo, "font-weight")) is string peso)
                    f = f with { Grassetto = peso == "bold" || (int.TryParse(peso, out int n) && n >= 600) };
                if (t.GetValueOrDefault(Chiave(Fo, "font-style")) is string corsivo)
                    f = f with { Corsivo = corsivo is "italic" or "oblique" };
                if (t.GetValueOrDefault(Chiave(Style, "text-underline-style")) is string sottolineato)
                    f = f with { Sottolineato = sottolineato != "none" };
                if (t.GetValueOrDefault(Chiave(Style, "text-line-through-style")) is string barrato)
                    f = f with { Barrato = barrato != "none" };
                if (t.GetValueOrDefault(Chiave(Style, "text-position")) is string posizione)
                    f = f with { Posizione = PosizioneDa(posizione) };
                if (!completo)
                    continue;
                if (Punti(t.GetValueOrDefault(Chiave(Fo, "font-size"))) is float dimensione)
                    f = f with { Dimensione = dimensione };
                if ((t.GetValueOrDefault(Chiave(Style, "font-name")) ?? t.GetValueOrDefault(Chiave(Fo, "font-family"))) is string carattere)
                    f = f with { Carattere = carattere.Trim('\'', '"') };
                if (t.GetValueOrDefault(Chiave(Fo, "color")) is string colore)
                    f = f with { Colore = Colore(colore) is int c && c != 0 ? c : null };
                if (t.GetValueOrDefault(Chiave(Fo, "background-color")) is string sfondo)
                    f = f with { Sfondo = Colore(sfondo) };
                if (Punti(t.GetValueOrDefault(Chiave(Fo, "letter-spacing"))) is float spaziatura)
                    f = f with { Spaziatura = spaziatura };
            }
            return f;
        }

        private static PosizioneTesto PosizioneDa(string valore)
        {
            var primo = valore.Trim().Split(' ')[0];
            if (primo == "super")
                return PosizioneTesto.Apice;
            if (primo == "sub")
                return PosizioneTesto.Pedice;
            return float.TryParse(primo.TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out float v) && v != 0
                ? (v > 0 ? PosizioneTesto.Apice : PosizioneTesto.Pedice)
                : PosizioneTesto.Normale;
        }

        /// <summary>Il contenuto di un paragrafo: testo, formattazione, a capo, note, segnalibri, commenti, immagini.</summary>
        private void Inline(IEnumerable<XNode> nodi, Segmento formato, ref Paragrafo paragrafo)
        {
            foreach (var nodo in nodi)
            {
                if (nodo is XText t)
                {
                    // In ODF gli spazi in fila contano come uno; quelli in più sono text:s.
                    var testo = t.Value.Replace('\n', ' ').Replace('\r', ' ').Replace('\t', ' ');
                    var compatto = new StringBuilder();
                    var finora = paragrafo.Testo;
                    bool dopoSpazio = finora.Length == 0 || finora[^1] == ' ';
                    foreach (var c in testo)
                    {
                        if (c == ' ' && dopoSpazio)
                            continue;
                        compatto.Append(c);
                        dopoSpazio = c == ' ';
                    }
                    paragrafo.Aggiungi(formato with { Testo = compatto.ToString() });
                    continue;
                }
                if (nodo is not XElement e)
                    continue;

                if (e.Name == Text + "span")
                    Inline(e.Nodes(), FormatoTesto((string?)e.Attribute(Text + "style-name"), formato), ref paragrafo);
                else if (e.Name == Text + "s")
                    paragrafo.Aggiungi(formato with { Testo = new string(' ', Math.Clamp((int?)e.Attribute(Text + "c") ?? 1, 1, 1000)) });
                else if (e.Name == Text + "tab")
                    paragrafo.Aggiungi(formato with { Testo = "\t" });
                else if (e.Name == Text + "line-break")
                {
                    paragrafo.TogliSpaziFinali();
                    contenuto.Paragrafi.Add(paragrafo);
                    var nuovo = paragrafo.CopiaProprieta();
                    nuovo.Cella = paragrafo.Cella;
                    paragrafo = nuovo;
                }
                else if (e.Name == Text + "note")
                {
                    if ((string?)e.Attribute(Text + "note-class") == "endnote")
                        _noteChiusura = true;
                    if (contenuto.Note.Count < Note.Massimo)
                    {
                        var corpoNota = e.Element(Text + "note-body");
                        contenuto.Note.Add(string.Join("\n", corpoNota?.Descendants().Where(x => x.Name == Text + "p" || x.Name == Text + "h")
                            .Select(TestoSemplice) ?? []).Trim());
                        paragrafo.Aggiungi(new Segmento(Note.Segnaposto(contenuto.Note.Count - 1).ToString()));
                    }
                }
                else if (e.Name == Text + "bookmark" || e.Name == Text + "bookmark-start")
                {
                    if ((string?)e.Attribute(Text + "name") is string nome && !Segnalibri.Nascosto(nome) && contenuto.Segnalibri.Count < Segnalibri.Massimo)
                    {
                        contenuto.Segnalibri.Add(nome.Trim());
                        paragrafo.Aggiungi(new Segmento(Segnalibri.Segnaposto(contenuto.Segnalibri.Count - 1).ToString()));
                    }
                }
                else if (e.Name == Office + "annotation")
                    Commento(e, paragrafo);
                else if (e.Name == Office + "annotation-end")
                {
                    if ((string?)e.Attribute(Office + "name") is string nome && _commentiAperti.Remove(nome, out int indice))
                        paragrafo.Aggiungi(new Segmento(Commenti.Fine(indice).ToString()));
                }
                else if (e.Name == Draw + "frame" || e.Name == Draw + "a")
                    Cornice(e, paragrafo);
                else if (e.Name == Text + "note-citation" || e.Name == Text + "number" || e.Name == Text + "bookmark-end" || e.Name == Text + "soft-page-break"
                         || e.Name == Text + "reference-mark-start" || e.Name == Text + "reference-mark-end")
                    continue;
                else
                    // Collegamenti, campi e tutto il resto: conta il testo che mostrano.
                    Inline(e.Nodes(), formato, ref paragrafo);
            }
        }

        /// <summary>
        /// Un commento: se ha un nome e più avanti c'è la sua fine, è sul testo che sta in mezzo; altrimenti su un punto.
        /// </summary>
        private void Commento(XElement annotazione, Paragrafo paragrafo)
        {
            if (contenuto.Commenti.Count >= Commenti.Massimo)
                return;
            var testo = string.Join("\n", annotazione.Elements().Where(x => x.Name == Text + "p" || x.Name == Text + "h").Select(TestoSemplice)).Trim();
            var autore = annotazione.Element(Dc + "creator")?.Value.Trim() ?? "";
            DateTime? data = DateTime.TryParse(annotazione.Element(Dc + "date")?.Value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var d) ? d : null;
            contenuto.Commenti.Add(new Commento(testo, autore, data));
            int indice = contenuto.Commenti.Count - 1;
            paragrafo.Aggiungi(new Segmento(Commenti.Inizio(indice).ToString()));
            var nome = (string?)annotazione.Attribute(Office + "name");
            bool conFine = nome is not null && finiCommenti.Contains(nome) && !_commentiAperti.ContainsKey(nome);
            if (conFine)
                _commentiAperti[nome!] = indice;
            else
                paragrafo.Aggiungi(new Segmento(Commenti.Fine(indice).ToString()));
        }

        /// <summary>Una cornice: se contiene un'immagine che Eleu sa leggere, diventa un'immagine con il testo alternativo.</summary>
        private void Cornice(XElement cornice, Paragrafo paragrafo)
        {
            if (cornice.Name == Draw + "a")
            {
                foreach (var interna in cornice.Elements(Draw + "frame"))
                    Cornice(interna, paragrafo);
                return;
            }
            if (cornice.Element(Draw + "text-box") is not null)
            {
                _caselleTesto = true;
                return;
            }
            var href = (string?)cornice.Element(Draw + "image")?.Attribute(XLink + "href");
            var voce = href is null ? null : zip.GetEntry(href.TrimStart('.', '/'));
            if (voce is null || contenuto.Immagini.Count >= Immagini.Massimo)
            {
                _immaginiPerse = true;
                return;
            }
            var titolo = cornice.Element(Svg + "title")?.Value.Trim() ?? "";
            var descrizione = cornice.Element(Svg + "desc")?.Value.Trim() ?? "";
            try
            {
                using var flusso = voce.Open();
                using var copia = new MemoryStream();
                flusso.CopyTo(copia);
                var immagine = Immagini.DaDati(copia.ToArray(), titolo.Length > 0 ? titolo : descrizione);
                if (Punti((string?)cornice.Attribute(Svg + "width")) is float larghezza && Punti((string?)cornice.Attribute(Svg + "height")) is float altezza
                    && larghezza > 0 && altezza > 0)
                    immagine = immagine with { Larghezza = larghezza, Altezza = altezza };
                contenuto.Immagini.Add(immagine);
                paragrafo.Aggiungi(new Segmento(Immagini.Segnaposto(contenuto.Immagini.Count - 1).ToString()));
            }
            catch (Exception ex) when (ex is ArgumentException or System.Runtime.InteropServices.ExternalException or OutOfMemoryException or InvalidDataException)
            {
                _immaginiPerse = true;
            }
        }

        /// <summary>Il testo di un paragrafo di nota o commento, con spazi e tabulazioni.</summary>
        private static string TestoSemplice(XElement p)
        {
            var testo = new StringBuilder();
            foreach (var n in p.DescendantNodes())
            {
                if (n is XText t && t.Parent?.Name != Text + "note-citation")
                    testo.Append(t.Value);
                else if (n is XElement e && e.Name == Text + "s")
                    testo.Append(' ', (int?)e.Attribute(Text + "c") ?? 1);
                else if (n is XElement e2 && e2.Name == Text + "tab")
                    testo.Append('\t');
            }
            return testo.ToString().Trim();
        }
    }
}
