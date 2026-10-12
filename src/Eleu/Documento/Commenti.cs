using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Eleu.Documento;

/// <summary>Un commento: chi l'ha scritto, quando e cosa dice. Il testo commentato è nel documento.</summary>
public sealed record Commento(string Testo, string Autore = "", DateTime? Data = null)
{
    /// <summary>Le iniziali dell'autore, come le vuole Word (al massimo 9 lettere).</summary>
    public string Iniziali
    {
        get
        {
            var iniziali = string.Concat(Autore.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(p => char.ToUpperInvariant(p[0])));
            return iniziali.Length > 9 ? iniziali[..9] : iniziali;
        }
    }
}

/// <summary>
/// Commenti. Nei file sono intervalli di testo con una nota a margine: w:commentRangeStart/End (DOCX),
/// office:annotation (ODT), {\*\atrfstart}/{\*\annotation} (RTF). Il campo di testo non li conosce: durante
/// caricamento e salvataggio l'inizio e la fine di ogni commento sono due caratteri segnaposto (area Unicode a uso
/// privato, dopo le immagini); i dati dei commenti stanno in un elenco a parte. Nel documento aperto ogni commento
/// è un intervallo del testo che si sposta da solo mentre si scrive.
/// </summary>
public static partial class Commenti
{
    // Inizi da U+F600, fine da U+F780, fino a U+F8FF: dopo le immagini (da U+F200 a U+F5FF).
    private const int PrimoInizio = 0xF600, PrimaFine = 0xF780;
    public const int Massimo = 384;

    public static char Inizio(int indice) => (char)(PrimoInizio + indice);

    public static char Fine(int indice) => (char)(PrimaFine + indice);

    /// <summary>Di quale commento il carattere segna l'inizio, o -1.</summary>
    public static int IndiceInizio(char c) => c >= PrimoInizio && c < PrimoInizio + Massimo ? c - PrimoInizio : -1;

    /// <summary>Di quale commento il carattere segna la fine, o -1.</summary>
    public static int IndiceFine(char c) => c >= PrimaFine && c < PrimaFine + Massimo ? c - PrimaFine : -1;

    public static bool Segnaposto(char c) => IndiceInizio(c) >= 0 || IndiceFine(c) >= 0;

    /// <summary>Come si legge un commento: "Commento di Anna: testo".</summary>
    public static string Descrivi(Commento commento) =>
        $"Commento{(commento.Autore.Length > 0 ? $" di {commento.Autore}" : "")}: {commento.Testo.ReplaceLineEndings(" ")}";

    // ---------- RTF ----------

    /// <summary>RTF di un file → stesso RTF con i segnaposto al posto dei commenti; i dati vanno in <paramref name="commenti"/>.</summary>
    public static string InSegnaposto(string rtf, List<Commento> commenti)
    {
        if (!rtf.Contains(@"\annotation", StringComparison.Ordinal))
            return Pulisci(rtf);

        var autori = new Queue<string>(Autore().Matches(rtf).Select(m => TestoRtf(m.Groups[1].Value).Trim()));
        var conIntervallo = new HashSet<string>(InizioIntervallo().Matches(rtf).Select(m => m.Groups[1].Value.Trim()));
        var indici = new Dictionary<string, int>();

        // Ogni {\*\annotation ...} diventa un commento; dove non c'è l'intervallo, inizio e fine stanno lì.
        var risultato = new StringBuilder();
        int da = 0;
        foreach (Match m in InizioAnnotazione().Matches(rtf))
        {
            if (m.Index < da)
                continue;
            int fine = FineGruppo(rtf, m.Index);
            if (fine < 0)
                break;
            risultato.Append(rtf, da, m.Index - da);
            da = fine + 1;
            var interno = rtf[(m.Index + m.Length)..fine];
            var autore = autori.Count > 0 ? autori.Dequeue() : "";
            if (commenti.Count >= Massimo)
                continue;
            var riferimento = Riferimento().Match(interno) is { Success: true } r ? r.Groups[1].Value.Trim() : "";
            DateTime? data = DataRtf().Match(interno) is { Success: true } d && long.TryParse(d.Groups[1].Value, out var dttm) ? DaDttm(dttm) : null;
            commenti.Add(new Commento(TestoDi(interno), autore, data));
            int indice = commenti.Count - 1;
            if (riferimento.Length > 0 && conIntervallo.Contains(riferimento) && !indici.ContainsKey(riferimento))
                indici[riferimento] = indice;
            else
                risultato.Append('{').Append(CarattereRtf(Inizio(indice))).Append(CarattereRtf(Fine(indice))).Append('}');
        }
        risultato.Append(rtf, da, rtf.Length - da);

        var testo = InizioIntervallo().Replace(risultato.ToString(), m =>
            indici.TryGetValue(m.Groups[1].Value.Trim(), out int i) ? "{" + CarattereRtf(Inizio(i)) + "}" : "");
        testo = FineIntervallo().Replace(testo, m =>
            indici.TryGetValue(m.Groups[1].Value.Trim(), out int i) ? "{" + CarattereRtf(Fine(i)) + "}" : "");
        return Pulisci(testo);
    }

    /// <summary>Toglie i pezzi dei commenti che restano (autori, iniziali, segni di richiamo).</summary>
    private static string Pulisci(string rtf) =>
        Richiamo().Replace(Iniziali().Replace(Autore().Replace(rtf, ""), ""), "");

