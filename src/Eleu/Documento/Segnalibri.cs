using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Eleu.Documento;

/// <summary>
/// Segnalibri. Nei file sono gruppi {\*\bkmkstart nome} (RTF) e w:bookmarkStart (DOCX); il campo di testo
/// di Windows invece li scarta. Per farli passare, durante il caricamento e il salvataggio ogni segnalibro
/// diventa un carattere segnaposto (dell'area Unicode a uso privato) nel punto in cui si trova: il primo
/// segnalibro è il segnaposto 0, il secondo l'1, e così via; i nomi stanno in un elenco a parte.
/// </summary>
public static partial class Segnalibri
{
    private const int Primo = 0xE000;
    public const int Massimo = 2048; // da U+E000 a U+E7FF; dopo vengono le note

    public static char Segnaposto(int indice) => (char)(Primo + indice);

    /// <summary>L'indice del segnalibro di cui il carattere è il segnaposto, o -1.</summary>
    public static int Indice(char carattere) =>
        carattere >= Primo && carattere < Primo + Massimo ? carattere - Primo : -1;

    /// <summary>RTF di un file → stesso RTF con i segnaposto al posto dei segnalibri; i nomi vanno in <paramref name="nomi"/>.</summary>
    public static string InSegnaposto(string rtf, List<string> nomi)
    {
        rtf = FineRtf().Replace(rtf, "");
        return InizioRtf().Replace(rtf, m =>
        {
            if (nomi.Count >= Massimo)
                return "";
            nomi.Add(TestoRtf(m.Groups[1].Value).Trim());
            return "{" + CarattereRtf(Segnaposto(nomi.Count - 1)) + "}";
        });
    }

    /// <summary>RTF del campo di testo con i segnaposto → RTF con i segnalibri veri.</summary>
    public static string DaSegnaposto(string rtf, IReadOnlyList<string> nomi) =>
        UnicodeRtf().Replace(rtf, m =>
        {
            int indice = Indice((char)(ushort)short.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture));
            if (indice < 0 || indice >= nomi.Count)
                return m.Value;
            var nome = NomeRtf(nomi[indice]);
            return $@"{{\*\bkmkstart {nome}}}{{\*\bkmkend {nome}}}";
        });

    /// <summary>
    /// Word accetta nei nomi solo lettere, cifre e trattino basso, al massimo 40, iniziando con una lettera:
    /// gli spazi diventano trattini bassi (e tornano spazi alla lettura).
    /// </summary>
    public static string NomeWord(string nome, ISet<string> giaUsati)
    {
        var pulito = new StringBuilder();
        foreach (var c in nome.Trim())
            pulito.Append(char.IsLetterOrDigit(c) ? c : '_');
        var risultato = pulito.Length > 0 && char.IsLetter(pulito[0]) ? pulito.ToString() : "S_" + pulito;
        if (risultato.Length > 40)
            risultato = risultato[..40];
        var base_ = risultato;
        for (int n = 2; !giaUsati.Add(risultato); n++)
            risultato = $"{base_[..Math.Min(base_.Length, 36)]}_{n}";
        return risultato;
    }

    public static string NomeDaWord(string nome) => nome.Replace('_', ' ').Trim();

    /// <summary>I segnalibri nascosti di Word (per esempio _GoBack, _Toc...) non sono dell'utente.</summary>
    public static bool Nascosto(string nome) => nome.StartsWith('_');

    private static string CarattereRtf(char c) => $@"\uc1\u{(short)c}?";

    private static string NomeRtf(string nome)
    {
        var rtf = new StringBuilder();
        foreach (var c in nome)
        {
            if (c is '\\' or '{' or '}')
                rtf.Append('\\').Append(c);
            else if (c > 127)
                rtf.Append(CarattereRtf(c));
            else
                rtf.Append(c);
        }
        return rtf.ToString();
    }

    /// <summary>Il testo di un nome scritto in RTF: \'xx e \uN decodificati, il resto com'è.</summary>
    private static string TestoRtf(string rtf) =>
        CodiceRtf().Replace(SaltoUnicode().Replace(rtf, ""), m => m.Groups[1].Success
            ? ((char)byte.Parse(m.Groups[1].Value, NumberStyles.HexNumber)).ToString()
            : m.Groups[2].Success
                ? ((char)(ushort)short.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture)).ToString()
                : m.Groups[3].Value);

    [GeneratedRegex(@"\{\\\*\\bkmkstart ?([^}]*)\}")]
    private static partial Regex InizioRtf();

    [GeneratedRegex(@"\{\\\*\\bkmkend ?[^}]*\}")]
    private static partial Regex FineRtf();

    // \uN seguito dal carattere di riserva (? oppure \'xx), che va tolto con lui.
    [GeneratedRegex(@"\\u(-?\d+) ?(?:\\'[0-9a-fA-F]{2}|\?)?")]
    private static partial Regex UnicodeRtf();

    [GeneratedRegex(@"\\'([0-9a-fA-F]{2})|\\u(-?\d+)\??|\\([\\{}])")]
    private static partial Regex CodiceRtf();

    [GeneratedRegex(@"\\uc\d+ ?")]
    private static partial Regex SaltoUnicode();
}
