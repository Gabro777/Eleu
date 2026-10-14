using System.Text.RegularExpressions;
using Eleu.Documents;
using System.Text.Json.Serialization;

namespace Eleu.Proofing;

/// <summary>What to clean up (Strumenti > Pulizia del testo).</summary>
public sealed record CleanupOptions(
    [property: JsonPropertyName("SpaziDoppi")] bool DoubleSpaces = true,
    [property: JsonPropertyName("SpaziAiBordi")] bool EdgeSpaces = true,
    [property: JsonPropertyName("RigheVuote")] bool BlankLines = true,
    [property: JsonPropertyName("Sillabazione")] bool Hyphenation = true,
    [property: JsonPropertyName("ParagrafiSpezzati")] bool BrokenParagraphs = false);

/// <summary>An edit to the text: replace <see cref="Length"/> characters from <see cref="Start"/> with <see cref="New"/>.</summary>
public sealed record Edit(int Start, int Length, string New);

/// <summary>
/// On-demand text cleanup, typical for text copied from PDFs or the web: double spaces, spaces at the start and end
/// of paragraphs, repeated blank lines, words split by a hyphen at the end of a line ("rico-\nstruire") and paragraphs
/// broken mid-sentence. Returns the edits to make, so whoever applies them touches only those spots and the
/// formatting of the rest stays. Tables are not touched.
/// </summary>
public static partial class Cleanup
{
    public static List<Edit> Edits(string text, CleanupOptions options, int from = 0, int to = -1)
    {
        to = to < 0 ? text.Length : Math.Min(to, text.Length);
        var edits = new List<Edit>();
        var tables = TextStructure.Tables(text);
        bool InTable(int position) => tables.Any(t => position >= t.Start && position < t.End);
        void Add(Regex rule, Func<Match, string> @new)
        {
            foreach (Match m in rule.Matches(text))
                if (m.Index >= from && m.Index + m.Length <= to && !text.AsSpan(m.Index, m.Length).ContainsAny(Technical)
                    && !InTable(m.Index))
                    edits.Add(new Edit(m.Index, m.Length, @new(m)));
        }

        if (options.Hyphenation)
            Add(Hyphenated(), _ => "");
        if (options.BrokenParagraphs)
            Add(Broken(), _ => " ");
        if (options.BlankLines)
            Add(RepeatedBlankLines(), _ => "\n\n");
        if (options.EdgeSpaces)
        {
            Add(LeadingSpaces(), _ => "");
            Add(TrailingSpaces(), _ => "");
        }
        if (options.DoubleSpaces)
            Add(MultipleSpaces(), _ => " ");

        // If two rules touch the same spot the first one wins (the order above).
        var result = new List<Edit>();
        foreach (var m in edits.OrderBy(m => m.Start))
            if (result.Count == 0 || m.Start >= result[^1].Start + result[^1].Length)
                result.Add(m);
        return result;
    }

    /// <summary>Applies the edits to a text (for tests and for code working on strings).</summary>
    public static string Apply(string text, IEnumerable<Edit> edits)
    {
        foreach (var m in edits.OrderByDescending(m => m.Start))
            text = text.Remove(m.Start, m.Length).Insert(m.Start, m.New);
        return text;
    }

    private static readonly char[] Technical = [TextStructure.RowStart, TextStructure.RowEnd, TextStructure.CellEnd];

    // "rico-\nstruire": hyphen at end of line with a lowercase letter after it
    [GeneratedRegex(@"(?<=\p{L})-[ \t]*\n[ \t]*(?=\p{Ll})")]
    private static partial Regex Hyphenated();

    // A line break after a line that does not end the sentence, before a lowercase letter
    [GeneratedRegex(@"(?<=[\p{L},])[ \t]*\n[ \t]*(?=\p{Ll})")]
    private static partial Regex Broken();

    [GeneratedRegex(@"\n(?:[ \t]*\n){2,}")]
    private static partial Regex RepeatedBlankLines();

    [GeneratedRegex(@"(?<=^|\n)[ \t]+(?=\S)")]
    private static partial Regex LeadingSpaces();

    [GeneratedRegex(@"(?<=\S)[ \t]+(?=\n|$)")]
    private static partial Regex TrailingSpaces();

    [GeneratedRegex(@"(?<=\S)[ \t]{2,}(?=\S)")]
    private static partial Regex MultipleSpaces();
}

/// <summary>A pair of Multiple replacements: what to find and what to replace it with.</summary>
public sealed record ReplacementPair([property: JsonPropertyName("Cerca")] string Find, [property: JsonPropertyName("Sostituisci")] string Replace);

public static class MultipleReplacements
{
    /// <summary>
    /// The edits to apply all pairs at once, searching each pair in the original text
    /// (so one replacement does not trigger another). If two pairs touch the same spot the first one wins.
    /// </summary>
    public static List<Edit> Edits(string text, IReadOnlyList<ReplacementPair> pairs, SearchOptions options)
    {
        var edits = new List<Edit>();
        foreach (var pair in pairs.Where(c => c.Find.Length > 0))
            foreach (var (start, length) in Search.FindAllMatches(text, pair.Find, options))
                if (!edits.Any(m => start < m.Start + m.Length && m.Start < start + length))
                    edits.Add(new Edit(start, length, pair.Replace));
        return edits.OrderBy(m => m.Start).ToList();
    }
}
