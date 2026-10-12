using System.Globalization;

namespace Eleu.Documento;

/// <summary>
/// Una tabella come griglia: righe → celle → paragrafi, più la larghezza di ogni colonna in punti (0 = automatica).
/// Serve per cambiare righe e colonne e per leggere la tabella riga per riga.
/// </summary>
public sealed class Griglia
{
    public List<List<List<Paragrafo>>> Righe { get; } = [];

    public List<float> Larghezze { get; } = [];

    public int Colonne => Righe.Count == 0 ? 0 : Righe.Max(r => r.Count);

    /// <summary>La griglia di una tabella del documento (i paragrafi con <see cref="Paragrafo.Cella"/> di quella tabella).</summary>
    public static Griglia Da(IEnumerable<Paragrafo> paragrafi, int tabella = 0)
    {
        var griglia = new Griglia();
        var celle = paragrafi.Where(p => p.Cella?.Tabella == tabella).ToList();
        foreach (var riga in celle.GroupBy(p => p.Cella!.Riga).OrderBy(g => g.Key))
            griglia.Righe.Add(riga.GroupBy(p => p.Cella!.Colonna).OrderBy(g => g.Key).Select(c => c.ToList()).ToList());
        for (int c = 0; c < griglia.Colonne; c++)
            griglia.Larghezze.Add(celle.Select(p => p.Cella!).Where(x => x.Colonna == c).Select(x => x.Larghezza).FirstOrDefault(l => l > 0));
        return griglia;
    }

    /// <summary>I paragrafi della griglia con la loro posizione di cella, pronti da scrivere come tabella numero <paramref name="tabella"/>.</summary>
    public List<Paragrafo> Paragrafi(int tabella = 0)
    {
        var risultato = new List<Paragrafo>();
        for (int r = 0; r < Righe.Count; r++)
            for (int c = 0; c < Righe[r].Count; c++)
            {
                if (Righe[r][c].Count == 0)
                    Righe[r][c].Add(new Paragrafo());
                foreach (var paragrafo in Righe[r][c])
                {
                    paragrafo.Cella = new PosizioneCella(tabella, r, c, c < Larghezze.Count ? Larghezze[c] : 0);
                    risultato.Add(paragrafo);
                }
            }
        return risultato;
    }

    public static string Testo(List<Paragrafo> cella) => string.Join(" ", cella.Select(p => p.Testo.Trim())).Trim();

    public void InserisciRiga(int indice) =>
        Righe.Insert(Math.Clamp(indice, 0, Righe.Count), Enumerable.Range(0, Math.Max(1, Colonne)).Select(_ => new List<Paragrafo> { new() }).ToList());

    public void InserisciColonna(int indice)
    {
        foreach (var riga in Righe)
            riga.Insert(Math.Clamp(indice, 0, riga.Count), [new Paragrafo()]);
        // La colonna nuova prende la larghezza della vicina.
        int vicina = Math.Clamp(indice > 0 ? indice - 1 : 0, 0, Math.Max(0, Larghezze.Count - 1));
        Larghezze.Insert(Math.Clamp(indice, 0, Larghezze.Count), Larghezze.Count > 0 ? Larghezze[vicina] : 0);
    }

    public void EliminaColonna(int indice)
    {
        foreach (var riga in Righe.Where(r => indice < r.Count && r.Count > 1))
            riga.RemoveAt(indice);
        if (indice < Larghezze.Count && Larghezze.Count > 1)
            Larghezze.RemoveAt(indice);
    }

