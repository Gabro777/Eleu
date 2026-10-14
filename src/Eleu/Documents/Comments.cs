using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Eleu.Documents;

/// <summary>A comment: who wrote it, when and what it says. The commented text is in the document.</summary>
public sealed record Comment(string Text, string Author = "", DateTime? Date = null)
{
    /// <summary>The author's initials, as Word wants them (at most 9 letters).</summary>
    public string Initials
    {
        get
        {
            var initials = string.Concat(Author.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(p => char.ToUpperInvariant(p[0])));
            return initials.Length > 9 ? initials[..9] : initials;
        }
    }
}

/// <summary>
/// Comments. In files they are text ranges with a margin note: w:commentRangeStart/End (DOCX),
/// office:annotation (ODT), {\*\atrfstart}/{\*\annotation} (RTF). The text box does not know them: during
/// loading and saving the start and end of each comment are two placeholder characters (Unicode private use
/// area, after the images); the comment data is kept in a separate list. In the open document each comment
/// is a text range that moves by itself while typing.
/// </summary>
public static partial class CommentMarkers
{
    // Starts from U+F600, ends from U+F780, up to U+F8FF: after the images (from U+F200 to U+F5FF).
    private const int FirstStart = 0xF600, FirstEnd = 0xF780;
    public const int Max = 384;

    public static char Start(int index) => (char)(FirstStart + index);

    public static char End(int index) => (char)(FirstEnd + index);

    /// <summary>Which comment the character marks the start of, or -1.</summary>
    public static int StartIndex(char c) => c >= FirstStart && c < FirstStart + Max ? c - FirstStart : -1;

    /// <summary>Which comment the character marks the end of, or -1.</summary>
    public static int EndIndex(char c) => c >= FirstEnd && c < FirstEnd + Max ? c - FirstEnd : -1;

    public static bool Placeholder(char c) => StartIndex(c) >= 0 || EndIndex(c) >= 0;

    /// <summary>How a comment is read: "Commento di Anna: testo".</summary>
    public static string Describe(Comment comment) =>
        $"Commento{(comment.Author.Length > 0 ? $" di {comment.Author}" : "")}: {comment.Text.ReplaceLineEndings(" ")}";

    // ---------- RTF ----------

    /// <summary>RTF of a file → same RTF with placeholders instead of comments; the data goes into <paramref name="comments"/>.</summary>
    public static string ToPlaceholders(string rtf, List<Comment> comments)
    {
        if (!rtf.Contains(@"\annotation", StringComparison.Ordinal))
            return Clean(rtf);

        var authors = new Queue<string>(Author().Matches(rtf).Select(m => RtfText(m.Groups[1].Value).Trim()));
        var withRange = new HashSet<string>(RangeStart().Matches(rtf).Select(m => m.Groups[1].Value.Trim()));
        var indexes = new Dictionary<string, int>();

        // Every {\*\annotation ...} becomes a comment; where there is no range, start and end are right there.
        var result = new StringBuilder();
        int from = 0;
        foreach (Match m in AnnotationStart().Matches(rtf))
        {
            if (m.Index < from)
                continue;
            int end = GroupEnd(rtf, m.Index);
            if (end < 0)
                break;
            result.Append(rtf, from, m.Index - from);
            from = end + 1;
            var inner = rtf[(m.Index + m.Length)..end];
            var author = authors.Count > 0 ? authors.Dequeue() : "";
            if (comments.Count >= Max)
                continue;
            var reference = Reference().Match(inner) is { Success: true } r ? r.Groups[1].Value.Trim() : "";
            DateTime? date = RtfDate().Match(inner) is { Success: true } d && long.TryParse(d.Groups[1].Value, out var dttm) ? FromDttm(dttm) : null;
            comments.Add(new Comment(TextOf(inner), author, date));
            int index = comments.Count - 1;
            if (reference.Length > 0 && withRange.Contains(reference) && !indexes.ContainsKey(reference))
                indexes[reference] = index;
            else
                result.Append('{').Append(RtfCharacter(Start(index))).Append(RtfCharacter(End(index))).Append('}');
        }
        result.Append(rtf, from, rtf.Length - from);

        var text = RangeStart().Replace(result.ToString(), m =>
            indexes.TryGetValue(m.Groups[1].Value.Trim(), out int i) ? "{" + RtfCharacter(Start(i)) + "}" : "");
        text = RangeEnd().Replace(text, m =>
            indexes.TryGetValue(m.Groups[1].Value.Trim(), out int i) ? "{" + RtfCharacter(End(i)) + "}" : "");
        return Clean(text);
    }

    /// <summary>Removes the leftover pieces of comments (authors, initials, reference marks).</summary>
    private static string Clean(string rtf) =>
        ReferenceRegex().Replace(Initials().Replace(Author().Replace(rtf, ""), ""), "");

