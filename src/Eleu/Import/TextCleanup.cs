using System.Text.RegularExpressions;

namespace Eleu.Import;

/// <summary>
/// Turns the lines extracted from a PDF or from OCR (broken as on the page) into readable paragraphs:
/// removes page numbers and repeated headers and footers, rejoins words split by hyphens
/// and joins the lines of the same paragraph. All automatic, without asking the user.
/// </summary>
public static partial class TextCleanup
{
    /// <summary>Lines at the top and bottom of the page in which to look for headers and footers.</summary>
    private const int EdgeLines = 2;

    public static List<string> Paragraphs(IReadOnlyList<IReadOnlyList<string>> pages)
    {
        var cleaned = RemoveRepeatedEdges(pages
            .Select(p => p.Select(r => MultipleSpaces().Replace(r, " ").Trim()).ToList())
            .ToList());
        return JoinLines(cleaned.SelectMany(p => p).ToList());
    }

    private static List<List<string>> RemoveRepeatedEdges(List<List<string>> pages)
    {
        // An edge line (digits aside) repeated on at least half of the pages is a header or a footer.
        var count = new Dictionary<string, int>();
        foreach (var page in pages)
            foreach (var key in NonEmptyEdgeLines(page).Select(Normalize).Distinct())
                count[key] = count.GetValueOrDefault(key) + 1;

        int threshold = Math.Max(2, (pages.Count + 1) / 2);
        var repeated = count.Where(c => c.Value >= threshold && c.Key.Length > 0).Select(c => c.Key).ToHashSet();

        return pages.Select(page =>
        {
            var border = NonEmptyEdgeLines(page).ToHashSet();
            return page
                .Where(r => !(border.Contains(r) && (PageNumber().IsMatch(r) || repeated.Contains(Normalize(r)))))
                .ToList();
        }).ToList();
    }

    private static IEnumerable<string> NonEmptyEdgeLines(List<string> page)
    {
        var full = page.Where(r => r.Length > 0).ToList();
        return full.Take(EdgeLines).Concat(full.TakeLast(EdgeLines));
    }

    private static string Normalize(string row) => Digits().Replace(row.ToLowerInvariant(), "#");

    private static List<string> JoinLines(List<string> rows)
    {
        var lengths = rows.Where(r => r.Length > 0).Select(r => r.Length).Order().ToList();
        int typical = lengths.Count == 0 ? 0 : lengths[lengths.Count * 3 / 4];

        var paragraphs = new List<string>();
        var current = "";
        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            if (row.Length == 0)
            {
                Close();
                continue;
            }

            if (current.Length == 0)
                current = row;
            else if (current.EndsWith('-') && current.Length > 1 && char.IsLetter(current[^2]) && char.IsLower(row[0]))
                current = current[..^1] + row; // word split by a hyphen at the end of the line
            else
                current += " " + row;

            // Short line ending a sentence: end of the paragraph.
            bool endsSentence = row.Length > 0 && ".!?:»\"”".Contains(row[^1]);
            if (endsSentence && row.Length < typical * 0.8)
                Close();
        }
        Close();
        return paragraphs;

        void Close()
        {
            if (current.Length > 0)
                paragraphs.Add(current);
            current = "";
        }
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex MultipleSpaces();

    [GeneratedRegex(@"\d+")]
    private static partial Regex Digits();

    // "12", "- 12 -", "Pag. 3", "pagina 4 di 20", "5/20"
    [GeneratedRegex(@"^\W*(?:pag(?:ina)?\.?\s*)?\d{1,4}(?:\s*(?:di|/)\s*\d{1,4})?\W*$", RegexOptions.IgnoreCase)]
    private static partial Regex PageNumber();
}
