using System.Text.RegularExpressions;

namespace Eleu.Proofing;

/// <summary>A suspicious word in the text, with the proposed correction (if any).</summary>
/// <param name="Reason">For errors that are not spelling ones, the reason ("spazio prima della punteggiatura").</param>
public sealed record LikelyError(int Start, int Length, string Word, string? Suggestion, string? Reason = null);

/// <summary>
/// Finds the words that are probably wrong: those the dictionary does not know and the typical errors
/// of text recognition (OCR), like digits instead of letters ("c0me") or stray characters ("|").
/// </summary>
public static partial class LikelyErrors
{
    // Digits that OCR often mistakes for letters.
    private static readonly Dictionary<char, char> DigitsAsLetters = new()
    {
        ['0'] = 'o', ['1'] = 'l', ['5'] = 's', ['8'] = 'b', ['6'] = 'b',
    };

    /// <param name="unknownWords">The spell checker: words not in the dictionary as (start, length).</param>
    /// <param name="suggestions">Proposed corrections for a word, the best first.</param>
    /// <param name="isCorrect">Whether a word is in the dictionary.</param>
    public static List<LikelyError> Find(
        string text,
        Func<string, IEnumerable<(int Start, int Length)>> unknownWords,
        Func<string, IReadOnlyList<string>> suggestions,
        Func<string, bool> isCorrect)
    {
        var errors = new SortedDictionary<int, LikelyError>();

        // "II gatto": OCR often reads the article "Il" as two capital I's, which the dictionary
        // accepts as a Roman numeral. If a lowercase word follows, it is almost certainly "Il".
        foreach (Match m in TwoCapitalIs().Matches(text))
            errors[m.Index] = new LikelyError(m.Index, 2, m.Value[..2], "Il");

        foreach (Match m in Word().Matches(text))
        {
            if (errors.ContainsKey(m.Index))
                continue;
            var word = m.Value;
            bool letters = word.Any(char.IsLetter);
            bool digits = word.Any(char.IsAsciiDigit);
            bool stray = word.Any(c => "|¦~^`".Contains(c));
            if (!(letters && digits && !Ordinal().IsMatch(word)) && !stray)
                continue;

            var cleaned = new string(word.Where(c => !"|¦~^`".Contains(c)).Select(c => DigitsAsLetters.GetValueOrDefault(c, c)).ToArray());
            string? suggestion = cleaned.Length > 0 && cleaned != word && isCorrect(cleaned)
                ? cleaned
                : BestSuggestion(cleaned, suggestions(cleaned.Length > 0 ? cleaned : word), isCorrect);
            errors[m.Index] = new LikelyError(m.Index, m.Length, word, suggestion);
        }

        var alreadyFound = errors.Values.ToList();
        foreach (var (start, length) in unknownWords(text))
        {
            if (start < 0 || start + length > text.Length)
                continue;
            // Part of a word already reported (e.g. "sa" in "ca|sa") is not one more error.
            if (alreadyFound.Any(e => start < e.Start + e.Length && e.Start < start + length))
                continue;
            // The dictionary suggestion is asked only for the error we stop on: asking it
            // for all of them would be slow on a long document coming out of OCR.
            errors[start] = new LikelyError(start, length, text.Substring(start, length), null);
        }

        return errors.Values.ToList();
    }

    // Letters and groups that OCR often confuses with each other.
    private static readonly (string, string)[] OcrSwaps =
    [
        ("l", "i"), ("i", "l"), ("l", "t"), ("t", "l"), ("c", "e"), ("e", "c"), ("n", "u"), ("u", "n"),
        ("rn", "m"), ("m", "rn"), ("cl", "d"), ("d", "cl"), ("vv", "w"), ("ii", "u"), ("li", "h"), ("h", "li"),
    ];

    /// <summary>
    /// The most likely correction for a word misread by OCR: first the words obtained with a
    /// typical swap ("dlvano" → "divano") that the dictionary knows, then the dictionary suggestions
    /// that match a swap, finally the first dictionary suggestion.
    /// </summary>
    public static string? BestSuggestion(string word, IReadOnlyList<string> suggestions, Func<string, bool> isCorrect)
    {
        var swaps = WithOneSwap(word).ToList();
        var inDictionary = swaps.FirstOrDefault(isCorrect);
        if (inDictionary is not null)
            return inDictionary;

        var lowercase = swaps.Select(s => s.ToLowerInvariant()).ToHashSet();
        return suggestions.Take(8).FirstOrDefault(s => lowercase.Contains(s.ToLowerInvariant()))
            ?? suggestions.FirstOrDefault();
    }

    private static IEnumerable<string> WithOneSwap(string word)
    {
        foreach (var (read, real) in OcrSwaps)
        {
            for (int i = word.IndexOf(read, StringComparison.OrdinalIgnoreCase); i >= 0;
                 i = word.IndexOf(read, i + 1, StringComparison.OrdinalIgnoreCase))
            {
                // Keep the initial capital, if there was one.
                var replacement = i == 0 && char.IsUpper(word[0]) ? char.ToUpperInvariant(real[0]) + real[1..] : real;
                yield return string.Concat(word.AsSpan(0, i), replacement, word.AsSpan(i + read.Length));
            }
        }
    }

    /// <summary>The first error after the position (or the last one before it, going backward); null if there are none.</summary>
    public static LikelyError? Next(IReadOnlyList<LikelyError> errors, int position, bool forward) =>
        forward
            ? errors.FirstOrDefault(e => e.Start >= position)
            : errors.LastOrDefault(e => e.Start + e.Length <= position);

    // Runs without spaces that contain at least one letter or digit, with OCR stray characters.
    [GeneratedRegex(@"[\p{L}\p{N}|¦~^`']*[\p{L}\p{N}][\p{L}\p{N}|¦~^`']*")]
    private static partial Regex Word();

    // Only at the start of a sentence, so "Enrico II è nato..." stays as it is.
    [GeneratedRegex(@"(?<=(?:^|[.!?:]\s+))II(?= \p{Ll})", RegexOptions.Multiline)]
    private static partial Regex TwoCapitalIs();

    // Ordinal numbers and the like, which are not errors: 1°, 2a, 3º, 10mo, 1980s.
    [GeneratedRegex(@"^\d+(°|º|ª|a|o|mo|ma|esimo|esima|s)$", RegexOptions.IgnoreCase)]
    private static partial Regex Ordinal();
}
