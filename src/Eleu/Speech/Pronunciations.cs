using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json.Serialization;

namespace Eleu.Speech;

/// <summary>How speech synthesis must say a word or an acronym: "NVDA" → "enne vu di a".</summary>
public sealed record Pronunciation([property: JsonPropertyName("Parola")] string Word, [property: JsonPropertyName("Come")] string SpokenAs);

/// <summary>
/// Pronunciation dictionary (Voce > Pronunce): before reading, whole words are replaced, ignoring
/// case. To bring reading back to the right point of the document, for every character
/// of the spoken text the matching position in the written text is kept.
/// </summary>
public static class Pronunciations
{
    public static (string Text, int[] Source) Apply(string text, IReadOnlyList<Pronunciation> pronunciations)
    {
        var valid = pronunciations.Where(p => p.Word.Trim().Length > 0).ToList();
        if (valid.Count == 0)
            return (text, Enumerable.Range(0, text.Length + 1).ToArray());

        // Longest first: "ONU" must not eat the start of "ONUS".
        var spokenAs = valid.GroupBy(p => p.Word.Trim(), StringComparer.CurrentCultureIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().SpokenAs, StringComparer.CurrentCultureIgnoreCase);
        var alternatives = string.Join("|", spokenAs.Keys.OrderByDescending(k => k.Length).Select(Regex.Escape));
        var rule = new Regex($@"(?<![\p{{L}}\p{{N}}])(?:{alternatives})(?![\p{{L}}\p{{N}}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        var @new = new StringBuilder();
        var source = new List<int>();
        int from = 0;
        foreach (Match m in rule.Matches(text))
        {
            for (int i = from; i < m.Index; i++)
            {
                @new.Append(text[i]);
                source.Add(i);
            }
            var replacement = spokenAs[m.Value];
            @new.Append(replacement);
            source.AddRange(Enumerable.Repeat(m.Index, replacement.Length));
            from = m.Index + m.Length;
        }
        for (int i = from; i < text.Length; i++)
        {
            @new.Append(text[i]);
            source.Add(i);
        }
        source.Add(text.Length);
        return (@new.ToString(), source.ToArray());
    }
}
