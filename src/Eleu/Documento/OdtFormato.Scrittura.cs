using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Eleu.Documento;

// Scrittura dei documenti OpenDocument (.odt): content.xml con il testo e la formattazione diretta (stili automatici),
// styles.xml con gli stili dei titoli, la pagina, intestazione e piè di pagina, meta.xml con le proprietà.
public static partial class OdtFormato
{
    private static readonly XNamespace Loext = "urn:org:documentfoundation:names:experimental:office:xmlns:loext:1.0";

    public static void Scrivi(ContenutoDocumento contenuto, Stream flusso, string carattere = "Calibri", float dimensioneBase = ContenutoDocumento.DimensioneBase)
    {
        var scrittore = new Scrittore(contenuto, carattere, dimensioneBase);
        var corpo = scrittore.Corpo();

        using var zip = new ZipArchive(flusso, ZipArchiveMode.Create, leaveOpen: true);
        // Il primo file deve essere "mimetype", non compresso: così i programmi riconoscono il formato.
        var tipo = zip.CreateEntry("mimetype", CompressionLevel.NoCompression);
        using (var s = tipo.Open())
            s.Write(Encoding.ASCII.GetBytes(TipoMime));
        Salva(zip, "content.xml", scrittore.Contenuto(corpo));
        Salva(zip, "styles.xml", scrittore.Stili());
        Salva(zip, "meta.xml", Metadati(contenuto.Metadati));
        foreach (var (percorso, dati, _) in scrittore.Figure)
        {
            var voce = zip.CreateEntry(percorso, CompressionLevel.NoCompression);
            using var s = voce.Open();
            s.Write(dati);
        }
        Salva(zip, "META-INF/manifest.xml", ElencoFile(scrittore.Figure.Select(f => (f.Percorso, f.Tipo))));
    }

