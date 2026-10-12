using System.Text;
using System.Text.RegularExpressions;

namespace Eleu.Documento;

/// <param name="CaratteriJolly">"?" sta per un carattere qualsiasi, "*" per più caratteri (anche nessuno), "#" per una cifra.</param>
public sealed record OpzioniRicerca(bool MaiuscoleMinuscole = false, bool ParolaIntera = false, bool CaratteriJolly = false)
{
    public StringComparison Confronto =>
        // Ordinal: la lunghezza della corrispondenza è sempre uguale a quella del testo cercato.
        MaiuscoleMinuscole ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
}

public static class Ricerca
{
    /// <summary>
    /// Cerca <paramref name="cerca"/> a partire da <paramref name="da"/>: in avanti trova la prima occorrenza
    /// che inizia da lì in poi, all'indietro l'ultima che inizia prima. Restituisce -1 se non c'è.
    /// </summary>
    public static int Trova(string testo, string cerca, int da, bool avanti, OpzioniRicerca opzioni) =>
        TrovaCorrispondenza(testo, cerca, da, avanti, opzioni)?.Inizio ?? -1;

    /// <summary>Come <see cref="Trova"/>, con anche la lunghezza del testo trovato (con i jolly può cambiare).</summary>
    public static (int Inizio, int Lunghezza)? TrovaCorrispondenza(string testo, string cerca, int da, bool avanti, OpzioniRicerca opzioni)
    {
        if (cerca.Length == 0 || testo.Length == 0)
            return null;
        if (opzioni.CaratteriJolly)
            return TrovaConJolly(testo, cerca, da, avanti, opzioni);

        if (avanti)
        {
            for (int i = Math.Clamp(da, 0, testo.Length); i <= testo.Length - cerca.Length;)
            {
                int trovato = testo.IndexOf(cerca, i, opzioni.Confronto);
                if (trovato < 0)
                    return null;
                if (!opzioni.ParolaIntera || ParolaIntera(testo, trovato, cerca.Length))
                    return (trovato, cerca.Length);
                i = trovato + 1;
            }
            return null;
        }

        for (int fineMassima = Math.Min(testo.Length - 1, da + cerca.Length - 2); fineMassima >= cerca.Length - 1;)
        {
            int trovato = testo.LastIndexOf(cerca, fineMassima, fineMassima + 1, opzioni.Confronto);
            if (trovato < 0)
                return null;
            if (!opzioni.ParolaIntera || ParolaIntera(testo, trovato, cerca.Length))
                return (trovato, cerca.Length);
            fineMassima = trovato + cerca.Length - 2;
        }
        return null;
    }

    /// <summary>Tutte le occorrenze, senza sovrapposizioni, in ordine.</summary>
    public static List<int> TrovaTutte(string testo, string cerca, OpzioniRicerca opzioni) =>
        TrovaTutteCorrispondenze(testo, cerca, opzioni).Select(c => c.Inizio).ToList();

    public static List<(int Inizio, int Lunghezza)> TrovaTutteCorrispondenze(string testo, string cerca, OpzioniRicerca opzioni)
    {
        var risultati = new List<(int, int)>();
        int da = 0;
        while (TrovaCorrispondenza(testo, cerca, da, true, opzioni) is var (inizio, lunghezza))
        {
            risultati.Add((inizio, lunghezza));
            da = inizio + Math.Max(1, lunghezza);
        }
        return risultati;
    }

    public static bool Corrisponde(string selezione, string cerca, OpzioniRicerca opzioni)
    {
        if (cerca.Length == 0 || selezione.Length == 0)
            return false;
        if (!opzioni.CaratteriJolly)
            return string.Equals(selezione, cerca, opzioni.Confronto);
        var tutto = TrovaConJolly(selezione, cerca, 0, true, opzioni with { ParolaIntera = false });
        return tutto is (0, var lunghezza) && lunghezza == selezione.Length;
    }

    private static (int, int)? TrovaConJolly(string testo, string cerca, int da, bool avanti, OpzioniRicerca opzioni)
    {
        var espressione = new Regex(Espressione(cerca, opzioni.ParolaIntera),
            opzioni.MaiuscoleMinuscole ? RegexOptions.CultureInvariant : RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        da = Math.Clamp(da, 0, testo.Length);
        if (avanti)
        {
            var m = espressione.Match(testo, da);
            return m.Success && m.Length > 0 ? (m.Index, m.Length) : null;
        }
        Match? ultima = null;
        for (var m = espressione.Match(testo); m.Success && m.Index < da; m = m.NextMatch())
            if (m.Length > 0)
                ultima = m;
        return ultima is null ? null : (ultima.Index, ultima.Length);
    }

    /// <summary>Il testo cercato con i jolly come espressione regolare; "*" prende il meno possibile e non va a capo.</summary>
    public static string Espressione(string cerca, bool parolaIntera)
    {
        var espressione = new StringBuilder();
        if (parolaIntera)
            espressione.Append(@"(?<![\p{L}\p{N}])");
        for (int i = 0; i < cerca.Length; i++)
            espressione.Append(cerca[i] switch
            {
                '?' => @"[^\n\a]",
                // In fondo o in cima "*" completa la parola ("cas*" trova "casa"); in mezzo prende il meno possibile.
                '*' when i == cerca.Length - 1 || i == 0 => @"[\p{L}\p{N}]*",
                '*' => @"[^\n\a]*?",
                '#' => @"\d",
                var c => Regex.Escape(c.ToString()),
            });
        if (parolaIntera)
            espressione.Append(@"(?![\p{L}\p{N}])");
        return espressione.ToString();
    }

    private static bool ParolaIntera(string testo, int inizio, int lunghezza)
    {
        bool primaOk = inizio == 0 || !char.IsLetterOrDigit(testo[inizio - 1]);
        int fine = inizio + lunghezza;
        bool dopoOk = fine >= testo.Length || !char.IsLetterOrDigit(testo[fine]);
        return primaOk && dopoOk;
    }
}
