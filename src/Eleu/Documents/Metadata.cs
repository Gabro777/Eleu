using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Eleu.Documents;

/// <summary>The document properties: read by Word, EPUB readers and digital libraries.</summary>
public sealed record Metadata(string Title = "", string Author = "", string Subject = "", string Keywords = "", DateTime? Created = null)
{
    public bool Empty => Title.Length == 0 && Author.Length == 0 && Subject.Length == 0 && Keywords.Length == 0 && Created is null;

    /// <summary>The properties from the {\info} group of an RTF.</summary>
    public static Metadata FromRtf(string rtf)
    {
        string Field(string name) => Regex.Match(rtf, $@"\{{\\{name} ((?:[^{{}}\\]|\\.)*)\}}") is { Success: true } m ? RtfText(m.Groups[1].Value) : "";
        DateTime? created = null;
        var date = Regex.Match(rtf, @"\{\\creatim\\yr(\d+)\\mo(\d+)\\dy(\d+)(?:\\hr(\d+))?(?:\\min(\d+))?");
        if (date.Success)
        {
            try
            {
                created = new DateTime(int.Parse(date.Groups[1].Value), int.Parse(date.Groups[2].Value), int.Parse(date.Groups[3].Value),
                    date.Groups[4].Success ? int.Parse(date.Groups[4].Value) : 0, date.Groups[5].Success ? int.Parse(date.Groups[5].Value) : 0, 0);
            }
            catch (ArgumentOutOfRangeException)
            {
            }
        }
        return new Metadata(Field("title"), Field("author"), Field("subject"), Field("keywords"), created);
    }

    /// <summary>The RTF with the {\info} group of these properties (replacing the existing one), right after the initial tables.</summary>
    public string ToRtf(string rtf)
    {
        rtf = Regex.Replace(rtf, @"\{\\info(?:[^{}]|\{[^{}]*\})*\}", "");
        if (Empty)
            return rtf;
        var info = new StringBuilder(@"{\info");
        void Add(string name, string @value)
        {
            if (@value.Length == 0)
                return;
            info.Append(@"{\").Append(name).Append(' ');
            foreach (var c in @value)
                info.Append(c switch
                {
                    '\\' or '{' or '}' => "\\" + c,
                    > (char)127 => $@"\u{(short)c}?",
                    < ' ' => " ",
                    _ => c.ToString(),
                });
            info.Append('}');
        }
        Add("title", Title);
        Add("author", Author);
        Add("subject", Subject);
        Add("keywords", Keywords);
        if (Created is DateTime d)
            info.Append(CultureInfo.InvariantCulture, $@"{{\creatim\yr{d.Year}\mo{d.Month}\dy{d.Day}\hr{d.Hour}\min{d.Minute}}}");
        info.Append('}');
        // After RichEdit's {\*\generator} group, or after the font and color tables.
        int after = GroupEnd(rtf, rtf.IndexOf(@"{\*\generator", StringComparison.Ordinal));
        if (after < 0)
            after = GroupEnd(rtf, rtf.IndexOf(@"{\colortbl", StringComparison.Ordinal));
        if (after < 0)
            after = GroupEnd(rtf, rtf.IndexOf(@"{\fonttbl", StringComparison.Ordinal));
        return after < 0 ? rtf : rtf.Insert(after + 1, info.ToString());
    }

    private static int GroupEnd(string rtf, int opening)
    {
        if (opening < 0)
            return -1;
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

    private static string RtfText(string rtf) =>
        Regex.Replace(Regex.Replace(rtf, @"\\uc\d+ ?", ""), @"\\'([0-9a-fA-F]{2})|\\u(-?\d+)\??|\\([\\{}])", m => m.Groups[1].Success
            ? ((char)byte.Parse(m.Groups[1].Value, NumberStyles.HexNumber)).ToString()
            : m.Groups[2].Success
                ? ((char)(ushort)short.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture)).ToString()
                : m.Groups[3].Value).Trim();
}
