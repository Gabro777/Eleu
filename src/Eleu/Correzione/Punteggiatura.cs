using System.Text.RegularExpressions;

namespace Eleu.Correzione;

/// <summary>
/// Errori di punteggiatura e di scrittura che il dizionario non vede: spazi prima della punteggiatura o
/// mancanti dopo, spazi doppi, punteggiatura ripetuta, minuscola dopo il punto, parola ripetuta, apostrofo
/// seguito da spazio, accenti sbagliati ("perchè", "pò", "qual'è") e parentesi non chiuse.
/// Ogni errore ha il suo motivo, da leggere a chi corregge, e quasi sempre una correzione.
/// </summary>
public static partial class Punteggiatura
{
    // Abbreviazioni dopo cui il punto non chiude la frase.
    private static readonly HashSet<string> Abbreviazioni = new(StringComparer.OrdinalIgnoreCase)
    {
        "ecc", "es", "pag", "pagg", "sig", "sigg", "sig.ra", "dott", "dr", "prof", "ing", "avv", "arch", "geom",
        "rag", "on", "sen", "gen", "col", "cap", "art", "cfr", "vol", "n", "nr", "tel", "fig", "p", "pp", "s", "ss",
        "ca", "c.a", "a.c", "d.c", "etc", "vs", "ad", "sez", "cit", "op", "ed", "trad", "id", "ib", "ibid", "st",
    };

    private static readonly (string Sbagliato, string Giusto)[] Accenti =
    [
        ("perchè", "perché"), ("poichè", "poiché"), ("benchè", "benché"), ("affinchè", "affinché"),
        ("finchè", "finché"), ("sicchè", "sicché"), ("cosicchè", "cosicché"), ("purchè", "purché"),
        ("giacchè", "giacché"), ("nonchè", "nonché"), ("anzichè", "anziché"), ("fuorchè", "fuorché"),
        ("pò", "po'"), ("qual'è", "qual è"), ("qual'era", "qual era"), ("sè", "sé"), ("nè", "né"),
        ("trè", "tre"), ("ventitre", "ventitré"), ("trentatre", "trentatré"),
    ];

    public static List<ErroreProbabile> Trova(string testo)
    {
        var errori = new SortedDictionary<int, ErroreProbabile>();
        void Aggiungi(int inizio, int lunghezza, string? correzione, string motivo)
        {
            if (inizio >= 0 && inizio + lunghezza <= testo.Length && !errori.ContainsKey(inizio))
                errori[inizio] = new ErroreProbabile(inizio, lunghezza, testo.Substring(inizio, lunghezza), correzione, motivo);
        }

        foreach (Match m in SpazioPrima().Matches(testo))
            Aggiungi(m.Index, m.Length, m.Groups[1].Value, "spazio prima della punteggiatura");
        foreach (Match m in SpazioMancanteVirgola().Matches(testo).Concat(SpazioMancantePunto().Matches(testo)))
            Aggiungi(m.Index, m.Length, m.Groups[1].Value + " " + m.Groups[2].Value, "manca lo spazio dopo la punteggiatura");
        foreach (Match m in SpaziDoppi().Matches(testo))
            Aggiungi(m.Index, m.Length, " ", "spazi in più");
        foreach (Match m in Ripetuta().Matches(testo))
            Aggiungi(m.Index, m.Length, m.Value[..1], "punteggiatura ripetuta");
        foreach (Match m in DopoIlPunto().Matches(testo))
        {
            var prima = m.Groups[1].Value;
            if (Abbreviazioni.Contains(prima) || prima.Length == 1 && char.IsUpper(prima[0]) || prima.EndsWith('.'))
                continue; // "ecc. e", "A. Manzoni", "Aspetta... poi"
            int lettera = m.Groups[3].Index;
            Aggiungi(lettera, m.Groups[3].Length, char.ToUpper(m.Groups[3].Value[0]) + m.Groups[3].Value[1..], "minuscola dopo il punto");
        }
        foreach (Match m in ParolaRipetuta().Matches(testo))
            Aggiungi(m.Index, m.Length, m.Groups[1].Value, "parola ripetuta");
        foreach (Match m in ApostrofoESpazio().Matches(testo))
            Aggiungi(m.Index, m.Length, m.Groups[1].Value + m.Groups[2].Value, "spazio dopo l'apostrofo");
        foreach (var (sbagliato, giusto) in Accenti)
            foreach (Match m in Regex.Matches(testo, $@"(?<![\p{{L}}'’]){Regex.Escape(sbagliato)}(?![\p{{L}}])", RegexOptions.IgnoreCase))
                Aggiungi(m.Index, m.Length, StessaMaiuscola(m.Value, giusto), $"si scrive {giusto}");
        foreach (Match m in EMaiuscolaApostrofo().Matches(testo))
            Aggiungi(m.Index, m.Length, "È", "si scrive È, con l'accento");

        foreach (var (inizio, motivo) in ParentesiNonChiuse(testo))
            Aggiungi(inizio, 1, null, motivo);
        return errori.Values.ToList();
    }

