using System.Text;
using System.Text.RegularExpressions;

namespace Eleu.Voce;

/// <summary>Come la sintesi vocale deve dire una parola o una sigla: "NVDA" → "enne vu di a".</summary>
public sealed record Pronuncia(string Parola, string Come);

/// <summary>
/// Dizionario delle pronunce (Voce > Pronunce): prima di leggere si sostituiscono le parole intere, senza badare
/// a maiuscole e minuscole. Per far tornare la lettura al punto giusto del documento si tiene, per ogni carattere
/// del testo detto, la posizione corrispondente nel testo scritto.
/// </summary>
public static class Pronunce
{
    public static (string Testo, int[] Origine) Applica(string testo, IReadOnlyList<Pronuncia> pronunce)
    {
        var valide = pronunce.Where(p => p.Parola.Trim().Length > 0).ToList();
        if (valide.Count == 0)
            return (testo, Enumerable.Range(0, testo.Length + 1).ToArray());

        // Le più lunghe prima: "ONU" non deve mangiarsi l'inizio di "ONUS".
        var come = valide.GroupBy(p => p.Parola.Trim(), StringComparer.CurrentCultureIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Come, StringComparer.CurrentCultureIgnoreCase);
        var alternative = string.Join("|", come.Keys.OrderByDescending(k => k.Length).Select(Regex.Escape));
        var regola = new Regex($@"(?<![\p{{L}}\p{{N}}])(?:{alternative})(?![\p{{L}}\p{{N}}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        var nuovo = new StringBuilder();
        var origine = new List<int>();
        int da = 0;
        foreach (Match m in regola.Matches(testo))
        {
            for (int i = da; i < m.Index; i++)
            {
                nuovo.Append(testo[i]);
                origine.Add(i);
            }
            var sostituto = come[m.Value];
            nuovo.Append(sostituto);
            origine.AddRange(Enumerable.Repeat(m.Index, sostituto.Length));
            da = m.Index + m.Length;
        }
        for (int i = da; i < testo.Length; i++)
        {
            nuovo.Append(testo[i]);
            origine.Add(i);
        }
        origine.Add(testo.Length);
        return (nuovo.ToString(), origine.ToArray());
    }
}