    private static void Salva(ZipArchive zip, string nome, XDocument xml)
    {
        var voce = zip.CreateEntry(nome, CompressionLevel.Optimal);
        using var s = voce.Open();
        using var w = XmlWriter.Create(s, new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = false });
        xml.Save(w);
    }

    /// <summary>Gli attributi xmlns per tutti i prefissi usati, da mettere sull'elemento radice.</summary>
    private static IEnumerable<XAttribute> Spazi() =>
    [
        new(XNamespace.Xmlns + "office", Office), new(XNamespace.Xmlns + "style", Style), new(XNamespace.Xmlns + "text", Text),
        new(XNamespace.Xmlns + "table", Table), new(XNamespace.Xmlns + "draw", Draw), new(XNamespace.Xmlns + "fo", Fo),
        new(XNamespace.Xmlns + "xlink", XLink), new(XNamespace.Xmlns + "dc", Dc), new(XNamespace.Xmlns + "meta", Meta),
        new(XNamespace.Xmlns + "svg", Svg), new(XNamespace.Xmlns + "loext", Loext),
    ];

    private static string Pt(float punti) => punti.ToString("0.###", CultureInfo.InvariantCulture) + "pt";

    private static string Esadecimale(int colore) => "#" + (colore & 0xFFFFFF).ToString("x6", CultureInfo.InvariantCulture);

    /// <summary>Il testo senza i caratteri che in XML non possono stare.</summary>
    private static string Pulito(string testo) =>
        testo.All(XmlConvert.IsXmlChar) ? testo : string.Concat(testo.Where(XmlConvert.IsXmlChar));

    private static XDocument Metadati(Metadati m)
    {
        var meta = new XElement(Office + "meta", new XElement(Meta + "generator", "Eleu"));
        if (m.Titolo.Length > 0)
            meta.Add(new XElement(Dc + "title", Pulito(m.Titolo)));
        if (m.Oggetto.Length > 0)
            meta.Add(new XElement(Dc + "subject", Pulito(m.Oggetto)));
        foreach (var parola in m.ParoleChiave.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            meta.Add(new XElement(Meta + "keyword", Pulito(parola)));
        if (m.Autore.Length > 0)
        {
            meta.Add(new XElement(Meta + "initial-creator", Pulito(m.Autore)));
            meta.Add(new XElement(Dc + "creator", Pulito(m.Autore)));
        }
        meta.Add(new XElement(Meta + "creation-date", (m.Creato ?? DateTime.Now).ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture)));
        meta.Add(new XElement(Dc + "date", DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture)));
        meta.Add(new XElement(Dc + "language", "it-IT"));
        return new XDocument(new XElement(Office + "document-meta", Spazi(), new XAttribute(Office + "version", "1.3"), meta));
    }

    private static XDocument ElencoFile(IEnumerable<(string Percorso, string Tipo)> figure)
    {
        XElement Voce(string percorso, string tipo) =>
            new(Manifest + "file-entry", new XAttribute(Manifest + "full-path", percorso), new XAttribute(Manifest + "media-type", tipo));
        var radice = new XElement(Manifest + "manifest", new XAttribute(XNamespace.Xmlns + "manifest", Manifest), new XAttribute(Manifest + "version", "1.3"),
            new XElement(Manifest + "file-entry", new XAttribute(Manifest + "full-path", "/"), new XAttribute(Manifest + "version", "1.3"),
                new XAttribute(Manifest + "media-type", TipoMime)),
            Voce("content.xml", "text/xml"), Voce("styles.xml", "text/xml"), Voce("meta.xml", "text/xml"));
        foreach (var (percorso, tipo) in figure)
            radice.Add(Voce(percorso, tipo));
        return new XDocument(radice);
    }

    /// <summary>Costruisce content.xml e styles.xml, raccogliendo strada facendo gli stili automatici e le immagini.</summary>
    private sealed class Scrittore(ContenutoDocumento contenuto, string carattere, float dimensioneBase)
    {
        private readonly Dictionary<string, string> _stiliParagrafo = [];   // proprietà → nome (P1, P2...)
        private readonly Dictionary<Segmento, string> _stiliTesto = [];     // formato → nome (T1, T2...)
        private readonly List<XElement> _automatici = [];
        private readonly HashSet<string> _caratteri = [carattere];
        private readonly HashSet<string> _segnalibri = new(StringComparer.Ordinal);
        private readonly Dictionary<int, (bool ConFine, bool SuUnPunto)> _commenti = [];
        private int _note;

        public List<(string Percorso, byte[] Dati, string Tipo)> Figure { get; } = [];

        public XElement Corpo()
        {
            PreparaCommenti();
            var testo = new XElement(Office + "text");
            int i = 0;
            var paragrafi = contenuto.Paragrafi;
            while (i < paragrafi.Count)
            {
                if (paragrafi[i].Cella is { } cella)
                {
                    int fine = i;
                    while (fine < paragrafi.Count && paragrafi[fine].Cella?.Tabella == cella.Tabella)
                        fine++;
                    testo.Add(Tabella(cella.Tabella, paragrafi.Skip(i).Take(fine - i).ToList()));
                    i = fine;
                }
                else
                {
                    int fine = i;
                    while (fine < paragrafi.Count && paragrafi[fine].Cella is null)
                        fine++;
                    Blocchi(testo, paragrafi.Skip(i).Take(fine - i).ToList());
                    i = fine;
                }
            }
            if (!testo.HasElements)
                testo.Add(new XElement(Text + "p"));
            return testo;
        }

        /// <summary>
        /// Quali commenti hanno inizio e fine (si scrivono con nome e fine), e quali sono su un punto (inizio e fine
        /// attaccati: un commento senza intervallo).
        /// </summary>
        private void PreparaCommenti()
        {
            var tutto = string.Join('\n', contenuto.Paragrafi.Select(p => p.Testo));
            for (int i = 0; i < contenuto.Commenti.Count; i++)
            {
                int inizio = tutto.IndexOf(Commenti.Inizio(i)), fine = tutto.IndexOf(Commenti.Fine(i));
                if (inizio < 0)
                    continue;
                bool conFine = fine > inizio;
                _commenti[i] = (conFine && fine > inizio + 1, conFine && fine == inizio + 1);
            }
        }

        public XDocument Contenuto(XElement corpo)
        {
            var radice = new XElement(Office + "document-content", Spazi(), new XAttribute(Office + "version", "1.3"),
                Caratteri(),
                new XElement(Office + "automatic-styles", StiliElenco(), StileCornice(), _automatici),
                new XElement(Office + "body", corpo));
            return new XDocument(radice);
        }

        private XElement Caratteri() => new(Office + "font-face-decls",
            _caratteri.Order().Select(c => new XElement(Style + "font-face", new XAttribute(Style + "name", c),
                new XAttribute(Svg + "font-family", c.Contains(' ') ? $"'{c}'" : c))));

        private static IEnumerable<XElement> StiliElenco()
        {
            // L1 puntato, L2 numerato, su tutti i livelli.
            var puntato = new XElement(Text + "list-style", new XAttribute(Style + "name", "L1"));
            var numerato = new XElement(Text + "list-style", new XAttribute(Style + "name", "L2"));
            for (int livello = 1; livello <= 10; livello++)
            {
                var rientro = new XElement(Style + "list-level-properties",
                    new XAttribute(Text + "list-level-position-and-space-mode", "label-alignment"),
                    new XElement(Style + "list-level-label-alignment", new XAttribute(Text + "label-followed-by", "listtab"),
                        new XAttribute(Fo + "text-indent", "-0.635cm"), new XAttribute(Fo + "margin-left", $"{(0.635 * (livello + 1)).ToString("0.###", CultureInfo.InvariantCulture)}cm")));
                puntato.Add(new XElement(Text + "list-level-style-bullet", new XAttribute(Text + "level", livello),
                    new XAttribute(Text + "bullet-char", "•"), new XElement(rientro)));
                numerato.Add(new XElement(Text + "list-level-style-number", new XAttribute(Text + "level", livello),
                    new XAttribute(Style + "num-suffix", "."), new XAttribute(Style + "num-format", "1"), new XElement(rientro)));
            }
            return [puntato, numerato];
        }

        private static XElement StileCornice() => new(Style + "style", new XAttribute(Style + "name", "fr1"), new XAttribute(Style + "family", "graphic"),
            new XElement(Style + "graphic-properties", new XAttribute(Style + "vertical-pos", "top"), new XAttribute(Style + "vertical-rel", "baseline")));

        /// <summary>Paragrafi fuori dalle tabelle o dentro una cella: quelli in elenco vanno in un text:list per ogni gruppo.</summary>
        private void Blocchi(XElement contenitore, List<Paragrafo> paragrafi)
        {
            int i = 0;
            while (i < paragrafi.Count)
            {
                var p = paragrafi[i];
                if ((p.Elenco || p.Numerato) && p.LivelloTitolo == 0)
                {
                    var lista = new XElement(Text + "list", new XAttribute(Text + "style-name", p.Numerato ? "L2" : "L1"));
                    while (i < paragrafi.Count && paragrafi[i].LivelloTitolo == 0 && paragrafi[i].Numerato == p.Numerato
                           && paragrafi[i].Elenco == p.Elenco && !(i > 0 && paragrafi[i].SaltoPagina && lista.HasElements))
                    {
                        lista.Add(new XElement(Text + "list-item", Paragrafo(paragrafi[i])));
                        i++;
                    }
                    contenitore.Add(lista);
                    continue;
                }
                contenitore.Add(Paragrafo(p));
                i++;
            }
        }

        private XElement Tabella(int numero, List<Paragrafo> paragrafi)
        {
            var celle = paragrafi.Select(p => p.Cella!).ToList();
            int colonne = celle.Max(c => c.Colonna) + 1;
            var larghezze = Enumerable.Range(0, colonne)
                .Select(c => celle.Where(x => x.Colonna == c).Select(x => x.Larghezza).FirstOrDefault(l => l > 0))
                .Select(l => l > 0 ? l : 113.4f) // 4 cm, come in Word
                .ToList();

            string nome = $"Tabella{numero + 1}";
            _automatici.Add(new XElement(Style + "style", new XAttribute(Style + "name", nome), new XAttribute(Style + "family", "table"),
                new XElement(Style + "table-properties", new XAttribute(Style + "width", Pt(larghezze.Sum())), new XAttribute(Table + "align", "left"))));
            var tabella = new XElement(Table + "table", new XAttribute(Table + "name", nome), new XAttribute(Table + "style-name", nome));
            for (int c = 0; c < colonne; c++)
            {
                var stileColonna = $"{nome}.C{c + 1}";
                _automatici.Add(new XElement(Style + "style", new XAttribute(Style + "name", stileColonna), new XAttribute(Style + "family", "table-column"),
                    new XElement(Style + "table-column-properties", new XAttribute(Style + "column-width", Pt(larghezze[c])))));
                tabella.Add(new XElement(Table + "table-column", new XAttribute(Table + "style-name", stileColonna)));
            }
            if (!_automatici.Any(s => (string?)s.Attribute(Style + "name") == "Cella"))
                _automatici.Add(new XElement(Style + "style", new XAttribute(Style + "name", "Cella"), new XAttribute(Style + "family", "table-cell"),
                    new XElement(Style + "table-cell-properties", new XAttribute(Fo + "padding", "0.1cm"), new XAttribute(Fo + "border", "0.5pt solid #000000"))));

            XElement? intestazione = null;
            foreach (var riga in paragrafi.GroupBy(p => p.Cella!.Riga).OrderBy(g => g.Key))
            {
                var elementoRiga = new XElement(Table + "table-row");
                for (int c = 0; c < colonne; c++)
                {
                    var cella = new XElement(Table + "table-cell", new XAttribute(Table + "style-name", "Cella"), new XAttribute(Office + "value-type", "string"));
                    var dentro = riga.Where(p => p.Cella!.Colonna == c).ToList();
                    if (dentro.Count == 0)
                        cella.Add(new XElement(Text + "p"));
                    else
                        Blocchi(cella, dentro);
                    elementoRiga.Add(cella);
                }
                if (riga.First().Cella!.Intestazione && (intestazione is not null || tabella.Elements(Table + "table-row").Any() == false))
                {
                    intestazione ??= new XElement(Table + "table-header-rows");
                    if (intestazione.Parent is null)
                        tabella.Add(intestazione);
                    intestazione.Add(elementoRiga);
                }
                else
                    tabella.Add(elementoRiga);
            }
            return tabella;
        }

        private XElement Paragrafo(Paragrafo p)
        {
            bool titolo = p.LivelloTitolo > 0;
            var elemento = new XElement(Text + (titolo ? "h" : "p"));
            elemento.Add(new XAttribute(Text + "style-name", StileParagrafo(p)));
            if (titolo)
                elemento.Add(new XAttribute(Text + "outline-level", p.LivelloTitolo));
            bool dopoSpazio = true; // a inizio paragrafo uno spazio va scritto come text:s
            foreach (var segmento in p.Segmenti)
                Segmento(elemento, segmento, ref dopoSpazio);
            return elemento;
        }

        private string StileParagrafo(Paragrafo p)
        {
            var genitore = p.LivelloTitolo > 0 ? $"Heading_20_{p.LivelloTitolo}" : "Standard";
            var proprieta = new List<XAttribute>();
            if (p.Allineamento != Allineamento.Sinistra)
                proprieta.Add(new XAttribute(Fo + "text-align", p.Allineamento switch
                {
                    Allineamento.Centro => "center",
                    Allineamento.Destra => "end",
                    _ => "justify",
                }));
            if (p.RientroSinistro != 0)
                proprieta.Add(new XAttribute(Fo + "margin-left", Pt(p.RientroSinistro)));
            if (p.RientroDestro != 0)
                proprieta.Add(new XAttribute(Fo + "margin-right", Pt(p.RientroDestro)));
            if (p.RientroPrimaRiga != 0)
                proprieta.Add(new XAttribute(Fo + "text-indent", Pt(p.RientroPrimaRiga)));
            if (p.SpazioPrima != 0)
                proprieta.Add(new XAttribute(Fo + "margin-top", Pt(p.SpazioPrima)));
            if (p.SpazioDopo != 0)
                proprieta.Add(new XAttribute(Fo + "margin-bottom", Pt(p.SpazioDopo)));
            if (p.Interlinea != 1f)
                proprieta.Add(new XAttribute(Fo + "line-height", $"{(p.Interlinea * 100).ToString("0", CultureInfo.InvariantCulture)}%"));
            if (p.SaltoPagina)
                proprieta.Add(new XAttribute(Fo + "break-before", "page"));
            if (proprieta.Count == 0)
                return genitore;

            var chiave = genitore + "|" + string.Join("|", proprieta.Select(a => a.ToString()));
            if (_stiliParagrafo.TryGetValue(chiave, out var nome))
                return nome;
            nome = $"P{_stiliParagrafo.Count + 1}";
            _stiliParagrafo[chiave] = nome;
            _automatici.Add(new XElement(Style + "style", new XAttribute(Style + "name", nome), new XAttribute(Style + "family", "paragraph"),
                new XAttribute(Style + "parent-style-name", genitore), new XElement(Style + "paragraph-properties", proprieta)));
            return nome;
        }

        private string? StileTesto(Segmento s)
        {
            var formato = s with { Testo = "" };
            if (formato == new Segmento(""))
                return null;
            if (_stiliTesto.TryGetValue(formato, out var nome))
                return nome;
            var proprieta = new XElement(Style + "text-properties");
            if (s.Carattere is string c)
            {
                _caratteri.Add(c);
                proprieta.Add(new XAttribute(Style + "font-name", c));
            }
            if (s.Dimensione is float d)
                proprieta.Add(new XAttribute(Fo + "font-size", Pt(d)));
            if (s.Grassetto)
                proprieta.Add(new XAttribute(Fo + "font-weight", "bold"));
            if (s.Corsivo)
                proprieta.Add(new XAttribute(Fo + "font-style", "italic"));
            if (s.Sottolineato)
                proprieta.Add(new XAttribute(Style + "text-underline-style", "solid"), new XAttribute(Style + "text-underline-width", "auto"),
                    new XAttribute(Style + "text-underline-color", "font-color"));
            if (s.Barrato)
                proprieta.Add(new XAttribute(Style + "text-line-through-style", "solid"));
            if (s.Posizione != PosizioneTesto.Normale)
                proprieta.Add(new XAttribute(Style + "text-position", s.Posizione == PosizioneTesto.Apice ? "super 58%" : "sub 58%"));
            if (s.Colore is int colore)
                proprieta.Add(new XAttribute(Fo + "color", Esadecimale(colore)));
            if (s.Sfondo is int sfondo)
                proprieta.Add(new XAttribute(Fo + "background-color", Esadecimale(sfondo)));
            if (s.Spaziatura != 0)
                proprieta.Add(new XAttribute(Fo + "letter-spacing", Pt(s.Spaziatura)));
            nome = $"T{_stiliTesto.Count + 1}";
            _stiliTesto[formato] = nome;
            _automatici.Add(new XElement(Style + "style", new XAttribute(Style + "name", nome), new XAttribute(Style + "family", "text"), proprieta));
            return nome;
        }

        /// <summary>Il testo di un segmento, con i segnaposto al posto giusto e gli spazi come li vuole ODF.</summary>
        private void Segmento(XElement paragrafo, Segmento segmento, ref bool dopoSpazio)
        {
            var stile = StileTesto(segmento);
            XElement? span = null;
            XElement Destinazione()
            {
                if (stile is null)
                    return paragrafo;
                if (span is null)
                {
                    span = new XElement(Text + "span", new XAttribute(Text + "style-name", stile));
                    paragrafo.Add(span);
                }
                return span;
            }

            var testo = new StringBuilder();
            void Svuota()
            {
                if (testo.Length > 0)
                    Destinazione().Add(testo.ToString());
                testo.Clear();
            }

            int i = 0;
            var t = segmento.Testo;
            while (i < t.Length)
            {
                char c = t[i];
                if (c == ' ')
                {
                    int n = 0;
                    while (i + n < t.Length && t[i + n] == ' ')
                        n++;
                    i += n;
                    // Il primo spazio dopo un carattere normale è testo; gli altri (e quelli a inizio paragrafo) sono text:s.
                    if (!dopoSpazio)
                    {
                        testo.Append(' ');
                        n--;
                    }
                    if (n > 0)
                    {
                        Svuota();
                        var spazi = new XElement(Text + "s");
                        if (n > 1)
                            spazi.Add(new XAttribute(Text + "c", n));
                        Destinazione().Add(spazi);
                    }
                    dopoSpazio = true;
                    continue;
                }
                if (c == '\t')
                {
                    Svuota();
                    Destinazione().Add(new XElement(Text + "tab"));
                    dopoSpazio = false;
                    i++;
                    continue;
                }
                if (c is >= '' and <= '')
                {
                    Svuota();
                    if (Elemento(c) is { } elemento)
                    {
                        paragrafo.Add(elemento);
                        span = null; // il testo dopo va in un altro span
                        // Note e immagini si vedono; segnalibri e commenti no, e non separano gli spazi.
                        if (elemento.Name == Text + "note" || elemento.Name == Draw + "frame")
                            dopoSpazio = false;
                    }
                    i++;
                    continue;
                }
                if (XmlConvert.IsXmlChar(c))
                    testo.Append(c);
                dopoSpazio = false;
                i++;
            }
            Svuota();
        }

        /// <summary>L'elemento ODF di un segnaposto: segnalibro, nota, immagine o inizio/fine di commento.</summary>
        private XElement? Elemento(char c)
        {
            if (Segnalibri.Indice(c) is var s and >= 0 && s < contenuto.Segnalibri.Count)
            {
                var nome = contenuto.Segnalibri[s];
                var unico = nome;
                for (int n = 2; !_segnalibri.Add(unico); n++)
                    unico = $"{nome} {n}";
                return new XElement(Text + "bookmark", new XAttribute(Text + "name", Pulito(unico)));
            }
            if (Note.Indice(c) is var nota and >= 0 && nota < contenuto.Note.Count)
            {
                _note++;
                var corpo = new XElement(Text + "note-body",
                    contenuto.Note[nota].ReplaceLineEndings("\n").Split('\n').Select(r => new XElement(Text + "p",
                        new XAttribute(Text + "style-name", "Footnote"), Pulito(r))));
                return new XElement(Text + "note", new XAttribute(Text + "id", $"ftn{_note}"), new XAttribute(Text + "note-class", "footnote"),
                    new XElement(Text + "note-citation", _note), corpo);
            }
            if (Immagini.Indice(c) is var m and >= 0 && m < contenuto.Immagini.Count)
                return Cornice(contenuto.Immagini[m], m + 1);
            if (Commenti.IndiceInizio(c) is var ci and >= 0 && ci < contenuto.Commenti.Count && _commenti.TryGetValue(ci, out var stato))
            {
                var commento = contenuto.Commenti[ci];
                var annotazione = new XElement(Office + "annotation");
                if (stato.ConFine)
                    annotazione.Add(new XAttribute(Office + "name", $"__Annotation__{ci + 1}"));
                if (commento.Autore.Length > 0)
                    annotazione.Add(new XElement(Dc + "creator", Pulito(commento.Autore)));
                if (commento.Data is DateTime data)
                    annotazione.Add(new XElement(Dc + "date", data.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture)));
                annotazione.Add(commento.Testo.ReplaceLineEndings("\n").Split('\n').Select(r => new XElement(Text + "p", Pulito(r))));
                return annotazione;
            }
            if (Commenti.IndiceFine(c) is var cf and >= 0 && _commenti.TryGetValue(cf, out var fine) && fine.ConFine)
                return new XElement(Office + "annotation-end", new XAttribute(Office + "name", $"__Annotation__{cf + 1}"));
            return null;
        }

        private XElement Cornice(Immagine immagine, int numero)
        {
            var estensione = immagine.Formato == "jpeg" ? "jpg" : "png";
            var tipo = immagine.Formato == "jpeg" ? "image/jpeg" : "image/png";
            var percorso = $"Pictures/immagine{numero}.{estensione}";
            if (!Figure.Any(f => f.Percorso == percorso))
                Figure.Add((percorso, immagine.Dati, tipo));
            var cornice = new XElement(Draw + "frame", new XAttribute(Draw + "style-name", "fr1"), new XAttribute(Draw + "name", $"Immagine {numero}"),
                new XAttribute(Text + "anchor-type", "as-char"), new XAttribute(Svg + "width", Pt(immagine.Larghezza)),
                new XAttribute(Svg + "height", Pt(immagine.Altezza)), new XAttribute(Draw + "z-index", 0),
                new XElement(Draw + "image", new XAttribute(XLink + "href", percorso), new XAttribute(XLink + "type", "simple"),
                    new XAttribute(XLink + "show", "embed"), new XAttribute(XLink + "actuate", "onLoad"), new XAttribute(Draw + "mime-type", tipo)));
            if (immagine.Descrizione.Length > 0)
                cornice.Add(new XElement(Svg + "title", Pulito(immagine.Descrizione)), new XElement(Svg + "desc", Pulito(immagine.Descrizione)));
            return cornice;
        }

        // ---------- styles.xml ----------

        public XDocument Stili()
        {
            var stili = new XElement(Office + "styles",
                new XElement(Style + "default-style", new XAttribute(Style + "family", "paragraph"),
                    new XElement(Style + "paragraph-properties", new XAttribute(Style + "writing-mode", "lr-tb")),
                    new XElement(Style + "text-properties", new XAttribute(Style + "font-name", carattere), new XAttribute(Fo + "font-size", Pt(dimensioneBase)),
                        new XAttribute(Fo + "language", "it"), new XAttribute(Fo + "country", "IT"))),
                new XElement(Style + "style", new XAttribute(Style + "name", "Standard"), new XAttribute(Style + "family", "paragraph"),
                    new XAttribute(Style + "class", "text")));
            for (int livello = 1; livello <= 9; livello++)
                stili.Add(new XElement(Style + "style", new XAttribute(Style + "name", $"Heading_20_{livello}"),
                    new XAttribute(Style + "display-name", $"Heading {livello}"), new XAttribute(Style + "family", "paragraph"),
                    new XAttribute(Style + "parent-style-name", "Standard"), new XAttribute(Style + "next-style-name", "Standard"),
                    new XAttribute(Style + "default-outline-level", livello), new XAttribute(Style + "class", "text"),
                    new XElement(Style + "paragraph-properties", new XAttribute(Fo + "margin-top", "12pt"), new XAttribute(Fo + "margin-bottom", "6pt"),
                        new XAttribute(Fo + "keep-with-next", "always")),
                    new XElement(Style + "text-properties", new XAttribute(Fo + "font-size", Pt(ContenutoDocumento.DimensioneTitolo(livello))),
                        new XAttribute(Fo + "font-weight", "bold"))));
            foreach (var (nome, dimensione) in new[] { ("Footnote", dimensioneBase - 2), ("Header", dimensioneBase - 1), ("Footer", dimensioneBase - 1) })
                stili.Add(new XElement(Style + "style", new XAttribute(Style + "name", nome), new XAttribute(Style + "family", "paragraph"),
                    new XAttribute(Style + "parent-style-name", "Standard"), new XAttribute(Style + "class", "extra"),
                    new XElement(Style + "text-properties", new XAttribute(Fo + "font-size", Pt(dimensione)))));
            var schema = new XElement(Text + "outline-style", new XAttribute(Style + "name", "Outline"));
            for (int livello = 1; livello <= 10; livello++)
                schema.Add(new XElement(Text + "outline-level-style", new XAttribute(Text + "level", livello), new XAttribute(Style + "num-format", "")));
            stili.Add(schema);
            stili.Add(new XElement(Text + "notes-configuration", new XAttribute(Text + "note-class", "footnote"), new XAttribute(Style + "num-format", "1"),
                new XAttribute(Text + "start-value", 0), new XAttribute(Text + "footnotes-position", "page"),
                new XAttribute(Text + "start-numbering-at", "document")));

            var pagina = contenuto.Pagina;
            var (larghezza, altezza) = pagina.Effettive;
            bool conIntestazione = pagina.Intestazione.Length > 0, conPiede = pagina.PiePagina.Length > 0;

            var proprietaPagina = new XElement(Style + "page-layout-properties",
                new XAttribute(Fo + "page-width", Pt(larghezza)), new XAttribute(Fo + "page-height", Pt(altezza)),
                new XAttribute(Style + "print-orientation", pagina.Orizzontale ? "landscape" : "portrait"),
                new XAttribute(Fo + "margin-top", Pt(conIntestazione ? pagina.DistanzaIntestazione : pagina.MargineSopra)),
                new XAttribute(Fo + "margin-bottom", Pt(conPiede ? pagina.DistanzaPiede : pagina.MargineSotto)),
                new XAttribute(Fo + "margin-left", Pt(pagina.MargineSinistro)), new XAttribute(Fo + "margin-right", Pt(pagina.MargineDestro)));
            var layout = new XElement(Style + "page-layout", new XAttribute(Style + "name", "pm1"), proprietaPagina);
            if (conIntestazione)
                layout.Add(new XElement(Style + "header-style", new XElement(Style + "header-footer-properties", new XAttribute(Fo + "min-height", "0pt"),
                    new XAttribute(Fo + "margin-bottom", Pt(Math.Max(2, pagina.MargineSopra - pagina.DistanzaIntestazione - RigaZona))))));
            if (conPiede)
                layout.Add(new XElement(Style + "footer-style", new XElement(Style + "header-footer-properties", new XAttribute(Fo + "min-height", "0pt"),
                    new XAttribute(Fo + "margin-top", Pt(Math.Max(2, pagina.MargineSotto - pagina.DistanzaPiede - RigaZona))))));

            var maestra = new XElement(Style + "master-page", new XAttribute(Style + "name", "Standard"), new XAttribute(Style + "page-layout-name", "pm1"));
            if (conIntestazione)
            {
                maestra.Add(new XElement(Style + "header", ZonaConVariabili(pagina.Intestazione, "Header")));
                if (pagina.EscludiPrimaPagina)
                    maestra.Add(new XElement(Style + "header-first", new XElement(Text + "p", new XAttribute(Text + "style-name", "Header"))));
            }
            if (conPiede)
            {
                maestra.Add(new XElement(Style + "footer", ZonaConVariabili(pagina.PiePagina, "Footer")));
                if (pagina.EscludiPrimaPagina)
                    maestra.Add(new XElement(Style + "footer-first", new XElement(Text + "p", new XAttribute(Text + "style-name", "Footer"))));
            }

            return new XDocument(new XElement(Office + "document-styles", Spazi(), new XAttribute(Office + "version", "1.3"),
                Caratteri(), stili,
                new XElement(Office + "automatic-styles", layout),
                new XElement(Office + "master-styles", maestra)));
        }

        /// <summary>Un paragrafo di intestazione o piè di pagina, con le variabili come campi veri.</summary>
        private static XElement ZonaConVariabili(string modello, string stile)
        {
            var p = new XElement(Text + "p", new XAttribute(Text + "style-name", stile));
            foreach (var (testo, campo) in ImpostazioniPagina.Pezzi(modello))
            {
                p.Add(campo switch
                {
                    "PAGE" => new XElement(Text + "page-number", new XAttribute(Text + "select-page", "current"), "1"),
                    "NUMPAGES" => new XElement(Text + "page-count", "1"),
                    "TITLE" => new XElement(Text + "title"),
                    "DATE" => new XElement(Text + "date", new XAttribute(Text + "fixed", "false")),
                    _ => (object)Pulito(testo),
                });
            }
            return p;
        }
    }
}
