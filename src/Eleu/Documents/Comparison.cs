namespace Eleu.Documents;

public enum DifferenceKind { Changed, OnlyHere, OnlyInOther }

/// <summary>
/// A difference between this document and the other one. <see cref="Here"/> is the number (from 0) of the paragraph of this
/// document it refers to: for what is only in the other one, the paragraph after which it would go (-1 = at the top).
/// </summary>
public sealed record Difference(DifferenceKind Kind, int Here, int Other, string TextHere, string OtherText)
{
    /// <summary>For a changed paragraph: the removed and added words, to read; otherwise the text.</summary>
    public string Description => Kind switch
    {
        DifferenceKind.Changed => $"Paragrafo {Here + 1} cambiato. {Comparison.ChangedWords(TextHere, OtherText)}",
        DifferenceKind.OnlyHere => $"Paragrafo {Here + 1} solo in questo documento: {Short(TextHere)}",
        _ => (Here < 0 ? "In cima" : $"Dopo il paragrafo {Here + 1}") + $", solo nell'altro documento: {Short(OtherText)}",
    };

    private static string Short(string text) => text.Length > 120 ? text[..120] + "..." : text.Length == 0 ? "(paragrafo vuoto)" : text;
}

/// <summary>
/// Comparison between two documents (Strumenti > Confronta con un altro documento): which paragraphs changed,
/// which exist in only one of the two. Uses the longest common subsequence, like diff programs.
/// </summary>
public static class Comparison
{
    /// <summary>Beyond this size (paragraphs × paragraphs) the middle part is compared line by line.</summary>
    private const long MaxCells = 4_000_000;

    public static List<Difference> Differences(IReadOnlyList<string> here, IReadOnlyList<string> other)
    {
        var a = here.Select(Normalize).ToList();
        var b = other.Select(Normalize).ToList();
        var operations = Align(a, b);

        // A group of removed lines followed by added lines: the first pairs are changed paragraphs.
        var differences = new List<Difference>();
        int lastHere = -1;
        for (int k = 0; k < operations.Count;)
        {
            if (operations[k].Kind == 0)
            {
                lastHere = operations[k].I;
                k++;
                continue;
            }
            var removed = new List<int>();
            var added = new List<int>();
            while (k < operations.Count && operations[k].Kind != 0)
            {
                if (operations[k].Kind < 0)
                    removed.Add(operations[k].I);
                else
                    added.Add(operations[k].J);
                k++;
            }
            int pairs = Math.Min(removed.Count, added.Count);
            for (int c = 0; c < pairs; c++)
                differences.Add(new Difference(DifferenceKind.Changed, removed[c], added[c], here[removed[c]], other[added[c]]));
            foreach (var i in removed.Skip(pairs))
                differences.Add(new Difference(DifferenceKind.OnlyHere, i, -1, here[i], ""));
            int after = removed.Count > 0 ? removed[^1] : lastHere;
            foreach (var j in added.Skip(pairs))
                differences.Add(new Difference(DifferenceKind.OnlyInOther, after, j, "", other[j]));
            if (removed.Count > 0)
                lastHere = removed[^1];
        }
        return differences;
    }

    /// <summary>"Tolto: «gatto». Aggiunto: «cane»." for two versions of a paragraph.</summary>
    public static string ChangedWords(string here, string other)
    {
        var a = here.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var b = other.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var operations = Align(a, b);
        var removed = operations.Where(o => o.Kind < 0).Select(o => a[o.I]).ToList();
        var added = operations.Where(o => o.Kind > 0).Select(o => b[o.J]).ToList();
        var parts = new List<string>();
        if (removed.Count > 0)
            parts.Add($"Qui: «{string.Join(" ", removed)}»");
        if (added.Count > 0)
            parts.Add($"nell'altro: «{string.Join(" ", added)}»");
        return parts.Count == 0 ? "Cambiano solo gli spazi." : string.Join("; ", parts) + ".";
    }

    private static string Normalize(string paragraph) => string.Join(' ', paragraph.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>The operations to go from a to b: 0 = equal (I in a, J in b), -1 = only in a, +1 = only in b.</summary>
    private static List<(int Kind, int I, int J)> Align(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        int start = 0;
        while (start < a.Count && start < b.Count && a[start] == b[start])
            start++;
        int tailA = a.Count, tailB = b.Count;
        while (tailA > start && tailB > start && a[tailA - 1] == b[tailB - 1])
        {
            tailA--;
            tailB--;
        }

        var operations = new List<(int, int, int)>();
        for (int i = 0; i < start; i++)
            operations.Add((0, i, i));

        int n = tailA - start, m = tailB - start;
        if ((long)n * m > MaxCells)
        {
            // Too big for the table: lines are compared in order.
            for (int i = 0; i < n; i++)
                operations.Add((-1, start + i, -1));
            for (int j = 0; j < m; j++)
                operations.Add((1, -1, start + j));
        }
        else
        {
            var lcs = new int[n + 1, m + 1];
            for (int i = n - 1; i >= 0; i--)
                for (int j = m - 1; j >= 0; j--)
                    lcs[i, j] = a[start + i] == b[start + j] ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
            int x = 0, y = 0;
            while (x < n || y < m)
            {
                if (x < n && y < m && a[start + x] == b[start + y])
                {
                    operations.Add((0, start + x, start + y));
                    x++;
                    y++;
                }
                else if (y < m && (x == n || lcs[x, y + 1] > lcs[x + 1, y]))
                {
                    operations.Add((1, -1, start + y));
                    y++;
                }
                else
                {
                    operations.Add((-1, start + x, -1));
                    x++;
                }
            }
        }
        for (int k = 0; k < a.Count - tailA; k++)
            operations.Add((0, tailA + k, tailB + k));
        return operations;
    }
}
