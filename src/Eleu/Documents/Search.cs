using System.Text;
using System.Text.RegularExpressions;

namespace Eleu.Documents;

/// <param name="Wildcards">"?" stands for any character, "*" for several characters (even none), "#" for a digit.</param>
public sealed record SearchOptions(bool MatchCase = false, bool WholeWord = false, bool Wildcards = false)
{
    public StringComparison Comparer =>
        // Ordinal: the match length is always the same as the search text length.
        MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
}

public static class Search
{
    /// <summary>
    /// Searches <paramref name="find"/> starting at <paramref name="from"/>: forward finds the first occurrence
    /// starting there or later, backward the last one starting before. Returns -1 if there is none.
    /// </summary>
    public static int Find(string text, string find, int from, bool forward, SearchOptions options) =>
        FindMatch(text, find, from, forward, options)?.Start ?? -1;

    /// <summary>Like <see cref="Find"/>, also giving the length of the found text (it can change with wildcards).</summary>
    public static (int Start, int Length)? FindMatch(string text, string find, int from, bool forward, SearchOptions options)
    {
        if (find.Length == 0 || text.Length == 0)
            return null;
        if (options.Wildcards)
            return FindWithWildcards(text, find, from, forward, options);

        if (forward)
        {
            for (int i = Math.Clamp(from, 0, text.Length); i <= text.Length - find.Length;)
            {
                int found = text.IndexOf(find, i, options.Comparer);
                if (found < 0)
                    return null;
                if (!options.WholeWord || WholeWord(text, found, find.Length))
                    return (found, find.Length);
                i = found + 1;
            }
            return null;
        }

        for (int maxEnd = Math.Min(text.Length - 1, from + find.Length - 2); maxEnd >= find.Length - 1;)
        {
            int found = text.LastIndexOf(find, maxEnd, maxEnd + 1, options.Comparer);
            if (found < 0)
                return null;
            if (!options.WholeWord || WholeWord(text, found, find.Length))
                return (found, find.Length);
            maxEnd = found + find.Length - 2;
        }
        return null;
    }

    /// <summary>All occurrences, without overlaps, in order.</summary>
    public static List<int> FindAll(string text, string find, SearchOptions options) =>
        FindAllMatches(text, find, options).Select(c => c.Start).ToList();

    public static List<(int Start, int Length)> FindAllMatches(string text, string find, SearchOptions options)
    {
        var results = new List<(int, int)>();
        int from = 0;
        while (FindMatch(text, find, from, true, options) is var (start, length))
        {
            results.Add((start, length));
            from = start + Math.Max(1, length);
        }
        return results;
    }

    public static bool Matches(string selection, string find, SearchOptions options)
    {
        if (find.Length == 0 || selection.Length == 0)
            return false;
        if (!options.Wildcards)
            return string.Equals(selection, find, options.Comparer);
        var all = FindWithWildcards(selection, find, 0, true, options with { WholeWord = false });
        return all is (0, var length) && length == selection.Length;
    }

    private static (int, int)? FindWithWildcards(string text, string find, int from, bool forward, SearchOptions options)
    {
        var expression = new Regex(Expression(find, options.WholeWord),
            options.MatchCase ? RegexOptions.CultureInvariant : RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        from = Math.Clamp(from, 0, text.Length);
        if (forward)
        {
            var m = expression.Match(text, from);
            return m.Success && m.Length > 0 ? (m.Index, m.Length) : null;
        }
        Match? last = null;
        for (var m = expression.Match(text); m.Success && m.Index < from; m = m.NextMatch())
            if (m.Length > 0)
                last = m;
        return last is null ? null : (last.Index, last.Length);
    }

    /// <summary>The search text with wildcards as a regular expression; "*" takes as little as possible and does not cross lines.</summary>
    public static string Expression(string find, bool wholeWord)
    {
        var expression = new StringBuilder();
        if (wholeWord)
            expression.Append(@"(?<![\p{L}\p{N}])");
        for (int i = 0; i < find.Length; i++)
            expression.Append(find[i] switch
            {
                '?' => @"[^\n\a]",
                // At the end or at the start "*" completes the word ("cas*" finds "casa"); in the middle it takes as little as possible.
                '*' when i == find.Length - 1 || i == 0 => @"[\p{L}\p{N}]*",
                '*' => @"[^\n\a]*?",
                '#' => @"\d",
                var c => Regex.Escape(c.ToString()),
            });
        if (wholeWord)
            expression.Append(@"(?![\p{L}\p{N}])");
        return expression.ToString();
    }

    private static bool WholeWord(string text, int start, int length)
    {
        bool firstOk = start == 0 || !char.IsLetterOrDigit(text[start - 1]);
        int end = start + length;
        bool afterOk = end >= text.Length || !char.IsLetterOrDigit(text[end]);
        return firstOk && afterOk;
    }
}
