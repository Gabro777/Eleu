namespace Eleu.Documento;

public enum TipoDifferenza { Cambiato, SoloQui, SoloNellAltro }

/// <summary>
/// Una differenza tra questo documento e l'altro. <see cref="Qui"/> è il numero (da 0) del paragrafo di questo
/// documento a cui si riferisce: per ciò che c'è solo nell'altro, il paragrafo dopo cui andrebbe (-1 = in cima).
/// </summary>
public sealed record Differenza(TipoDifferenza Tipo, int Qui, int Altro, string TestoQui, string TestoAltro)
{
    /// <summary>Per un paragrafo cambiato: le parole tolte e aggiunte, da leggere; altrimenti il testo.</summary>
    public string Descrizione => Tipo switch
    {
        TipoDifferenza.Cambiato => $"Paragrafo {Qui + 1} cambiato. {Confronto.ParoleCambiate(TestoQui, TestoAltro)}",
        TipoDifferenza.SoloQui => $"Paragrafo {Qui + 1} solo in questo documento: {Breve(TestoQui)}",
        _ => (Qui < 0 ? "In cima" : $"Dopo il paragrafo {Qui + 1}") + $", solo nell'altro documento: {Breve(TestoAltro)}",
    };

    private static string Breve(string testo) => testo.Length > 120 ? testo[..120] + "..." : testo.Length == 0 ? "(paragrafo vuoto)" : testo;
}

/// <summary>
/// Confronto tra due documenti (Strumenti > Confronta con un altro documento): quali paragrafi sono cambiati,
/// quali ci sono solo in uno dei due. Si usa la sottosequenza comune più lunga, come i programmi di confronto.
/// </summary>
public static class Confronto
{
    /// <summary>Oltre questa dimensione (paragrafi × paragrafi) la parte centrale si confronta riga per riga.</summary>
    private const long MassimoCelle = 4_000_000;

    public static List<Differenza> Differenze(IReadOnlyList<string> qui, IReadOnlyList<string> altro)
    {
        var a = qui.Select(Normalizza).ToList();
        var b = altro.Select(Normalizza).ToList();
        var operazioni = Allinea(a, b);

        // Un gruppo di righe tolte seguito da righe aggiunte: le prime coppie sono paragrafi cambiati.
        var differenze = new List<Differenza>();
        int ultimaQui = -1;
        for (int k = 0; k < operazioni.Count;)
        {
            if (operazioni[k].Tipo == 0)
            {
                ultimaQui = operazioni[k].I;
                k++;
                continue;
            }
            var tolte = new List<int>();
            var aggiunte = new List<int>();
            while (k < operazioni.Count && operazioni[k].Tipo != 0)
            {
                if (operazioni[k].Tipo < 0)
                    tolte.Add(operazioni[k].I);
                else
                    aggiunte.Add(operazioni[k].J);
                k++;
            }
            int coppie = Math.Min(tolte.Count, aggiunte.Count);
            for (int c = 0; c < coppie; c++)
                differenze.Add(new Differenza(TipoDifferenza.Cambiato, tolte[c], aggiunte[c], qui[tolte[c]], altro[aggiunte[c]]));
            foreach (var i in tolte.Skip(coppie))
                differenze.Add(new Differenza(TipoDifferenza.SoloQui, i, -1, qui[i], ""));
            int dopo = tolte.Count > 0 ? tolte[^1] : ultimaQui;
            foreach (var j in aggiunte.Skip(coppie))
                differenze.Add(new Differenza(TipoDifferenza.SoloNellAltro, dopo, j, "", altro[j]));
            if (tolte.Count > 0)
                ultimaQui = tolte[^1];
        }
        return differenze;
    }

    /// <summary>"Tolto: «gatto». Aggiunto: «cane»." per due versioni di un paragrafo.</summary>
    public static string ParoleCambiate(string qui, string altro)
    {
        var a = qui.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var b = altro.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var operazioni = Allinea(a, b);
        var tolte = operazioni.Where(o => o.Tipo < 0).Select(o => a[o.I]).ToList();
        var aggiunte = operazioni.Where(o => o.Tipo > 0).Select(o => b[o.J]).ToList();
        var parti = new List<string>();
        if (tolte.Count > 0)
            parti.Add($"Qui: «{string.Join(" ", tolte)}»");
        if (aggiunte.Count > 0)
            parti.Add($"nell'altro: «{string.Join(" ", aggiunte)}»");
        return parti.Count == 0 ? "Cambiano solo gli spazi." : string.Join("; ", parti) + ".";
    }

    private static string Normalizza(string paragrafo) => string.Join(' ', paragrafo.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>Le operazioni per passare da a a b: 0 = uguale (I in a, J in b), -1 = solo in a, +1 = solo in b.</summary>
    private static List<(int Tipo, int I, int J)> Allinea(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        int inizio = 0;
        while (inizio < a.Count && inizio < b.Count && a[inizio] == b[inizio])
            inizio++;
        int finaleA = a.Count, finaleB = b.Count;
        while (finaleA > inizio && finaleB > inizio && a[finaleA - 1] == b[finaleB - 1])
        {
            finaleA--;
            finaleB--;
        }

        var operazioni = new List<(int, int, int)>();
        for (int i = 0; i < inizio; i++)
            operazioni.Add((0, i, i));

        int n = finaleA - inizio, m = finaleB - inizio;
        if ((long)n * m > MassimoCelle)
        {
            // Troppo grande per la tabella: si confrontano le righe in ordine.
            for (int i = 0; i < n; i++)
                operazioni.Add((-1, inizio + i, -1));
            for (int j = 0; j < m; j++)
                operazioni.Add((1, -1, inizio + j));
        }
        else
        {
            var lcs = new int[n + 1, m + 1];
            for (int i = n - 1; i >= 0; i--)
                for (int j = m - 1; j >= 0; j--)
                    lcs[i, j] = a[inizio + i] == b[inizio + j] ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
            int x = 0, y = 0;
            while (x < n || y < m)
            {
                if (x < n && y < m && a[inizio + x] == b[inizio + y])
                {
                    operazioni.Add((0, inizio + x, inizio + y));
                    x++;
                    y++;
                }
                else if (y < m && (x == n || lcs[x, y + 1] > lcs[x + 1, y]))
                {
                    operazioni.Add((1, -1, inizio + y));
                    y++;
                }
                else
                {
                    operazioni.Add((-1, inizio + x, -1));
                    x++;
                }
            }
        }
        for (int k = 0; k < a.Count - finaleA; k++)
            operazioni.Add((0, finaleA + k, finaleB + k));
        return operazioni;
    }
}
