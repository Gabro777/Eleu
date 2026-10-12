using System.Text.RegularExpressions;

namespace Eleu.Importa;

/// <summary>
/// Trasforma le righe estratte da un PDF o dall'OCR (spezzate come sulla pagina) in paragrafi leggibili:
/// toglie numeri di pagina, intestazioni e piè di pagina ripetuti, ricuce le parole divise dal trattino
/// e unisce le righe dello stesso paragrafo. Tutto automatico, senza domande all'utente.
/// </summary>
public static partial class PuliziaTesto
{
    /// <summary>Righe in cima e in fondo alla pagina in cui cercare intestazioni e piè di pagina.</summary>
    private const int RigheDiBordo = 2;

    public static List<string> Paragrafi(IReadOnlyList<IReadOnlyList<string>> pagine)
    {
        var pulite = TogliBordiRipetuti(pagine
            .Select(p => p.Select(r => SpaziMultipli().Replace(r, " ").Trim()).ToList())
            .ToList());
        return UnisciRighe(pulite.SelectMany(p => p).ToList());
    }

    private static List<List<string>> TogliBordiRipetuti(List<List<string>> pagine)
    {
        // Una riga di bordo (cifre a parte) che si ripete in almeno metà delle pagine è un'intestazione o un piè di pagina.
        var conteggio = new Dictionary<string, int>();
        foreach (var pagina in pagine)
            foreach (var chiave in RigheDiBordoNonVuote(pagina).Select(Normalizza).Distinct())
                conteggio[chiave] = conteggio.GetValueOrDefault(chiave) + 1;

        int soglia = Math.Max(2, (pagine.Count + 1) / 2);
        var ripetute = conteggio.Where(c => c.Value >= soglia && c.Key.Length > 0).Select(c => c.Key).ToHashSet();

        return pagine.Select(pagina =>
        {
            var bordo = RigheDiBordoNonVuote(pagina).ToHashSet();
            return pagina
                .Where(r => !(bordo.Contains(r) && (NumeroDiPagina().IsMatch(r) || ripetute.Contains(Normalizza(r)))))
                .ToList();
        }).ToList();
    }

    private static IEnumerable<string> RigheDiBordoNonVuote(List<string> pagina)
    {
        var piene = pagina.Where(r => r.Length > 0).ToList();
        return piene.Take(RigheDiBordo).Concat(piene.TakeLast(RigheDiBordo));
    }

    private static string Normalizza(string riga) => Cifre().Replace(riga.ToLowerInvariant(), "#");

    private static List<string> UnisciRighe(List<string> righe)
    {
        var lunghezze = righe.Where(r => r.Length > 0).Select(r => r.Length).Order().ToList();
        int tipica = lunghezze.Count == 0 ? 0 : lunghezze[lunghezze.Count * 3 / 4];

        var paragrafi = new List<string>();
        var corrente = "";
        for (int i = 0; i < righe.Count; i++)
        {
            var riga = righe[i];
            if (riga.Length == 0)
            {
                Chiudi();
                continue;
            }

            if (corrente.Length == 0)
                corrente = riga;
            else if (corrente.EndsWith('-') && corrente.Length > 1 && char.IsLetter(corrente[^2]) && char.IsLower(riga[0]))
                corrente = corrente[..^1] + riga; // parola divisa dal trattino a fine riga
            else
                corrente += " " + riga;

            // Riga corta che chiude una frase: fine del paragrafo.
            bool finisceFrase = riga.Length > 0 && ".!?:»\"”".Contains(riga[^1]);
            if (finisceFrase && riga.Length < tipica * 0.8)
                Chiudi();
        }
        Chiudi();
        return paragrafi;

        void Chiudi()
        {
            if (corrente.Length > 0)
                paragrafi.Add(corrente);
            corrente = "";
        }
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex SpaziMultipli();

    [GeneratedRegex(@"\d+")]
    private static partial Regex Cifre();

    // "12", "- 12 -", "Pag. 3", "pagina 4 di 20", "5/20"
    [GeneratedRegex(@"^\W*(?:pag(?:ina)?\.?\s*)?\d{1,4}(?:\s*(?:di|/)\s*\d{1,4})?\W*$", RegexOptions.IgnoreCase)]
    private static partial Regex NumeroDiPagina();
}
