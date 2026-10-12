using System.Text;
using System.Text.RegularExpressions;

namespace Eleu.Documento;

/// <summary>
/// Legge l'RTF prodotto dal campo di testo (RichEdit) e lo converte in <see cref="ContenutoDocumento"/>.
/// Gestisce ciò che Eleu sa conservare: carattere (tipo, dimensione, stile, colori, posizione, spaziatura),
/// paragrafo (allineamento, rientri, spazi, interlinea), titoli (stili "heading N") ed elenchi.
/// Il resto viene ignorato.
/// </summary>
public static partial class RtfLettore
{
    /// <summary>Rientro aggiunto agli elenchi per far posto al pallino o al numero.</summary>
    public const float RientroElenco = 18f;

    // Destinazioni il cui contenuto non è testo del documento.
    private static readonly HashSet<string> DestinazioniDaSaltare =
    [
        "fonttbl", "colortbl", "stylesheet", "info", "pict", "object", "header", "footer",
        "headerl", "headerr", "footerl", "footerr", "pntext", "listtable", "listoverridetable",
        "themedata", "colorschememapping", "latentstyles", "datastore", "xmlnstbl", "rsidtbl",
        "generator", "fldinst", "footnote", "annotation",
    ];

    private sealed record StatoCarattere(Segmento Formato, bool Salta, int SaltoUnicode);