    /// <summary>
    /// Ordina le righe secondo il testo della colonna. I numeri si confrontano come numeri ("9" prima di "10"),
    /// il resto in ordine alfabetico italiano. Con <paramref name="intestazione"/> la prima riga resta in cima.
    /// </summary>
    public void Ordina(int colonna, bool crescente, bool intestazione)
    {
        int prima = intestazione && Righe.Count > 1 ? 1 : 0;
        var daOrdinare = Righe.Skip(prima).ToList();
        var confronto = StringComparer.Create(CultureInfo.GetCultureInfo("it-IT"), ignoreCase: true);
        int Confronta(List<List<Paragrafo>> a, List<List<Paragrafo>> b)
        {
            string ta = colonna < a.Count ? Testo(a[colonna]) : "", tb = colonna < b.Count ? Testo(b[colonna]) : "";
            bool na = Numero(ta, out var va), nb = Numero(tb, out var vb);
            int esito = na && nb ? va.CompareTo(vb) : na ? -1 : nb ? 1 : confronto.Compare(ta, tb);
            return crescente ? esito : -esito;
        }
        // Ordinamento stabile: a parità resta l'ordine di prima.
        var ordinate = daOrdinare.Select((r, i) => (r, i)).OrderBy(x => x.r, Comparer<List<List<Paragrafo>>>.Create(Confronta))
            .ThenBy(x => x.i).Select(x => x.r).ToList();
        Righe.RemoveRange(prima, daOrdinare.Count);
        Righe.AddRange(ordinate);
    }

    private static bool Numero(string testo, out decimal valore) =>
        decimal.TryParse(testo.Replace("€", "").Replace("%", "").Trim(), NumberStyles.Number, CultureInfo.GetCultureInfo("it-IT"), out valore);

    /// <summary>
    /// La tabella da leggere di seguito (Braille, audiolibro): la prima riga elenca le colonne, ogni riga dopo
    /// diventa un paragrafo "Nome: Anna; Età: 40". Le celle vuote si saltano.
    /// </summary>
    public List<Paragrafo> RigheLeggibili()
    {
        var risultato = new List<Paragrafo>();
        if (Righe.Count == 0)
            return risultato;
        var intestazione = Righe[0].Select(Testo).ToList();
        // Ogni riga letta tiene la pagina a stampa della sua prima cella (serve al Braille).
        static int Pagina(List<List<Paragrafo>> riga) => riga.SelectMany(c => c).Select(p => p.PaginaStampa).FirstOrDefault();
        void Aggiungi(string testo, int pagina)
        {
            var p = new Paragrafo { PaginaStampa = pagina };
            p.Aggiungi(new Segmento(testo));
            risultato.Add(p);
        }
        Aggiungi($"Tabella: {string.Join("; ", intestazione.Where(t => t.Length > 0))}", Pagina(Righe[0]));
        foreach (var riga in Righe.Skip(1))
        {
            var parti = riga.Select((cella, c) => (Nome: c < intestazione.Count ? intestazione[c] : "", Valore: Testo(cella)))
                .Where(x => x.Valore.Length > 0)
                .Select(x => x.Nome.Length > 0 ? $"{x.Nome}: {x.Valore}" : x.Valore);
            Aggiungi(string.Join("; ", parti), Pagina(riga));
        }
        Aggiungi("Fine tabella", Pagina(Righe[^1]));
        return risultato;
    }

    /// <summary>Il documento con ogni tabella al posto delle sue celle in forma di <see cref="RigheLeggibili"/>.</summary>
    public static ContenutoDocumento Leggibile(ContenutoDocumento contenuto)
    {
        var risultato = new ContenutoDocumento { Pagina = contenuto.Pagina };
        risultato.Note.AddRange(contenuto.Note);
        var paragrafi = contenuto.Paragrafi;
        for (int i = 0; i < paragrafi.Count; i++)
        {
            if (paragrafi[i].Cella is not { } cella)
            {
                risultato.Paragrafi.Add(paragrafi[i]);
                continue;
            }
            int fine = i;
            while (fine < paragrafi.Count && paragrafi[fine].Cella?.Tabella == cella.Tabella)
                fine++;
            risultato.Paragrafi.AddRange(Da(paragrafi.Skip(i).Take(fine - i), cella.Tabella).RigheLeggibili());
            i = fine - 1;
        }
        return risultato;
    }
}
