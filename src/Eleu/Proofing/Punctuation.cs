using System.Text.RegularExpressions;

namespace Eleu.Proofing;

/// <summary>
/// Punctuation and typing errors the dictionary does not see: spaces before punctuation or
/// missing after it, double spaces, repeated punctuation, lowercase after a period, repeated word, apostrophe
/// followed by a space, wrong accents ("perchè", "pò", "qual'è") and unclosed brackets.
/// Every error has its reason, to read to the person correcting, and almost always a correction.
/// </summary>
public static partial class Punctuation
{
    // Abbreviations after which the period does not end the sentence.
    private static readonly HashSet<string> Abbreviations = new(StringComparer.OrdinalIgnoreCase)
    {
        "ecc", "es", "pag", "pagg", "sig", "sigg", "sig.ra", "dott", "dr", "prof", "ing", "avv", "arch", "geom",
        "rag", "on", "sen", "gen", "col", "cap", "art", "cfr", "vol", "n", "nr", "tel", "fig", "p", "pp", "s", "ss",
        "ca", "c.a", "a.c", "d.c", "etc", "vs", "ad", "sez", "cit", "op", "ed", "trad", "id", "ib", "ibid", "st",
    };

    private static readonly (string Wrong, string Right)[] Accents =
    [
        ("perchè", "perché"), ("poichè", "poiché"), ("benchè", "benché"), ("affinchè", "affinché"),
        ("finchè", "finché"), ("sicchè", "sicché"), ("cosicchè", "cosicché"), ("purchè", "purché"),
        ("giacchè", "giacché"), ("nonchè", "nonché"), ("anzichè", "anziché"), ("fuorchè", "fuorché"),
        ("pò", "po'"), ("qual'è", "qual è"), ("qual'era", "qual era"), ("sè", "sé"), ("nè", "né"),
        ("trè", "tre"), ("ventitre", "ventitré"), ("trentatre", "trentatré"),
    ];

    public static List<LikelyError> Find(string text)
    {
        var errors = new SortedDictionary<int, LikelyError>();
        void Add(int start, int length, string? proofing, string reason)
        {
            if (start >= 0 && start + length <= text.Length && !errors.ContainsKey(start))
                errors[start] = new LikelyError(start, length, text.Substring(start, length), proofing, reason);
        }

        foreach (Match m in SpaceBefore().Matches(text))
            Add(m.Index, m.Length, m.Groups[1].Value, "spazio prima della punteggiatura");
        foreach (Match m in MissingSpaceAfterComma().Matches(text).Concat(MissingSpaceAfterPeriod().Matches(text)))
            Add(m.Index, m.Length, m.Groups[1].Value + " " + m.Groups[2].Value, "manca lo spazio dopo la punteggiatura");
        foreach (Match m in DoubleSpaces().Matches(text))
            Add(m.Index, m.Length, " ", "spazi in più");
        foreach (Match m in Repeated().Matches(text))
            Add(m.Index, m.Length, m.Value[..1], "punteggiatura ripetuta");
        foreach (Match m in AfterThePeriod().Matches(text))
        {
            var before = m.Groups[1].Value;
            if (Abbreviations.Contains(before) || before.Length == 1 && char.IsUpper(before[0]) || before.EndsWith('.'))
                continue; // "ecc. e", "A. Manzoni", "Aspetta... poi"
            int letter = m.Groups[3].Index;
            Add(letter, m.Groups[3].Length, char.ToUpper(m.Groups[3].Value[0]) + m.Groups[3].Value[1..], "minuscola dopo il punto");
        }
        foreach (Match m in RepeatedWord().Matches(text))
            Add(m.Index, m.Length, m.Groups[1].Value, "parola ripetuta");
        foreach (Match m in ApostropheAndSpace().Matches(text))
            Add(m.Index, m.Length, m.Groups[1].Value + m.Groups[2].Value, "spazio dopo l'apostrofo");
        foreach (var (wrong, right) in Accents)
            foreach (Match m in Regex.Matches(text, $@"(?<![\p{{L}}'’]){Regex.Escape(wrong)}(?![\p{{L}}])", RegexOptions.IgnoreCase))
                Add(m.Index, m.Length, SameCapital(m.Value, right), $"si scrive {right}");
        foreach (Match m in CapitalEApostrophe().Matches(text))
            Add(m.Index, m.Length, "È", "si scrive È, con l'accento");

        foreach (var (start, reason) in UnclosedBrackets(text))
            Add(start, 1, null, reason);
        return errors.Values.ToList();
    }

    private static string SameCapital(string original, string right) =>
        char.IsUpper(original[0]) ? char.ToUpper(right[0]) + right[1..] : right;

    /// <summary>Brackets opened and never closed (or closed without being opened) in the same paragraph.</summary>
    private static IEnumerable<(int, string)> UnclosedBrackets(string text)
    {
        int paragraphStart = 0;
        while (paragraphStart <= text.Length)
        {
            int end = text.IndexOfAny(['\n', '\a'], paragraphStart);
            if (end < 0)
                end = text.Length;
            var opened = new Stack<int>();
            for (int i = paragraphStart; i < end; i++)
            {
                if (text[i] == '(')
                    opened.Push(i);
                else if (text[i] == ')')
                {
                    if (opened.Count > 0)
                        opened.Pop();
                    else
                        yield return (i, "parentesi chiusa senza essere aperta");
                }
            }
            foreach (var i in opened.Reverse())
                yield return (i, "parentesi aperta e non chiusa");
            paragraphStart = end + 1;
        }
    }

    // " ," " ." " ;" ... (but not an ellipsis at the start of a sentence nor the space after other punctuation)
    [GeneratedRegex(@"(?<=[\p{L}\p{N}])[ \t]+([,;:.!?)])(?!\.\.)")]
    private static partial Regex SpaceBefore();

    // "ciao,come" (but not "3,5": letters are needed around it)
    [GeneratedRegex(@"(?<=\p{L})([,;])(\p{L})")]
    private static partial Regex MissingSpaceAfterComma();

    // "fine.Inizio" (but not "www.sito.it" nor "e.g.": two lowercase letters before and a capital after are needed)
    [GeneratedRegex(@"(?<=\p{Ll}{2})([.!?])(\p{Lu})")]
    private static partial Regex MissingSpaceAfterPeriod();

    [GeneratedRegex(@"(?<=\S)  +(?=\S)")]
    private static partial Regex DoubleSpaces();

    [GeneratedRegex(@",,+|;;+|::+|(?<!\.)\.\.(?!\.)")]
    private static partial Regex Repeated();

    // ". parola": the word before the period (for abbreviations), the period, the first lowercase letter after it.
    [GeneratedRegex(@"(?<![\p{L}.])([\p{L}.]+)([.!?])[ \t]+(\p{Ll}[\p{L}]*)")]
    private static partial Regex AfterThePeriod();

    [GeneratedRegex(@"(?<![\p{L}])(\p{L}+)[ \t]+\1(?![\p{L}])", RegexOptions.IgnoreCase)]
    private static partial Regex RepeatedWord();

    // "l' acqua", "dell' anno", "un' amica"
    [GeneratedRegex(@"(?<![\p{L}])((?:l|un|dell|all|nell|sull|dall|coll|d|c|s|m|t|v|quest|quell|bell|sant|tutt)['’])[ \t]+(\p{L})", RegexOptions.IgnoreCase)]
    private static partial Regex ApostropheAndSpace();

    // "E' vero" at the start of a sentence
    [GeneratedRegex(@"(?<=^|[.!?]\s+|\n)E['’](?=\s)")]
    private static partial Regex CapitalEApostrophe();
}
