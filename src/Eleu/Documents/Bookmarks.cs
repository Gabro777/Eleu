using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Eleu.Documents;

/// <summary>
/// Bookmarks. In files they are {\*\bkmkstart name} groups (RTF) and w:bookmarkStart (DOCX); the Windows
/// text box drops them instead. To get them through, during loading and saving each bookmark
/// becomes a placeholder character (from the Unicode private use area) at its position: the first
/// bookmark is placeholder 0, the second 1, and so on; the names are kept in a separate list.
/// </summary>
public static partial class Bookmarks
{
    private const int First = 0xE000;
    public const int Max = 2048; // from U+E000 to U+E7FF; footnotes come after

    public static char Placeholder(int index) => (char)(First + index);

    /// <summary>The index of the bookmark the character is the placeholder of, or -1.</summary>
    public static int Index(char character) =>
        character >= First && character < First + Max ? character - First : -1;

    /// <summary>RTF of a file → same RTF with placeholders instead of bookmarks; the names go into <paramref name="names"/>.</summary>
    public static string ToPlaceholders(string rtf, List<string> names)
    {
        rtf = RtfEnd().Replace(rtf, "");
        return RtfStart().Replace(rtf, m =>
        {
            if (names.Count >= Max)
                return "";
            names.Add(RtfText(m.Groups[1].Value).Trim());
            return "{" + RtfCharacter(Placeholder(names.Count - 1)) + "}";
        });
    }

    /// <summary>RTF of the text box with placeholders → RTF with real bookmarks.</summary>
    public static string FromPlaceholders(string rtf, IReadOnlyList<string> names) =>
        UnicodeRtf().Replace(rtf, m =>
        {
            int index = Index((char)(ushort)short.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture));
            if (index < 0 || index >= names.Count)
                return m.Value;
            var name = RtfName(names[index]);
            return $@"{{\*\bkmkstart {name}}}{{\*\bkmkend {name}}}";
        });

    /// <summary>
    /// Word accepts only letters, digits and underscores in names, at most 40, starting with a letter:
    /// spaces become underscores (and turn back into spaces when reading).
    /// </summary>
    public static string WordName(string name, ISet<string> alreadyUsed)
    {
        var cleaned = new StringBuilder();
        foreach (var c in name.Trim())
            cleaned.Append(char.IsLetterOrDigit(c) ? c : '_');
        var result = cleaned.Length > 0 && char.IsLetter(cleaned[0]) ? cleaned.ToString() : "S_" + cleaned;
        if (result.Length > 40)
            result = result[..40];
        var base_ = result;
        for (int n = 2; !alreadyUsed.Add(result); n++)
            result = $"{base_[..Math.Min(base_.Length, 36)]}_{n}";
        return result;
    }

    public static string NameFromWord(string name) => name.Replace('_', ' ').Trim();

    /// <summary>Word's hidden bookmarks (for example _GoBack, _Toc...) do not belong to the user.</summary>
    public static bool Hidden(string name) => name.StartsWith('_');

    private static string RtfCharacter(char c) => $@"\uc1\u{(short)c}?";

    private static string RtfName(string name)
    {
        var rtf = new StringBuilder();
        foreach (var c in name)
        {
            if (c is '\\' or '{' or '}')
                rtf.Append('\\').Append(c);
            else if (c > 127)
                rtf.Append(RtfCharacter(c));
            else
                rtf.Append(c);
        }
        return rtf.ToString();
    }

    /// <summary>The text of a name written in RTF: \'xx and \uN decoded, the rest as it is.</summary>
    private static string RtfText(string rtf) =>
        RtfCode().Replace(UnicodeSkip().Replace(rtf, ""), m => m.Groups[1].Success
            ? ((char)byte.Parse(m.Groups[1].Value, NumberStyles.HexNumber)).ToString()
            : m.Groups[2].Success
                ? ((char)(ushort)short.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture)).ToString()
                : m.Groups[3].Value);

    [GeneratedRegex(@"\{\\\*\\bkmkstart ?([^}]*)\}")]
    private static partial Regex RtfStart();

    [GeneratedRegex(@"\{\\\*\\bkmkend ?[^}]*\}")]
    private static partial Regex RtfEnd();

    // \uN followed by the fallback character (? or \'xx), which is removed with it.
    [GeneratedRegex(@"\\u(-?\d+) ?(?:\\'[0-9a-fA-F]{2}|\?)?")]
    private static partial Regex UnicodeRtf();

    [GeneratedRegex(@"\\'([0-9a-fA-F]{2})|\\u(-?\d+)\??|\\([\\{}])")]
    private static partial Regex RtfCode();

    [GeneratedRegex(@"\\uc\d+ ?")]
    private static partial Regex UnicodeSkip();
}
