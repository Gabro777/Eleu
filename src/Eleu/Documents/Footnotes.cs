using System.Text;
using System.Text.RegularExpressions;

namespace Eleu.Documents;

/// <summary>
/// Footnotes. In files they are {\footnote ...} (RTF) and w:footnoteReference (DOCX); the Windows text
/// box does not know them. As for <see cref="Bookmarks"/>, during loading and saving each footnote
/// is a placeholder character (Unicode private use area, after the bookmarks one) at the reference point;
/// the footnote texts are kept in a separate list. In the open document the reference is the superscript number.
/// </summary>
public static partial class FootnoteMarkers
{
    // From U+E800 to U+EFFF: right after the bookmarks. Not from U+F000, which the text box treats as symbols.
    private const int First = 0xE800;
    public const int Max = 2048;

    public static char Placeholder(int index) => (char)(First + index);

    public static int Index(char character) =>
        character >= First && character < First + Max ? character - First : -1;

    /// <summary>RTF of a file → same RTF with a placeholder in place of each footnote; the texts go into <paramref name="texts"/>.</summary>
    public static string ToPlaceholders(string rtf, List<string> texts)
    {
        var result = new StringBuilder();
        int from = 0;
        foreach (Match m in FootnoteStart().Matches(rtf))
        {
            if (m.Index < from)
                continue; // footnote inside a footnote: already taken with the outer one
            int end = GroupEnd(rtf, m.Index);
            if (end < 0)
                break;
            result.Append(rtf, from, m.Index - from);
            if (texts.Count < Max)
            {
                var inner = rtf[(m.Index + m.Length)..end];
                texts.Add(TextOf(inner));
                result.Append('{').Append(RtfCharacter(Placeholder(texts.Count - 1))).Append('}');
            }
            from = end + 1;
        }
        result.Append(rtf, from, rtf.Length - from);
        // Eleu puts back the automatic reference number (\chftn).
        return ReferenceRegex().Replace(result.ToString(), "");
    }

    /// <summary>RTF of the text box with placeholders → RTF with real footnotes.</summary>
    public static string FromPlaceholders(string rtf, IReadOnlyList<string> texts) =>
        UnicodeRtf().Replace(rtf, m =>
        {
            int index = Index((char)(ushort)short.Parse(m.Groups[1].Value));
            if (index < 0 || index >= texts.Count)
                return m.Value;
            var footnote = new StringBuilder(@"{\super\chftn}{\footnote\pard\plain\fs20{\super\chftn} ");
            var rows = texts[index].Replace("\r", "").Split('\n');
            for (int i = 0; i < rows.Length; i++)
            {
                if (i > 0)
                    footnote.Append(@"\par ");
                Escape(rows[i], footnote);
            }
            return footnote.Append('}').ToString();
        });

    /// <summary>The position of the brace that closes the group opening at <paramref name="opening"/>, or -1.</summary>
    private static int GroupEnd(string rtf, int opening)
    {
        int level = 0;
        for (int i = opening; i < rtf.Length; i++)
        {
            switch (rtf[i])
            {
                case '\\':
                    i++; // escaped character (\{ \} \\)
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

    /// <summary>The plain text of a footnote written in RTF, one paragraph per line.</summary>
    private static string TextOf(string inner)
    {
        var content = RtfReader.Read("{" + @"\rtf1 " + ReferenceRegex().Replace(inner, "") + "}");
        return string.Join("\n", content.Paragraphs.Select(p => p.Text.Trim())).Trim('\n');
    }

    private static string RtfCharacter(char c) => $@"\uc1\u{(short)c}?";

    private static void Escape(string text, StringBuilder rtf)
    {
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
    }

    [GeneratedRegex(@"\{\\(?:\*\\)?footnote(?![a-z])")]
    private static partial Regex FootnoteStart();

    [GeneratedRegex(@"\\chftn(?![a-z]) ?")]
    private static partial Regex ReferenceRegex();

    [GeneratedRegex(@"\\u(-?\d+) ?(?:\\'[0-9a-fA-F]{2}|\?)?")]
    private static partial Regex UnicodeRtf();
}
