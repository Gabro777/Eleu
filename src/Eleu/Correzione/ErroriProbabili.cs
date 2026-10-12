using System.Text.RegularExpressions;

namespace Eleu.Correzione;

/// <summary>Una parola sospetta nel testo, con la correzione proposta (se c'è).</summary>
/// <param name="Motivo">Per gli errori che non sono di ortografia, il perché ("spazio prima della punteggiatura").</param>
public sealed record ErroreProbabile(int Inizio, int Lunghezza, string Parola, string? Suggerimento, string? Motivo = null);

/// <summary>
/// Trova le parole probabilmente sbagliate: quelle che il dizionario non conosce e gli errori tipici
/// del riconoscimento del testo (OCR), come cifre al posto di lettere ("c0me") o caratteri spuri ("|").
/// </summary>
public static partial class ErroriProbabili
{
    // Cifre che l'OCR scambia spesso per lettere.
    private static readonly Dictionary<char, char> CifreComeLettere = new()
    {
        ['0'] = 'o', ['1'] = 'l', ['5'] = 's', ['8'] = 'b', ['6'] = 'b',
    };

    /// <param name="paroleSconosciute">Il controllo ortografico: parole non nel dizionario come (inizio, lunghezza).</param>
    /// <param name="suggerimenti">Correzioni proposte per una parola, la migliore per prima.</param>
    /// <param name="corretta">Se una parola è nel dizionario.</param>
    public static List<ErroreProbabile> Trova(
        string testo,
        Func<string, IEnumerable<(int Inizio, int Lunghezza)>> paroleSconosciute,
        Func<string, IReadOnlyList<string>> suggerimenti,
        Func<string, bool> corretta)
    {
        var errori = new SortedDictionary<int, ErroreProbabile>();

        // "II gatto": l'OCR legge spesso l'articolo "Il" come due I maiuscole, che il dizionario
        // accetta come numero romano. Se segue una parola minuscola, è quasi certamente "Il".
        foreach (Match m in DueIMaiuscole().Matches(testo))
            errori[m.Index] = new ErroreProbabile(m.Index, 2, m.Value[..2], "Il");

        foreach (Match m in Parola().Matches(testo))
        {
            if (errori.ContainsKey(m.Index))
                continue;
            var parola = m.Value;
            bool lettere = parola.Any(char.IsLetter);
            bool cifre = parola.Any(char.IsAsciiDigit);
            bool spuri = parola.Any(c => "|¦~^`".Contains(c));
            if (!(lettere && cifre && !Ordinale().IsMatch(parola)) && !spuri)
                continue;

            var ripulita = new string(parola.Where(c => !"|¦~^`".Contains(c)).Select(c => CifreComeLettere.GetValueOrDefault(c, c)).ToArray());
            string? suggerimento = ripulita.Length > 0 && ripulita != parola && corretta(ripulita)
                ? ripulita
                : MigliorSuggerimento(ripulita, suggerimenti(ripulita.Length > 0 ? ripulita : parola), corretta);
            errori[m.Index] = new ErroreProbabile(m.Index, m.Length, parola, suggerimento);
        }

        var giaTrovati = errori.Values.ToList();
        foreach (var (inizio, lunghezza) in paroleSconosciute(testo))
        {
            if (inizio < 0 || inizio + lunghezza > testo.Length)
                continue;
            // Una parte di una parola già segnalata (es. "sa" in "ca|sa") non è un errore in più.
            if (giaTrovati.Any(e => inizio < e.Inizio + e.Lunghezza && e.Inizio < inizio + lunghezza))
                continue;
            // Il suggerimento del dizionario si chiede solo per l'errore su cui ci si ferma: chiederlo
            // per tutti sarebbe lento su un documento lungo uscito dall'OCR.
            errori[inizio] = new ErroreProbabile(inizio, lunghezza, testo.Substring(inizio, lunghezza), null);
        }

        return errori.Values.ToList();
    }

    // Lettere e gruppi che l'OCR confonde spesso tra loro.
    private static readonly (string, string)[] ScambiOcr =
    [
        ("l", "i"), ("i", "l"), ("l", "t"), ("t", "l"), ("c", "e"), ("e", "c"), ("n", "u"), ("u", "n"),
        ("rn", "m"), ("m", "rn"), ("cl", "d"), ("d", "cl"), ("vv", "w"), ("ii", "u"), ("li", "h"), ("h", "li"),
    ];

    /// <summary>
    /// La correzione più probabile per una parola letta male dall'OCR: prima le parole ottenute con uno
    /// scambio tipico ("dlvano" → "divano") che il dizionario conosce, poi i suggerimenti del dizionario
    /// che corrispondono a uno scambio, infine il primo suggerimento del dizionario.
    /// </summary>
    public static string? MigliorSuggerimento(string parola, IReadOnlyList<string> suggerimenti, Func<string, bool> corretta)
    {
        var scambi = ConUnoScambio(parola).ToList();
        var nelDizionario = scambi.FirstOrDefault(corretta);
        if (nelDizionario is not null)
            return nelDizionario;

        var minuscole = scambi.Select(s => s.ToLowerInvariant()).ToHashSet();
        return suggerimenti.Take(8).FirstOrDefault(s => minuscole.Contains(s.ToLowerInvariant()))
            ?? suggerimenti.FirstOrDefault();
    }

    private static IEnumerable<string> ConUnoScambio(string parola)
    {
        foreach (var (letto, vero) in ScambiOcr)
        {
            for (int i = parola.IndexOf(letto, StringComparison.OrdinalIgnoreCase); i >= 0;
                 i = parola.IndexOf(letto, i + 1, StringComparison.OrdinalIgnoreCase))
            {
                // Si mantiene la maiuscola iniziale, se c'era.
                var sostituto = i == 0 && char.IsUpper(parola[0]) ? char.ToUpperInvariant(vero[0]) + vero[1..] : vero;
                yield return string.Concat(parola.AsSpan(0, i), sostituto, parola.AsSpan(i + letto.Length));
            }
        }
    }

    /// <summary>Il primo errore dopo la posizione (o l'ultimo prima, all'indietro); null se non ce ne sono.</summary>
    public static ErroreProbabile? Successivo(IReadOnlyList<ErroreProbabile> errori, int posizione, bool avanti) =>
        avanti
            ? errori.FirstOrDefault(e => e.Inizio >= posizione)
            : errori.LastOrDefault(e => e.Inizio + e.Lunghezza <= posizione);

    // Sequenze senza spazi che contengono almeno una lettera o una cifra, con i caratteri spuri dell'OCR.
    [GeneratedRegex(@"[\p{L}\p{N}|¦~^`']*[\p{L}\p{N}][\p{L}\p{N}|¦~^`']*")]
    private static partial Regex Parola();

    // Solo a inizio frase, così "Enrico II è nato..." resta com'è.
    [GeneratedRegex(@"(?<=(?:^|[.!?:]\s+))II(?= \p{Ll})", RegexOptions.Multiline)]
    private static partial Regex DueIMaiuscole();

    // Numeri ordinali e simili, che non sono errori: 1°, 2a, 3º, 10mo, 1980s.
    [GeneratedRegex(@"^\d+(°|º|ª|a|o|mo|ma|esimo|esima|s)$", RegexOptions.IgnoreCase)]
    private static partial Regex Ordinale();
}