    public static ContenutoDocumento Leggi(string rtf)
    {
        var stiliTitolo = LeggiStiliTitolo(rtf);
        var caratteri = LeggiCaratteri(rtf);
        var colori = LeggiColori(rtf);

        var contenuto = new ContenutoDocumento();
        var paragrafo = new Paragrafo();
        var testo = new StringBuilder();
        var stato = new StatoCarattere(new Segmento(""), false, 1);
        var pila = new Stack<StatoCarattere>();
        var codifica = Encoding.Latin1;
        int caratteriDaSaltare = 0;
        bool inizioGruppo = false;
        var formatoTesto = stato.Formato;
        bool interlineaMultipla = false;
        int interlineaGrezza = 0;

        // Tabelle: la parola trowd apre una riga (e una tabella nuova se prima non c'era una riga), cellx dà il
        // bordo destro di ogni cella, intbl dice che il paragrafo è in tabella, cell chiude la cella, row la riga.
        int tabella = -1, riga = -1, colonna = 0;
        bool inTabella = false, dopoRiga = false, intestazione = false;
        var bordiCelle = new List<int>();

        PosizioneCella CellaCorrente()
        {
            int destra = colonna < bordiCelle.Count ? bordiCelle[colonna] : 0;
            int sinistra = colonna > 0 && colonna - 1 < bordiCelle.Count ? bordiCelle[colonna - 1] : 0;
            return new PosizioneCella(tabella, riga, colonna, Math.Max(0, destra - sinistra) / 20f, intestazione);
        }

        void Formato(Func<Segmento, Segmento> cambia) => stato = stato with { Formato = cambia(stato.Formato) };

        void ChiudiSegmento()
        {
            if (testo.Length == 0)
                return;
            paragrafo.Aggiungi(formatoTesto with { Testo = testo.ToString() });
            testo.Clear();
        }

        void AggiungiCarattere(char c)
        {
            if (stato.Salta)
                return;
            if (caratteriDaSaltare > 0)
            {
                caratteriDaSaltare--;
                return;
            }
            if (testo.Length > 0 && formatoTesto != stato.Formato)
                ChiudiSegmento();
            formatoTesto = stato.Formato;
            testo.Append(c);
        }

        void ChiudiParagrafo()
        {
            ChiudiSegmento();
            if (inTabella && tabella >= 0)
                paragrafo.Cella = CellaCorrente();
            else
                dopoRiga = false;
            // Le proprietà di paragrafo in RTF valgono fino al prossimo \pard: il paragrafo seguente le eredita
            // così come sono nel file, prima di togliere il rientro aggiunto per gli elenchi.
            var prossimo = paragrafo.CopiaProprieta();
            contenuto.Paragrafi.Add(Normalizza(paragrafo));
            paragrafo = prossimo;
        }

        int i = 0;
        while (i < rtf.Length)
        {
            char c = rtf[i];
            switch (c)
            {
                case '{':
                    pila.Push(stato);
                    inizioGruppo = true;
                    i++;
                    continue;
                case '}':
                    if (pila.Count > 0)
                        stato = pila.Pop();
                    inizioGruppo = false;
                    i++;
                    continue;
                case '\r' or '\n':
                    i++;
                    continue;
                case '\\':
                    break;
                default:
                    AggiungiCarattere(c);
                    inizioGruppo = false;
                    i++;
                    continue;
            }

            // Sequenza di controllo
            i++;
            if (i >= rtf.Length)
                break;
            char dopo = rtf[i];

            if (dopo is '\\' or '{' or '}')
            {
                AggiungiCarattere(dopo);
                i++;
                inizioGruppo = false;
                continue;
            }
            if (dopo == '\'')
            {
                if (i + 2 < rtf.Length && byte.TryParse(rtf.AsSpan(i + 1, 2), System.Globalization.NumberStyles.HexNumber, null, out var b))
                    AggiungiCarattere(codifica.GetString([b])[0]);
                i += 3;
                inizioGruppo = false;
                continue;
            }
            if (dopo == '*')
            {
                // {\* ...} = destinazione opzionale: si salta, ma si guarda se definisce un elenco.
                stato = stato with { Salta = true };
                if (inizioGruppo && rtf.AsSpan(i + 1).StartsWith(@"\pn") && !rtf.AsSpan(i + 1).StartsWith(@"\pntext"))
                {
                    int fine = rtf.IndexOf('}', i);
                    var definizione = fine > 0 ? rtf.AsSpan(i, fine - i) : [];
                    if (definizione.Contains(@"\pnlvlblt", StringComparison.Ordinal))
                        paragrafo.Elenco = true;
                    else if (definizione.Contains(@"\pnlvl", StringComparison.Ordinal))
                        paragrafo.Numerato = true;
                }
                i++;
                continue;
            }
            if (dopo is '\r' or '\n')
            {
                if (!stato.Salta)
                    ChiudiParagrafo();
                i++;
                continue;
            }
            if (dopo == '~')
            {
                AggiungiCarattere('\u00A0');
                i++;
                continue;
            }
            if (dopo is '-' or '_' or ':' or '|')
            {
                if (dopo == '_')
                    AggiungiCarattere('-');
                i++;
                continue;
            }

            // Parola di controllo con parametro numerico opzionale
            int inizio = i;
            while (i < rtf.Length && char.IsAsciiLetter(rtf[i]))
                i++;
            string parola = rtf[inizio..i];
            int? parametro = null;
            int inizioNumero = i;
            if (i < rtf.Length && (rtf[i] == '-' || char.IsAsciiDigit(rtf[i])))
            {
                i++;
                while (i < rtf.Length && char.IsAsciiDigit(rtf[i]))
                    i++;
                if (int.TryParse(rtf.AsSpan(inizioNumero, i - inizioNumero), out var n))
                    parametro = n;
            }
            if (i < rtf.Length && rtf[i] == ' ')
                i++;

            bool primaParolaDelGruppo = inizioGruppo;
            inizioGruppo = false;

            if (primaParolaDelGruppo && DestinazioniDaSaltare.Contains(parola))
            {
                stato = stato with { Salta = true };
                continue;
            }

            switch (parola)
            {
                case "par" or "line" when !stato.Salta:
                    ChiudiParagrafo();
                    break;
                case "page" when !stato.Salta:
                    // Salto pagina: il paragrafo seguente (o questo, se non ha ancora testo) inizia su una pagina nuova.
                    if (testo.Length > 0 || paragrafo.Segmenti.Count > 0)
                        ChiudiParagrafo();
                    paragrafo.SaltoPagina = true;
                    break;
                case "pagebb" when !stato.Salta:
                    paragrafo.SaltoPagina = true;
                    break;
                case "trowd":
                    // La riga avanza a ogni \row, non a ogni \trowd: Word ripete la definizione della riga
                    // anche dopo le celle, subito prima di \row.
                    if (!dopoRiga)
                    {
                        tabella++;
                        riga = 0;
                    }
                    colonna = 0;
                    bordiCelle.Clear();
                    intestazione = false;
                    dopoRiga = true;
                    break;
                case "cellx" when parametro is int bordo:
                    bordiCelle.Add(bordo);
                    break;
                case "trhdr":
                    intestazione = true;
                    break;
                case "intbl" when tabella >= 0:
                    inTabella = true;
                    break;
                case "cell" when !stato.Salta && inTabella:
                    ChiudiParagrafo();
                    colonna++;
                    break;
                case "row" when !stato.Salta:
                    inTabella = false;
                    riga++;
                    colonna = 0;
                    dopoRiga = true;
                    break;
                case "pard":
                    bool saltoAppenaLetto = paragrafo.SaltoPagina && paragrafo.Segmenti.Count == 0 && testo.Length == 0;
                    paragrafo.ReimpostaProprieta();
                    inTabella = false; // pard toglie anche intbl, che in tabella segue subito
                    paragrafo.SaltoPagina = saltoAppenaLetto; // un \page appena letto vale anche dopo un \pard
                    interlineaGrezza = 0;
                    interlineaMultipla = false;
                    break;
                case "plain":
                    Formato(_ => new Segmento(""));
                    break;
                case "b":
                    Formato(f => f with { Grassetto = parametro != 0 });
                    break;
                case "i":
                    Formato(f => f with { Corsivo = parametro != 0 });
                    break;
                case "ul" or "ulw" or "uld" or "uldb" or "ulth" or "uldash":
                    Formato(f => f with { Sottolineato = parametro != 0 });
                    break;
                case "ulnone":
                    Formato(f => f with { Sottolineato = false });
                    break;
                case "strike":
                    Formato(f => f with { Barrato = parametro != 0 });
                    break;
                case "super":
                    Formato(f => f with { Posizione = PosizioneTesto.Apice });
                    break;
                case "sub":
                    Formato(f => f with { Posizione = PosizioneTesto.Pedice });
                    break;
                case "nosupersub":
                    Formato(f => f with { Posizione = PosizioneTesto.Normale });
                    break;
                case "fs" when parametro is > 0:
                    Formato(f => f with { Dimensione = parametro.Value / 2f });
                    break;
                case "f" when parametro is int numeroCarattere && !stato.Salta:
                    Formato(f => f with { Carattere = caratteri.GetValueOrDefault(numeroCarattere) });
                    break;
                case "cf" when parametro is int indiceColore:
                    Formato(f => f with { Colore = Colore(colori, indiceColore) });
                    break;
                case "highlight" or "cb" or "chcbpat" when parametro is int indiceSfondo:
                    Formato(f => f with { Sfondo = Colore(colori, indiceSfondo) });
                    break;
                case "expndtw" when parametro is int spaziaturaTwip:
                    Formato(f => f with { Spaziatura = spaziaturaTwip / 20f });
                    break;
                case "expnd" when parametro is int spaziaturaQuarti:
                    Formato(f => f with { Spaziatura = spaziaturaQuarti / 4f });
                    break;
                case "s" when parametro is int numeroStile && !stato.Salta:
                    paragrafo.LivelloTitolo = stiliTitolo.GetValueOrDefault(numeroStile);
                    break;
                case "ql":
                    paragrafo.Allineamento = Allineamento.Sinistra;
                    break;
                case "qc":
                    paragrafo.Allineamento = Allineamento.Centro;
                    break;
                case "qr":
                    paragrafo.Allineamento = Allineamento.Destra;
                    break;
                case "qj":
                    paragrafo.Allineamento = Allineamento.Giustificato;
                    break;
                case "li" when parametro is int sinistro && !stato.Salta:
                    paragrafo.RientroSinistro = sinistro / 20f;
                    break;
                case "ri" when parametro is int destro && !stato.Salta:
                    paragrafo.RientroDestro = destro / 20f;
                    break;
                case "fi" when parametro is int primaRiga && !stato.Salta:
                    paragrafo.RientroPrimaRiga = primaRiga / 20f;
                    break;
                case "sb" when parametro is int prima && !stato.Salta:
                    paragrafo.SpazioPrima = prima / 20f;
                    break;
                case "sa" when parametro is int dopoParagrafo && !stato.Salta:
                    paragrafo.SpazioDopo = dopoParagrafo / 20f;
                    break;
                case "sl" when parametro is int interlinea && !stato.Salta:
                    interlineaGrezza = interlinea;
                    paragrafo.Interlinea = interlineaMultipla && interlinea > 0 ? interlinea / 240f : 1f;
                    break;
                case "slmult" when !stato.Salta:
                    interlineaMultipla = parametro == 1;
                    paragrafo.Interlinea = interlineaMultipla && interlineaGrezza > 0 ? interlineaGrezza / 240f : 1f;
                    break;
                case "ansicpg" when parametro is int pagina:
                    codifica = CodificaDaPaginaCodici(pagina);
                    break;
                case "uc" when parametro is int salto:
                    stato = stato with { SaltoUnicode = salto };
                    break;
                case "u" when parametro is int codice:
                    AggiungiCarattere((char)(codice < 0 ? codice + 65536 : codice));
                    caratteriDaSaltare = stato.Salta ? 0 : stato.SaltoUnicode;
                    break;
                case "tab":
                    AggiungiCarattere('\t');
                    break;
                case "emdash":
                    AggiungiCarattere('\u2014');
                    break;
                case "endash":
                    AggiungiCarattere('\u2013');
                    break;
                case "lquote":
                    AggiungiCarattere('\u2018');
                    break;
                case "rquote":
                    AggiungiCarattere('\u2019');
                    break;
                case "ldblquote":
                    AggiungiCarattere('\u201C');
                    break;
                case "rdblquote":
                    AggiungiCarattere('\u201D');
                    break;
                case "bullet":
                    AggiungiCarattere('\u2022');
                    break;
            }
        }

        ChiudiSegmento();
        if (paragrafo.Segmenti.Count > 0)
            contenuto.Paragrafi.Add(Normalizza(paragrafo));
        contenuto.Pagina = LeggiPagina(rtf);
        return contenuto;
    }