    /// <summary>RTF del campo di testo con i segnaposto → RTF con i commenti veri, come li scrive Word.</summary>
    public static string DaSegnaposto(string rtf, IReadOnlyList<Commento> commenti) =>
        UnicodeRtf().Replace(rtf, m =>
        {
            var c = (char)(ushort)short.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            if (IndiceInizio(c) is var inizio and >= 0)
                return inizio < commenti.Count ? $@"{{\*\atrfstart {inizio + 1}}}" : "";
            if (IndiceFine(c) is not (var fine and >= 0))
                return m.Value;
            if (fine >= commenti.Count)
                return "";
            var commento = commenti[fine];
            var rtfCommento = new StringBuilder();
            rtfCommento.Append($@"{{\*\atrfend {fine + 1}}}{{\*\atnid ").Append(Escapa(commento.Iniziali)).Append(@"}{\*\atnauthor ")
                .Append(Escapa(commento.Autore)).Append(@"}\chatn {\*\annotation{\*\atnref ").Append(fine + 1).Append('}');
            if (commento.Data is DateTime data)
                rtfCommento.Append(@"{\*\atndate ").Append(Dttm(data)).Append('}');
            rtfCommento.Append(@"\pard\plain {\chatn }");
            var righe = commento.Testo.Replace("\r", "").Split('\n');
            for (int i = 0; i < righe.Length; i++)
            {
                if (i > 0)
                    rtfCommento.Append(@"\par ");
                rtfCommento.Append(Escapa(righe[i]));
            }
            return rtfCommento.Append('}').ToString();
        });

    /// <summary>
    /// Data e ora nel formato DTTM di Word: minuti (6 bit), ore (5), giorno (5), mese (4), anni dal 1900 (9),
    /// giorno della settimana (3).
    /// </summary>
    public static long Dttm(DateTime data) =>
        data.Minute | (data.Hour << 6) | (data.Day << 11) | (data.Month << 16) | ((long)(data.Year - 1900) << 20) | ((long)data.DayOfWeek << 29);

    public static DateTime? DaDttm(long dttm)
    {
        try
        {
            return new DateTime((int)((dttm >> 20) & 0x1FF) + 1900, (int)((dttm >> 16) & 0xF), (int)((dttm >> 11) & 0x1F),
                (int)((dttm >> 6) & 0x1F), (int)(dttm & 0x3F), 0);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    /// <summary>Il testo di un commento scritto in RTF, un paragrafo per riga.</summary>
    private static string TestoDi(string interno)
    {
        var pulito = Pulisci(RiferimentoODataGruppo().Replace(interno, ""));
        var contenuto = RtfLettore.Leggi("{" + @"\rtf1 " + pulito + "}");
        return string.Join("\n", contenuto.Paragrafi.Select(p => p.Testo.Trim())).Trim('\n');
    }

    private static int FineGruppo(string rtf, int apertura)
    {
        int livello = 0;
        for (int i = apertura; i < rtf.Length; i++)
        {
            switch (rtf[i])
            {
                case '\\':
                    i++;
                    break;
                case '{':
                    livello++;
                    break;
                case '}':
                    if (--livello == 0)
                        return i;
                    break;
            }
        }
        return -1;
    }

    private static string CarattereRtf(char c) => $@"\uc1\u{(short)c}?";

    private static string Escapa(string testo)
    {
        var rtf = new StringBuilder();
        foreach (var c in testo)
        {
            if (c is '\\' or '{' or '}')
                rtf.Append('\\').Append(c);
            else if (c == '\t')
                rtf.Append(@"\tab ");
            else if (c > 127)
                rtf.Append(CarattereRtf(c));
            else
                rtf.Append(c);
        }
        return rtf.ToString();
    }

    private static string TestoRtf(string rtf) =>
        CodiceRtf().Replace(SaltoUnicode().Replace(rtf, ""), m => m.Groups[1].Success
            ? Encoding.Latin1.GetString([byte.Parse(m.Groups[1].Value, NumberStyles.HexNumber)])
            : m.Groups[2].Success
                ? ((char)(ushort)short.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture)).ToString()
                : m.Groups[3].Value);

    [GeneratedRegex(@"\{\\\*\\annotation(?![a-z])")]
    private static partial Regex InizioAnnotazione();

    [GeneratedRegex(@"\{\\\*\\atnauthor ?([^}]*)\}")]
    private static partial Regex Autore();

    [GeneratedRegex(@"\{\\\*\\atnid ?[^}]*\}")]
    private static partial Regex Iniziali();

    [GeneratedRegex(@"\\chatn(?![a-z]) ?")]
    private static partial Regex Richiamo();

    [GeneratedRegex(@"\{\\\*\\atrfstart ?([^}]*)\}")]
    private static partial Regex InizioIntervallo();

    [GeneratedRegex(@"\{\\\*\\atrfend ?([^}]*)\}")]
    private static partial Regex FineIntervallo();

    [GeneratedRegex(@"\{\\\*\\atnref ?([^}]*)\}")]
    private static partial Regex Riferimento();

    [GeneratedRegex(@"\{\\\*\\atndate ?(\d+)\}")]
    private static partial Regex DataRtf();

    [GeneratedRegex(@"\{\\\*\\(?:atnref|atndate|atnparent|atnicn)[^}]*\}")]
    private static partial Regex RiferimentoODataGruppo();

    [GeneratedRegex(@"\\u(-?\d+) ?(?:\\'[0-9a-fA-F]{2}|\?)?")]
    private static partial Regex UnicodeRtf();

    [GeneratedRegex(@"\\'([0-9a-fA-F]{2})|\\u(-?\d+)\??|\\([\\{}])")]
    private static partial Regex CodiceRtf();

    [GeneratedRegex(@"\\uc\d+ ?")]
    private static partial Regex SaltoUnicode();
}
