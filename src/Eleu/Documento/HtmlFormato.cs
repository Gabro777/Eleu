using System.Globalization;
using System.Net;
using System.Text;
using HtmlAgilityPack;

namespace Eleu.Documento;

/// <summary>
/// HTML in lettura e scrittura. In scrittura esce una pagina accessibile: titoli veri (h1-h6), elenchi, tabelle
/// con la prima riga di intestazione, immagini con il testo alternativo, note collegate al richiamo e ritorno,
/// lingua italiana. In lettura si prende ciò che Eleu conserva e il resto diventa testo.
/// </summary>
public static class HtmlFormato
{
    /// <summary>Come scrivere le immagini: null = dentro la pagina (data:), altrimenti il percorso del file dell'immagine n (da 0).</summary>
    /// <param name="fileNote">Se c'è, le note stanno in quel file (EPUB): i richiami portano lì e qui non si scrivono.</param>
    /// <param name="primoParagrafo">Il numero del primo paragrafo nel documento intero, per gli id dei titoli ("t12").</param>
    public static string Scrivi(ContenutoDocumento contenuto, bool xhtml = false, Func<int, string>? fileImmagine = null,
        string? fileNote = null, int primoParagrafo = 0)
    {
        var h = new StringBuilder();
        string Chiudi(string tag) => xhtml ? $"<{tag} />" : $"<{tag}>";
        var titolo = contenuto.Metadati.Titolo.Length > 0 ? contenuto.Metadati.Titolo
            : contenuto.Paragrafi.FirstOrDefault(p => p.LivelloTitolo > 0)?.Testo.Trim() ?? "Documento";

        if (xhtml)
        {
            h.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
            h.AppendLine("<!DOCTYPE html>");
            h.AppendLine("<html xmlns=\"http://www.w3.org/1999/xhtml\" xmlns:epub=\"http://www.idpf.org/2007/ops\" lang=\"it\" xml:lang=\"it\">");
        }
        else
        {
            h.AppendLine("<!DOCTYPE html>");
            h.AppendLine("<html lang=\"it\">");
        }
        h.AppendLine("<head>");
        h.AppendLine(xhtml ? "<meta charset=\"utf-8\" />" : "<meta charset=\"utf-8\">");
        h.Append("<title>").Append(Testo(titolo)).AppendLine("</title>");
        if (contenuto.Metadati.Autore.Length > 0)
            h.Append("<meta name=\"author\" content=\"").Append(Attributo(contenuto.Metadati.Autore)).AppendLine(xhtml ? "\" />" : "\">");
        h.AppendLine("<style>body{font-family:'Segoe UI',sans-serif;line-height:1.5;max-width:45em;margin:auto;padding:1em}table{border-collapse:collapse}td,th{border:1px solid;padding:.3em .6em;vertical-align:top}</style>");
        h.AppendLine("</head>");
        h.AppendLine("<body>");

        var paragrafi = contenuto.Paragrafi;
        int numeroNota = 0;
        for (int i = 0; i < paragrafi.Count; i++)
        {
            var p = paragrafi[i];
            if (p.Cella is { } cella)
            {
                int fine = i;
                while (fine < paragrafi.Count && paragrafi[fine].Cella?.Tabella == cella.Tabella)
                    fine++;
                ScriviTabella(paragrafi.Skip(i).Take(fine - i).ToList());
                i = fine - 1;
                continue;
            }
            if (p.Elenco || p.Numerato)
            {
                var tag = p.Numerato ? "ol" : "ul";
                h.Append('<').Append(tag).AppendLine(">");
                while (i < paragrafi.Count && paragrafi[i].Cella is null && (p.Numerato ? paragrafi[i].Numerato : paragrafi[i].Elenco))
                {
                    h.Append("<li").Append(Stile(paragrafi[i])).Append('>');
                    Contenuto(paragrafi[i]);
                    h.AppendLine("</li>");
                    i++;
                }
                i--;
                h.Append("</").Append(tag).AppendLine(">");
                continue;
            }
            ScriviParagrafo(p, i);
        }

        if (contenuto.Note.Count > 0 && fileNote is null)
        {
            h.AppendLine(xhtml ? "<section epub:type=\"endnotes\" role=\"doc-endnotes\">" : "<section role=\"doc-endnotes\">");
            h.AppendLine("<h2>Note</h2>");
            h.AppendLine("<ol>");
            for (int n = 0; n < contenuto.Note.Count; n++)
            {
                h.Append($"<li id=\"nota-{n + 1}\"").Append(xhtml ? " epub:type=\"endnote\"" : "").Append('>');
                h.Append(Testo(contenuto.Note[n]).Replace("\n", Chiudi("br")));
                h.Append($" <a href=\"#rif-{n + 1}\" role=\"doc-backlink\" aria-label=\"Torna al testo\">↩</a></li>").AppendLine();
            }
            h.AppendLine("</ol>");
            h.AppendLine("</section>");
        }
        h.AppendLine("</body>");
        h.AppendLine("</html>");
        return h.ToString();

        void ScriviParagrafo(Paragrafo p, int indice)
        {
            var tag = p.LivelloTitolo > 0 ? $"h{Math.Min(6, p.LivelloTitolo)}" : "p";
            h.Append('<').Append(tag);
            if (p.LivelloTitolo > 0)
                h.Append($" id=\"t{primoParagrafo + indice}\"");
            h.Append(Stile(p)).Append('>');
            Contenuto(p);
            if (p.Segmenti.Count == 0 || p.Testo.Trim().Length == 0 && !p.Testo.Any(EmbeddedMarker))
                h.Append(Chiudi("br"));
            h.Append("</").Append(tag).AppendLine(">");
        }

        void ScriviTabella(List<Paragrafo> celle)
        {
            var griglia = Griglia.Da(celle, celle[0].Cella!.Tabella);
            h.AppendLine("<table>");
            for (int r = 0; r < griglia.Righe.Count; r++)
            {
                h.AppendLine("<tr>");
                foreach (var cella in griglia.Righe[r])
                {
                    var tag = r == 0 ? "th" : "td";
                    h.Append('<').Append(tag).Append(r == 0 ? " scope=\"col\"" : "").Append('>');
                    for (int k = 0; k < cella.Count; k++)
                    {
                        if (cella.Count > 1)
                            h.Append("<p>");
                        Contenuto(cella[k]);
                        if (cella.Count > 1)
                            h.Append("</p>");
                    }
                    h.Append("</").Append(tag).AppendLine(">");
                }
                h.AppendLine("</tr>");
            }
            h.AppendLine("</table>");
        }

        void Contenuto(Paragrafo p)
        {
            foreach (var s in p.Segmenti)
            {
                var aperti = new List<string>();
                void Apri(string tag, string attributi = "")
                {
                    h.Append('<').Append(tag).Append(attributi).Append('>');
                    aperti.Add(tag);
                }
                if (s.Grassetto && p.LivelloTitolo == 0)
                    Apri("strong");
                if (s.Corsivo)
                    Apri("em");
                if (s.Sottolineato)
                    Apri("u");
                if (s.Barrato)
                    Apri("s");
                if (s.Posizione == PosizioneTesto.Apice)
                    Apri("sup");
                else if (s.Posizione == PosizioneTesto.Pedice)
                    Apri("sub");
                var stile = new List<string>();
                if (s.Colore is int colore)
                    stile.Add($"color:#{colore:X6}");
                if (s.Sfondo is int sfondo)
                    stile.Add($"background-color:#{sfondo:X6}");
                if (s.Dimensione is float dimensione)
                    stile.Add(string.Create(CultureInfo.InvariantCulture, $"font-size:{dimensione}pt"));
                if (s.Carattere is string carattere)
                    stile.Add($"font-family:'{carattere.Replace("'", "")}'");
                if (stile.Count > 0)
                    Apri("span", $" style=\"{Attributo(string.Join(";", stile))}\"");

                foreach (var c in s.Testo)
                {
                    if (Segnalibri.Indice(c) is var segnalibro and >= 0 && segnalibro < contenuto.Segnalibri.Count)
                        h.Append($"<a id=\"{Attributo(Ancora(contenuto.Segnalibri[segnalibro]))}\"></a>");
                    else if (Note.Indice(c) is var nota and >= 0 && nota < contenuto.Note.Count)
                    {
                        numeroNota = nota + 1;
                        h.Append($"<a href=\"{fileNote}#nota-{numeroNota}\" id=\"rif-{numeroNota}\" role=\"doc-noteref\"")
                            .Append(xhtml ? " epub:type=\"noteref\"" : "").Append($"><sup>{numeroNota}</sup></a>");
                    }
                    else if (Immagini.Indice(c) is var immagine and >= 0 && immagine < contenuto.Immagini.Count)
                    {
                        var img = contenuto.Immagini[immagine];
                        var src = fileImmagine?.Invoke(immagine) ?? $"data:image/{img.Formato};base64,{Convert.ToBase64String(img.Dati)}";
                        h.Append($"<img src=\"{Attributo(src)}\" alt=\"{Attributo(img.Descrizione)}\"")
                            .Append(CultureInfo.InvariantCulture, $" width=\"{Math.Round(img.Larghezza * 96 / 72)}\" height=\"{Math.Round(img.Altezza * 96 / 72)}\"")
                            .Append(xhtml ? " />" : ">");
                    }
                    else if (c == '\t')
                        h.Append(' ');
                    else if (c == Editor_SaltoPagina)
                        continue;
                    else
                        h.Append(Testo(c.ToString()));
                }
                for (int k = aperti.Count - 1; k >= 0; k--)
                    h.Append("</").Append(aperti[k]).Append('>');
            }
        }
    }

    private const char Editor_SaltoPagina = '\f';

    private static bool EmbeddedMarker(char c) => c >= 0xE000 && c <= 0xF8FF;

    private static string Stile(Paragrafo p)
    {
        var stile = new List<string>();
        if (p.Allineamento != Allineamento.Sinistra)
            stile.Add("text-align:" + p.Allineamento switch { Allineamento.Centro => "center", Allineamento.Destra => "right", _ => "justify" });
        if (p.RientroSinistro != 0 && !p.Elenco && !p.Numerato)
            stile.Add(string.Create(CultureInfo.InvariantCulture, $"margin-left:{p.RientroSinistro}pt"));
        if (p.RientroPrimaRiga != 0 && !p.Elenco && !p.Numerato)
            stile.Add(string.Create(CultureInfo.InvariantCulture, $"text-indent:{p.RientroPrimaRiga}pt"));
        if (p.SaltoPagina)
            stile.Add("page-break-before:always");
        return stile.Count == 0 ? "" : $" style=\"{Attributo(string.Join(";", stile))}\"";
    }

    /// <summary>Un nome di segnalibro come identificatore HTML (senza spazi).</summary>
    public static string Ancora(string nome) => string.Concat(nome.Trim().Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_'));

    private static string Testo(string testo) => WebUtility.HtmlEncode(testo).Replace("&#39;", "'");

    private static string Attributo(string testo) => WebUtility.HtmlEncode(testo);

    // ---------- Lettura ----------

    /// <summary>
    /// Il documento da un HTML (anche XHTML, per esempio i capitoli di un EPUB). <paramref name="risorsa"/> dà i
    /// dati di un'immagine dal suo indirizzo, se non è dentro la pagina.
    /// </summary>
    public static ContenutoDocumento Leggi(string html, Func<string, byte[]?>? risorsa = null, ContenutoDocumento? aggiungiA = null)
    {
        var documento = new HtmlAgilityPack.HtmlDocument { OptionFixNestedTags = true };
        documento.LoadHtml(html);
        var contenuto = aggiungiA ?? new ContenutoDocumento();
        var radice = documento.DocumentNode.SelectSingleNode("//body") ?? documento.DocumentNode;
        if (aggiungiA is null)
        {
            var titolo = WebUtility.HtmlDecode(documento.DocumentNode.SelectSingleNode("//title")?.InnerText ?? "").Trim();
            var autore = documento.DocumentNode.SelectSingleNode("//meta[@name='author']")?.GetAttributeValue("content", "") ?? "";
            contenuto.Metadati = new Metadati(titolo, WebUtility.HtmlDecode(autore).Trim());
        }
        var lettore = new LettoreHtml(contenuto, risorsa);
        lettore.PreparaNote(documento.DocumentNode);
        lettore.Blocco(radice);
        lettore.Fine();
        return contenuto;
    }

    private sealed class LettoreHtml(ContenutoDocumento contenuto, Func<string, byte[]?>? risorsa)
    {
        private Paragrafo? _corrente;
        private int _tabella = contenuto.Paragrafi.Select(p => p.Cella?.Tabella ?? -1).DefaultIfEmpty(-1).Max() + 1;
        private PosizioneCella? _cella;
        private bool _saltoPagina;

        private static readonly HashSet<string> Blocchi = ["p", "div", "h1", "h2", "h3", "h4", "h5", "h6", "li", "ul", "ol", "table",
            "blockquote", "pre", "section", "article", "header", "footer", "main", "nav", "aside", "figure", "figcaption", "dl", "dt", "dd",
            "address", "hr", "tr", "td", "th", "tbody", "thead", "tfoot", "caption", "body", "html"];

        private Paragrafo Corrente()
        {
            if (_corrente is null)
            {
                _corrente = new Paragrafo { Cella = _cella, SaltoPagina = _saltoPagina };
                _saltoPagina = false;
                contenuto.Paragrafi.Add(_corrente);
            }
            return _corrente;
        }

        public void Fine() => FineParagrafo();

        private void FineParagrafo()
        {
            _corrente?.TogliSpaziFinali();
            _corrente = null;
        }

        /// <summary>Le note della pagina: id del testo della nota → testo (senza il collegamento di ritorno).</summary>
        private Dictionary<string, string>? _testiNote;

        /// <summary>Gli elementi che contengono le note (la sezione in fondo): non si leggono come testo.</summary>
        private readonly HashSet<HtmlNode> _sezioniNote = [];

        public void PreparaNote(HtmlNode radice)
        {
            _testiNote = [];
            foreach (var richiamo in radice.Descendants("a").Where(Richiamo))
            {
                var id = richiamo.GetAttributeValue("href", "").Split('#') is [.., var frammento] ? frammento : "";
                if (id.Length == 0 || radice.SelectSingleNode($"//*[@id='{id.Replace("'", "")}']") is not { } nota)
                    continue;
                var copia = nota.CloneNode(true);
                foreach (var ritorno in copia.Descendants("a").Where(a => a.GetAttributeValue("role", "") == "doc-backlink" || a.InnerText.Trim() is "↩" or "↑").ToList())
                    ritorno.Remove();
                _testiNote[id] = SpaziHtml(WebUtility.HtmlDecode(copia.InnerText)).Trim();
                var sezione = nota.Ancestors().FirstOrDefault(a => a.GetAttributeValue("role", "") is "doc-endnotes" or "doc-footnotes"
                    || a.GetAttributeValue("epub:type", "").Contains("notes")) ?? nota;
                _sezioniNote.Add(sezione);
            }
        }

        private static bool Richiamo(HtmlNode a) =>
            a.GetAttributeValue("role", "") == "doc-noteref" || a.GetAttributeValue("epub:type", "") == "noteref";

        public void Blocco(HtmlNode nodo, Action<Paragrafo>? prepara = null)
        {
            foreach (var figlio in nodo.ChildNodes)
            {
                if (figlio.NodeType == HtmlNodeType.Comment || figlio.Name is "script" or "style" or "head" or "title" or "noscript"
                    || _sezioniNote.Contains(figlio))
                    continue;
                if (figlio.NodeType == HtmlNodeType.Text || !Blocchi.Contains(figlio.Name))
                {
                    Inline(figlio, new Segmento(""), prepara);
                    continue;
                }
                bool salto = (figlio.GetAttributeValue("style", "") + figlio.GetAttributeValue("class", "")).Contains("page-break-before", StringComparison.OrdinalIgnoreCase);
                switch (figlio.Name)
                {
                    case "h1" or "h2" or "h3" or "h4" or "h5" or "h6":
                        FineParagrafo();
                        _saltoPagina |= salto;
                        int livello = figlio.Name[1] - '0';
                        Blocco(figlio, p => p.LivelloTitolo = livello);
                        FineParagrafo();
                        break;
                    case "ul" or "ol":
                        FineParagrafo();
                        bool numerato = figlio.Name == "ol";
                        foreach (var voce in figlio.ChildNodes.Where(n => n.Name == "li"))
                        {
                            Blocco(voce, p =>
                            {
                                p.Elenco = !numerato;
                                p.Numerato = numerato;
                            });
                            FineParagrafo();
                        }
                        break;
                    case "table":
                        FineParagrafo();
                        Tabella(figlio);
                        break;
                    case "hr":
                        FineParagrafo();
                        break;
                    case "pre":
                        FineParagrafo();
                        foreach (var riga in WebUtility.HtmlDecode(figlio.InnerText).ReplaceLineEndings("\n").Split('\n'))
                        {
                            Corrente().Aggiungi(new Segmento(riga, Carattere: "Consolas"));
                            FineParagrafo();
                        }
                        break;
                    case "blockquote":
                        FineParagrafo();
                        Blocco(figlio, p => p.RientroSinistro = 36);
                        FineParagrafo();
                        break;
                    default:
                        FineParagrafo();
                        _saltoPagina |= salto;
                        Blocco(figlio, prepara + (p => p.Allineamento = Allineamento(figlio, p.Allineamento)));
                        FineParagrafo();
                        break;
                }
            }
        }

        private static Allineamento Allineamento(HtmlNode nodo, Allineamento attuale)
        {
            var stile = (nodo.GetAttributeValue("style", "") + ";" + nodo.GetAttributeValue("align", "")).ToLowerInvariant();
            return stile.Contains("center") ? Documento.Allineamento.Centro
                : stile.Contains("right") ? Documento.Allineamento.Destra
                : stile.Contains("justify") ? Documento.Allineamento.Giustificato
                : attuale;
        }

        private void Tabella(HtmlNode tabella)
        {
            int numero = _tabella++;
            var righe = tabella.Descendants("tr").Where(r => r.Ancestors("table").First() == tabella).ToList();
            for (int r = 0; r < righe.Count; r++)
            {
                var celle = righe[r].ChildNodes.Where(n => n.Name is "td" or "th").ToList();
                for (int c = 0; c < celle.Count; c++)
                {
                    _cella = new PosizioneCella(numero, r, c);
                    FineParagrafo();
                    Blocco(celle[c]);
                    if (!contenuto.Paragrafi.Any(p => p.Cella == _cella))
                        Corrente(); // cella vuota: un paragrafo vuoto
                    FineParagrafo();
                }
            }
            _cella = null;
        }

        private void Inline(HtmlNode nodo, Segmento formato, Action<Paragrafo>? prepara)
        {
            if (nodo.NodeType == HtmlNodeType.Text)
            {
                var testo = SpaziHtml(WebUtility.HtmlDecode(nodo.InnerText));
                if (testo.Length == 0 || testo == " " && _corrente is null)
                    return;
                var p = Corrente();
                prepara?.Invoke(p);
                if (p.Segmenti.Count == 0)
                    testo = testo.TrimStart();
                p.Aggiungi(formato with { Testo = testo });
                return;
            }
            switch (nodo.Name)
            {
                case "br":
                    FineParagrafo();
                    return;
                case "img":
                    var src = nodo.GetAttributeValue("src", "");
                    var dati = DatiImmagine(src);
                    if (dati is null || contenuto.Immagini.Count >= Immagini.Massimo)
                        return;
                    try
                    {
                        var immagine = Immagini.DaDati(dati, WebUtility.HtmlDecode(nodo.GetAttributeValue("alt", "")).Trim());
                        if (int.TryParse(nodo.GetAttributeValue("width", ""), out int larghezza) && larghezza > 0)
                            immagine = Immagini.ConLarghezza(immagine, larghezza * 72f / 96f);
                        contenuto.Immagini.Add(immagine);
                        var p = Corrente();
                        prepara?.Invoke(p);
                        p.Aggiungi(new Segmento(Immagini.Segnaposto(contenuto.Immagini.Count - 1).ToString()));
                    }
                    catch (Exception e) when (e is ArgumentException or System.Runtime.InteropServices.ExternalException or OutOfMemoryException)
                    {
                    }
                    return;
                case "a" when Richiamo(nodo) && _testiNote is not null
                    && _testiNote.TryGetValue(nodo.GetAttributeValue("href", "").Split('#')[^1], out var testoNota)
                    && contenuto.Note.Count < Note.Massimo:
                    contenuto.Note.Add(testoNota);
                    var conNota = Corrente();
                    prepara?.Invoke(conNota);
                    conNota.Aggiungi(new Segmento(Note.Segnaposto(contenuto.Note.Count - 1).ToString()));
                    return;
                case "a" when nodo.ChildNodes.Count == 0 && (nodo.GetAttributeValue("id", "") is { Length: > 0 } || nodo.GetAttributeValue("name", "") is { Length: > 0 }):
                    var nome = nodo.GetAttributeValue("id", "") is { Length: > 0 } id ? id : nodo.GetAttributeValue("name", "");
                    if (contenuto.Segnalibri.Count < Segnalibri.Massimo)
                    {
                        contenuto.Segnalibri.Add(nome.Replace('_', ' '));
                        Corrente().Aggiungi(new Segmento(Segnalibri.Segnaposto(contenuto.Segnalibri.Count - 1).ToString()));
                    }
                    return;
            }
            var nuovo = nodo.Name switch
            {
                "b" or "strong" => formato with { Grassetto = true },
                "i" or "em" or "cite" or "dfn" => formato with { Corsivo = true },
                "u" or "ins" => formato with { Sottolineato = true },
                "s" or "del" or "strike" => formato with { Barrato = true },
                "sup" => formato with { Posizione = PosizioneTesto.Apice },
                "sub" => formato with { Posizione = PosizioneTesto.Pedice },
                "code" or "kbd" or "samp" => formato with { Carattere = "Consolas" },
                _ => formato,
            };
            foreach (var figlio in nodo.ChildNodes)
            {
                if (figlio.NodeType != HtmlNodeType.Text && Blocchi.Contains(figlio.Name))
                {
                    // Un blocco dentro un elemento in linea (HTML scritto male): lo si tratta come blocco.
                    FineParagrafo();
                    Blocco(figlio.ParentNode == nodo ? WrapperDi(figlio) : figlio);
                    continue;
                }
                Inline(figlio, nuovo, prepara);
            }
        }

        private static HtmlNode WrapperDi(HtmlNode nodo)
        {
            var contenitore = HtmlNode.CreateNode("<div></div>");
            contenitore.AppendChild(nodo.CloneNode(true));
            return contenitore;
        }

        private byte[]? DatiImmagine(string src)
        {
            if (src.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                int virgola = src.IndexOf(',');
                if (virgola < 0 || !src[..virgola].Contains(";base64", StringComparison.OrdinalIgnoreCase))
                    return null;
                try
                {
                    return Convert.FromBase64String(src[(virgola + 1)..]);
                }
                catch (FormatException)
                {
                    return null;
                }
            }
            return risorsa?.Invoke(WebUtility.HtmlDecode(src));
        }

        /// <summary>Come fa il browser: a capo e spazi ripetuti diventano un solo spazio.</summary>
        private static string SpaziHtml(string testo)
        {
            var risultato = new StringBuilder(testo.Length);
            bool spazio = false;
            foreach (var c in testo)
            {
                if (c is ' ' or '\n' or '\r' or '\t')
                {
                    if (!spazio)
                        risultato.Append(' ');
                    spazio = true;
                }
                else
                {
                    risultato.Append(c == ' ' ? ' ' : c);
                    spazio = false;
                }
            }
            return risultato.ToString();
        }
    }
}