    /// <summary>Foglio, margini, intestazione, piè di pagina e numeri di pagina scritti nell'RTF.</summary>
    public static ImpostazioniPagina LeggiPagina(string rtf)
    {
        var predefinite = new ImpostazioniPagina();
        float Valore(string parola, float altrimenti)
        {
            var m = Regex.Match(rtf, $@"\\{parola}(-?\d+)");
            return m.Success ? int.Parse(m.Groups[1].Value) / 20f : altrimenti;
        }

        bool orizzontale = Regex.IsMatch(rtf, @"\\landscape(?![a-z])");
        float larghezza = Valore("paperw", orizzontale ? predefinite.Altezza : predefinite.Larghezza);
        float altezza = Valore("paperh", orizzontale ? predefinite.Larghezza : predefinite.Altezza);

        // I campi (PAGE, NUMPAGES, ...) tornano variabili come {pagina}, scritte con le graffe escapate.
        string ConVariabili(string gruppo) => Regex.Replace(gruppo, @"\{\\field\{\\\*\\fldinst\s*([^}]*)\}\{\\fldrslt[^}]*\}\}",
            m => ImpostazioniPagina.SegnapostoDelCampo(m.Groups[1].Value) is string s ? s.Replace("{", @"\{").Replace("}", @"\}") : "");
        string Testo(string nome) => string.Join(" ", TestoDelGruppo(ConVariabili(Gruppo(rtf, @"{\" + nome))));

        return new ImpostazioniPagina(
            orizzontale ? altezza : larghezza,
            orizzontale ? larghezza : altezza,
            orizzontale,
            Valore("margl", predefinite.MargineSinistro),
            Valore("margr", predefinite.MargineDestro),
            Valore("margt", predefinite.MargineSopra),
            Valore("margb", predefinite.MargineSotto),
            Valore("headery", predefinite.DistanzaIntestazione),
            Valore("footery", predefinite.DistanzaPiede),
            Testo("header"),
            Testo("footer"),
            Regex.IsMatch(rtf, @"\\titlepg(?![a-z])"));
    }

    /// <summary>Le righe di testo di un gruppo come {\header ...}, lette come un piccolo documento a sé.</summary>
    private static IEnumerable<string> TestoDelGruppo(string gruppo)
    {
        // "{\header\pard\qc Testo\par}": si toglie "{\header" (o "{\footer") e la graffa finale.
        if (gruppo.Length < @"{\header}".Length)
            return [];
        var interno = gruppo[@"{\header".Length..^1];
        return Leggi(@"{\rtf1 " + interno + "}").Paragrafi.Select(p => p.Testo.Trim()).Where(t => t.Length > 0);
    }

    /// <summary>Negli elenchi il rientro del pallino o del numero non è un rientro scelto dall'utente: lo si toglie.</summary>
    private static Paragrafo Normalizza(Paragrafo paragrafo)
    {
        if (paragrafo.Elenco || paragrafo.Numerato)
        {
            paragrafo.RientroSinistro = Math.Max(0, paragrafo.RientroSinistro - RientroElenco);
            paragrafo.RientroPrimaRiga = 0;
        }
        return paragrafo;
    }

    private static int? Colore(List<int?> colori, int indice) => indice > 0 && indice < colori.Count ? colori[indice] : null;

    /// <summary>Associa i numeri di stile (\sN) dello stylesheet ai livelli di titolo ("heading N").</summary>
    private static Dictionary<int, int> LeggiStiliTitolo(string rtf)
    {
        var mappa = new Dictionary<int, int>();
        foreach (Match m in VoceStile().Matches(rtf))
        {
            var nome = m.Groups["nome"].Value.Trim();
            var titolo = NomeTitolo().Match(nome);
            if (titolo.Success)
                mappa[int.Parse(m.Groups["numero"].Value)] = int.Parse(titolo.Groups[1].Value);
        }
        return mappa;
    }

    /// <summary>Tabella dei caratteri: \fN → nome del tipo di carattere.</summary>
    private static Dictionary<int, string> LeggiCaratteri(string rtf)
    {
        var mappa = new Dictionary<int, string>();
        foreach (Match m in VoceCarattere().Matches(Gruppo(rtf, @"{\fonttbl")))
            mappa.TryAdd(int.Parse(m.Groups["numero"].Value), m.Groups["nome"].Value.Trim());
        return mappa;
    }

    /// <summary>Il gruppo RTF che inizia con <paramref name="apertura"/>, fino alla graffa che lo chiude.</summary>
    private static string Gruppo(string rtf, string apertura)
    {
        // Il nome deve finire lì: "{\header" non deve trovare "{\headerf".
        var trovato = Regex.Match(rtf, Regex.Escape(apertura) + "(?![a-z])");
        if (!trovato.Success)
            return "";
        int inizio = trovato.Index;
        int profondita = 0;
        for (int i = inizio; i < rtf.Length; i++)
        {
            if (rtf[i] == '\\')
            {
                i++; // salta il carattere escapato, comprese le graffe \{ \}
                continue;
            }
            if (rtf[i] == '{')
                profondita++;
            else if (rtf[i] == '}' && --profondita == 0)
                return rtf[inizio..(i + 1)];
        }
        return rtf[inizio..];
    }

    /// <summary>Tabella dei colori: l'indice 0 è "automatico" (null), gli altri 0xRRGGBB.</summary>
    private static List<int?> LeggiColori(string rtf)
    {
        var colori = new List<int?>();
        var gruppo = Gruppo(rtf, @"{\colortbl");
        if (gruppo.Length == 0)
            return colori;
        var tabella = gruppo[@"{\colortbl".Length..^1];
        foreach (var voce in tabella.Split(';')[..^1])
        {
            int Componente(string nome)
            {
                var m = Regex.Match(voce, $@"\\{nome}(\d+)");
                return m.Success ? int.Parse(m.Groups[1].Value) & 0xFF : 0;
            }
            colori.Add(voce.Contains(@"\red") || voce.Contains(@"\green") || voce.Contains(@"\blue")
                ? (Componente("red") << 16) | (Componente("green") << 8) | Componente("blue")
                : null);
        }
        return colori;
    }

    private static Encoding CodificaDaPaginaCodici(int pagina)
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(pagina);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException)
        {
            return Encoding.Latin1;
        }
    }

    [GeneratedRegex(@"\{\\s(?<numero>\d+)(?:\\[a-z]+-?\d*\s?)*(?<nome>[^\\{};]+);\}")]
    private static partial Regex VoceStile();

    [GeneratedRegex(@"\{\\f(?<numero>\d+)(?:\\[a-z]+-?\d*\s?)*(?:\{[^{}]*\}\s*)*(?<nome>[^\\{};]+);\}")]
    private static partial Regex VoceCarattere();

    [GeneratedRegex(@"^heading\s*(\d)$", RegexOptions.IgnoreCase)]
    private static partial Regex NomeTitolo();
}