    private static string StessaMaiuscola(string originale, string giusto) =>
        char.IsUpper(originale[0]) ? char.ToUpper(giusto[0]) + giusto[1..] : giusto;

    /// <summary>Parentesi aperte e mai chiuse (o chiuse senza essere aperte) nello stesso paragrafo.</summary>
    private static IEnumerable<(int, string)> ParentesiNonChiuse(string testo)
    {
        int inizioParagrafo = 0;
        while (inizioParagrafo <= testo.Length)
        {
            int fine = testo.IndexOfAny(['\n', '\a'], inizioParagrafo);
            if (fine < 0)
                fine = testo.Length;
            var aperte = new Stack<int>();
            for (int i = inizioParagrafo; i < fine; i++)
            {
                if (testo[i] == '(')
                    aperte.Push(i);
                else if (testo[i] == ')')
                {
                    if (aperte.Count > 0)
                        aperte.Pop();
                    else
                        yield return (i, "parentesi chiusa senza essere aperta");
                }
            }
            foreach (var i in aperte.Reverse())
                yield return (i, "parentesi aperta e non chiusa");
            inizioParagrafo = fine + 1;
        }
    }

    // " ," " ." " ;" ... (ma non i tre puntini a inizio frase né lo spazio dopo un'altra punteggiatura)
    [GeneratedRegex(@"(?<=[\p{L}\p{N}])[ \t]+([,;:.!?)])(?!\.\.)")]
    private static partial Regex SpazioPrima();

    // "ciao,come" (ma non "3,5": servono lettere attorno)
    [GeneratedRegex(@"(?<=\p{L})([,;])(\p{L})")]
    private static partial Regex SpazioMancanteVirgola();

    // "fine.Inizio" (ma non "www.sito.it" né "e.g.": servono due minuscole prima e una maiuscola dopo)
    [GeneratedRegex(@"(?<=\p{Ll}{2})([.!?])(\p{Lu})")]
    private static partial Regex SpazioMancantePunto();

    [GeneratedRegex(@"(?<=\S)  +(?=\S)")]
    private static partial Regex SpaziDoppi();

    [GeneratedRegex(@",,+|;;+|::+|(?<!\.)\.\.(?!\.)")]
    private static partial Regex Ripetuta();

    // ". parola": la parola prima del punto (per le abbreviazioni), il punto, la prima lettera minuscola dopo.
    [GeneratedRegex(@"(?<![\p{L}.])([\p{L}.]+)([.!?])[ \t]+(\p{Ll}[\p{L}]*)")]
    private static partial Regex DopoIlPunto();

    [GeneratedRegex(@"(?<![\p{L}])(\p{L}+)[ \t]+\1(?![\p{L}])", RegexOptions.IgnoreCase)]
    private static partial Regex ParolaRipetuta();

    // "l' acqua", "dell' anno", "un' amica"
    [GeneratedRegex(@"(?<![\p{L}])((?:l|un|dell|all|nell|sull|dall|coll|d|c|s|m|t|v|quest|quell|bell|sant|tutt)['’])[ \t]+(\p{L})", RegexOptions.IgnoreCase)]
    private static partial Regex ApostrofoESpazio();

    // "E' vero" a inizio frase
    [GeneratedRegex(@"(?<=^|[.!?]\s+|\n)E['’](?=\s)")]
    private static partial Regex EMaiuscolaApostrofo();
}