    /// <summary>RTF of the text box with placeholders → RTF with real comments, as Word writes them.</summary>
    public static string FromPlaceholders(string rtf, IReadOnlyList<Comment> comments) =>
        UnicodeRtf().Replace(rtf, m =>
        {
            var c = (char)(ushort)short.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            if (StartIndex(c) is var start and >= 0)
                return start < comments.Count ? $@"{{\*\atrfstart {start + 1}}}" : "";
            if (EndIndex(c) is not (var end and >= 0))
                return m.Value;
            if (end >= comments.Count)
                return "";
            var comment = comments[end];
            var commentRtf = new StringBuilder();
            commentRtf.Append($@"{{\*\atrfend {end + 1}}}{{\*\atnid ").Append(Escape(comment.Initials)).Append(@"}{\*\atnauthor ")
                .Append(Escape(comment.Author)).Append(@"}\chatn {\*\annotation{\*\atnref ").Append(end + 1).Append('}');
            if (comment.Date is DateTime date)
                commentRtf.Append(@"{\*\atndate ").Append(Dttm(date)).Append('}');
            commentRtf.Append(@"\pard\plain {\chatn }");
            var rows = comment.Text.Replace("\r", "").Split('\n');
            for (int i = 0; i < rows.Length; i++)
            {
                if (i > 0)
                    commentRtf.Append(@"\par ");
                commentRtf.Append(Escape(rows[i]));
            }
            return commentRtf.Append('}').ToString();
        });

    /// <summary>
    /// Date and time in Word's DTTM format: minutes (6 bits), hours (5), day (5), month (4), years since 1900 (9),
    /// day of the week (3).
    /// </summary>
    public static long Dttm(DateTime date) =>
        date.Minute | (date.Hour << 6) | (date.Day << 11) | (date.Month << 16) | ((long)(date.Year - 1900) << 20) | ((long)date.DayOfWeek << 29);

    public static DateTime? FromDttm(long dttm)
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

    /// <summary>The text of a comment written in RTF, one paragraph per line.</summary>
    private static string TextOf(string inner)
    {
        var cleaned = Clean(ReferenceOrDateGroup().Replace(inner, ""));
        var content = RtfReader.Read("{" + @"\rtf1 " + cleaned + "}");
        return string.Join("\n", content.Paragraphs.Select(p => p.Text.Trim())).Trim('\n');
    }

    private static int GroupEnd(string rtf, int opening)
    {
        int level = 0;
        for (int i = opening; i < rtf.Length; i++)
        {
            switch (rtf[i])
            {
                case '\\':
                    i++;
                    break;
                case '{':
                    level++;
                    break;
                case '}':
                    if (--level == 0)
                        return i;
                    break;
            }
        }
        return -1;
    }

    private static string RtfCharacter(char c) => $@"\uc1\u{(short)c}?";

    private static string Escape(string text)
    {
        var rtf = new StringBuilder();
        foreach (var c in text)
        {
            if (c is '\\' or '{' or '}')
                rtf.Append('\\').Append(c);
            else if (c == '\t')
                rtf.Append(@"\tab ");
            else if (c > 127)
                rtf.Append(RtfCharacter(c));
            else
                rtf.Append(c);
        }
        return rtf.ToString();
    }

    private static string RtfText(string rtf) =>
        RtfCode().Replace(UnicodeSkip().Replace(rtf, ""), m => m.Groups[1].Success
            ? Encoding.Latin1.GetString([byte.Parse(m.Groups[1].Value, NumberStyles.HexNumber)])
            : m.Groups[2].Success
                ? ((char)(ushort)short.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture)).ToString()
                : m.Groups[3].Value);

    [GeneratedRegex(@"\{\\\*\\annotation(?![a-z])")]
    private static partial Regex AnnotationStart();

    [GeneratedRegex(@"\{\\\*\\atnauthor ?([^}]*)\}")]
    private static partial Regex Author();

    [GeneratedRegex(@"\{\\\*\\atnid ?[^}]*\}")]
    private static partial Regex Initials();

    [GeneratedRegex(@"\\chatn(?![a-z]) ?")]
    private static partial Regex ReferenceRegex();

    [GeneratedRegex(@"\{\\\*\\atrfstart ?([^}]*)\}")]
    private static partial Regex RangeStart();

    [GeneratedRegex(@"\{\\\*\\atrfend ?([^}]*)\}")]
    private static partial Regex RangeEnd();

    [GeneratedRegex(@"\{\\\*\\atnref ?([^}]*)\}")]
    private static partial Regex Reference();

    [GeneratedRegex(@"\{\\\*\\atndate ?(\d+)\}")]
    private static partial Regex RtfDate();

    [GeneratedRegex(@"\{\\\*\\(?:atnref|atndate|atnparent|atnicn)[^}]*\}")]
    private static partial Regex ReferenceOrDateGroup();

    [GeneratedRegex(@"\\u(-?\d+) ?(?:\\'[0-9a-fA-F]{2}|\?)?")]
    private static partial Regex UnicodeRtf();

    [GeneratedRegex(@"\\'([0-9a-fA-F]{2})|\\u(-?\d+)\??|\\([\\{}])")]
    private static partial Regex RtfCode();

    [GeneratedRegex(@"\\uc\d+ ?")]
    private static partial Regex UnicodeSkip();
}
