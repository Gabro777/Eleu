using System.Text;

namespace Eleu.Correzione;

public static class Parlato
{
    /// <summary>
    /// Un pezzo di testo da leggere con lo screen reader, con spazi e punteggiatura detti a parole
    /// ("l' a" diventa "l apostrofo spazio a", " ," diventa "spazio virgola"): con le impostazioni normali lo
    /// screen reader non legge né gli spazi né molti segni. Lo spazio tra due parole resta uno spazio.
    /// </summary>
    public static string Di(string testo)
    {
        if (testo == " ")
            return "uno spazio";
        var parti = new List<string>();
        var parola = new StringBuilder();
        for (int i = 0; i < testo.Length; i++)
        {
            char c = testo[i];
            if (char.IsLetterOrDigit(c))
            {
                parola.Append(c);
                continue;
            }
            if (parola.Length > 0)
            {
                parti.Add(parola.ToString());
                parola.Clear();
            }
            bool traDueParole = c == ' ' && i > 0 && i < testo.Length - 1
                && char.IsLetterOrDigit(testo[i - 1]) && char.IsLetterOrDigit(testo[i + 1]);
            if (!traDueParole)
                parti.Add(Nome(c));
        }
        if (parola.Length > 0)
            parti.Add(parola.ToString());
        return string.Join(" ", parti);
    }

    private static string Nome(char c) => c switch
    {
        ' ' or '\t' => "spazio",
        ',' => "virgola",
        '.' => "punto",
        ';' => "punto e virgola",
        ':' => "due punti",
        '!' => "punto esclamativo",
        '?' => "punto interrogativo",
        '(' => "parentesi aperta",
        ')' => "parentesi chiusa",
        '\'' or '’' => "apostrofo",
        '-' => "trattino",
        '"' or '«' or '»' or '“' or '”' => "virgolette",
        _ => c.ToString(),
    };
}
