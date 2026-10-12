using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Eleu.Documento;

/// <summary>Le proprietà del documento: le leggono Word, i lettori di EPUB e le biblioteche digitali.</summary>
public sealed record Metadati(string Titolo = "", string Autore = "", string Oggetto = "", string ParoleChiave = "", DateTime? Creato = null)
{
    public bool Vuoti => Titolo.Length == 0 && Autore.Length == 0 && Oggetto.Length == 0 && ParoleChiave.Length == 0 && Creato is null;

    /// <summary>Le proprietà dal gruppo {\info} di un RTF.</summary>
    public static Metadati DaRtf(string rtf)
    {
        string Campo(string nome) => Regex.Match(rtf, $@"\{{\\{nome} ((?:[^{{}}\\]|\\.)*)\}}") is { Success: true } m ? TestoRtf(m.Groups[1].Value) : "";
        DateTime? creato = null;
        var data = Regex.Match(rtf, @"\{\\creatim\\yr(\d+)\\mo(\d+)\\dy(\d+)(?:\\hr(\d+))?(?:\\min(\d+))?");
        if (data.Success)
        {
            try
            {
                creato = new DateTime(int.Parse(data.Groups[1].Value), int.Parse(data.Groups[2].Value), int.Parse(data.Groups[3].Value),
                    data.Groups[4].Success ? int.Parse(data.Groups[4].Value) : 0, data.Groups[5].Success ? int.Parse(data.Groups[5].Value) : 0, 0);
            }
            catch (ArgumentOutOfRangeException)
            {
            }
        }
        return new Metadati(Campo("title"), Campo("author"), Campo("subject"), Campo("keywords"), creato);
    }

    /// <summary>L'RTF con il gruppo {\info} di queste proprietà (al posto di quello che c'era), subito dopo le tabelle iniziali.</summary>
    public string InRtf(string rtf)
    {
        rtf = Regex.Replace(rtf, @"\{\\info(?:[^{}]|\{[^{}]*\})*\}", "");
        if (Vuoti)
            return rtf;
        var info = new StringBuilder(@"{\info");
        void Aggiungi(string nome, string valore)
        {
            if (valore.Length == 0)
                return;
            info.Append(@"{\").Append(nome).Append(' ');
            foreach (var c in valore)
                info.Append(c switch
                {
                    '\\' or '{' or '}' => "\\" + c,
                    > (char)127 => $@"\u{(short)c}?",
                    < ' ' => " ",
                    _ => c.ToString(),
                });
            info.Append('}');
        }
        Aggiungi("title", Titolo);
        Aggiungi("author", Autore);
        Aggiungi("subject", Oggetto);
        Aggiungi("keywords", ParoleChiave);
        if (Creato is DateTime d)
            info.Append(CultureInfo.InvariantCulture, $@"{{\creatim\yr{d.Year}\mo{d.Month}\dy{d.Day}\hr{d.Hour}\min{d.Minute}}}");
        info.Append('}');
        // Dopo il gruppo {\*\generator} di RichEdit, o dopo le tabelle dei caratteri e dei colori.
        int dopo = FineGruppo(rtf, rtf.IndexOf(@"{\*\generator", StringComparison.Ordinal));
        if (dopo < 0)
            dopo = FineGruppo(rtf, rtf.IndexOf(@"{\colortbl", StringComparison.Ordinal));
        if (dopo < 0)
            dopo = FineGruppo(rtf, rtf.IndexOf(@"{\fonttbl", StringComparison.Ordinal));
        return dopo < 0 ? rtf : rtf.Insert(dopo + 1, info.ToString());
    }

    private static int FineGruppo(string rtf, int apertura)
    {
        if (apertura < 0)
            return -1;
        int livello = 0;
        for (int i = apertura; i < rtf.Length; i++)
        {
            switch (rtf[i])
            {
                case '\\':
                    i++;
                    break;
                case '{':
                    livello++;
                    break;
                case '}':
                    if (--livello == 0)
                        return i;
                    break;
            }
        }
        return -1;
    }

    private static string TestoRtf(string rtf) =>
        Regex.Replace(Regex.Replace(rtf, @"\\uc\d+ ?", ""), @"\\'([0-9a-fA-F]{2})|\\u(-?\d+)\??|\\([\\{}])", m => m.Groups[1].Success
            ? ((char)byte.Parse(m.Groups[1].Value, NumberStyles.HexNumber)).ToString()
            : m.Groups[2].Success
                ? ((char)(ushort)short.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture)).ToString()
                : m.Groups[3].Value).Trim();
}
