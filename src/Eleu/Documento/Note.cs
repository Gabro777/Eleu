using System.Text;
using System.Text.RegularExpressions;

namespace Eleu.Documento;

/// <summary>
/// Note a piè di pagina. Nei file sono {\footnote ...} (RTF) e w:footnoteReference (DOCX); il campo di testo
/// di Windows non le conosce. Come per i <see cref="Segnalibri"/>, durante caricamento e salvataggio ogni nota
/// è un carattere segnaposto (area Unicode a uso privato, dopo quella dei segnalibri) nel punto del richiamo;
/// i testi delle note stanno in un elenco a parte. Nel documento aperto il richiamo è il numero in apice.
/// </summary>
public static partial class Note
{
    // Da U+E800 a U+EFFF: subito dopo i segnalibri. Non da U+F000, che il campo di testo tratta come simboli.
    private const int Primo = 0xE800;
    public const int Massimo = 2048;

    public static char Segnaposto(int indice) => (char)(Primo + indice);

    public static int Indice(char carattere) =>
        carattere >= Primo && carattere < Primo + Massimo ? carattere - Primo : -1;

    /// <summary>RTF di un file → stesso RTF con un segnaposto al posto di ogni nota; i testi vanno in <paramref name="testi"/>.</summary>
    public static string InSegnaposto(string rtf, List<string> testi)
    {
        var risultato = new StringBuilder();
        int da = 0;
        foreach (Match m in InizioNota().Matches(rtf))
        {
            if (m.Index < da)
                continue; // nota dentro una nota: già presa con quella esterna
            int fine = FineGruppo(rtf, m.Index);
            if (fine < 0)
                break;
            risultato.Append(rtf, da, m.Index - da);
            if (testi.Count < Massimo)
            {
                var interno = rtf[(m.Index + m.Length)..fine];
                testi.Add(TestoDi(interno));
                risultato.Append('{').Append(CarattereRtf(Segnaposto(testi.Count - 1))).Append('}');
            }
            da = fine + 1;
        }
        risultato.Append(rtf, da, rtf.Length - da);
        // Il numero automatico del richiamo (\chftn) lo rimette Eleu.
        return Richiamo().Replace(risultato.ToString(), "");
    }

    /// <summary>RTF del campo di testo con i segnaposto → RTF con le note vere.</summary>
    public static string DaSegnaposto(string rtf, IReadOnlyList<string> testi) =>
        UnicodeRtf().Replace(rtf, m =>
        {
            int indice = Indice((char)(ushort)short.Parse(m.Groups[1].Value));
            if (indice < 0 || indice >= testi.Count)
                return m.Value;
            var nota = new StringBuilder(@"{\super\chftn}{\footnote\pard\plain\fs20{\super\chftn} ");
            var righe = testi[indice].Replace("\r", "").Split('\n');
            for (int i = 0; i < righe.Length; i++)
            {
                if (i > 0)
                    nota.Append(@"\par ");
                Escapa(righe[i], nota);
            }
            return nota.Append('}').ToString();
        });

    /// <summary>La posizione della graffa che chiude il gruppo che si apre in <paramref name="apertura"/>, o -1.</summary>
    private static int FineGruppo(string rtf, int apertura)
    {
        int livello = 0;
        for (int i = apertura; i < rtf.Length; i++)
        {
            switch (rtf[i])
            {
                case '\\':
                    i++; // carattere protetto (\{ \} \\)
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

    /// <summary>Il testo semplice di una nota scritta in RTF, un paragrafo per riga.</summary>
    private static string TestoDi(string interno)
    {
        var contenuto = RtfLettore.Leggi("{" + @"\rtf1 " + Richiamo().Replace(interno, "") + "}");
        return string.Join("\n", contenuto.Paragrafi.Select(p => p.Testo.Trim())).Trim('\n');
    }

    private static string CarattereRtf(char c) => $@"\uc1\u{(short)c}?";

    private static void Escapa(string testo, StringBuilder rtf)
    {
        foreach (var c in testo)
        {
            if (c is '\\' or '{' or '}')
                rtf.Append('\\').Append(c);
            else if (c == '\t')
                rtf.Append(@"\tab ");
            else if (c > 127)
                rtf.Append(CarattereRtf(c));
            else
                rtf.Append(c);
        }
    }

    [GeneratedRegex(@"\{\\(?:\*\\)?footnote(?![a-z])")]
    private static partial Regex InizioNota();

    [GeneratedRegex(@"\\chftn(?![a-z]) ?")]
    private static partial Regex Richiamo();

    [GeneratedRegex(@"\\u(-?\d+) ?(?:\\'[0-9a-fA-F]{2}|\?)?")]
    private static partial Regex UnicodeRtf();
}
