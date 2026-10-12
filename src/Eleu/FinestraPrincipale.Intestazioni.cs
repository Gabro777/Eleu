using System.Text.RegularExpressions;
using Eleu.Documento;

namespace Eleu;

// Righe di intestazione delle tabelle (quelle che si ripetono in cima a ogni pagina e danno il nome alle colonne).
// Il campo di testo di Windows non le conserva: appena riceve la tabella dimentica \trhdr. Eleu le ricorda a parte,
// come i segnalibri: per ogni tabella con intestazione, un intervallo vuoto del Text Object Model all'inizio della
// tabella (si sposta da solo con il testo) e quante righe di intestazione ha.
public sealed partial class FinestraPrincipale
{
    private readonly List<(dynamic Inizio, int Righe)> _intestazioni = [];
    private List<(int Inizio, int Righe)>? _intestazioniInSospeso;

    private void PreparaIntestazioni()
    {
        _testo.HandleDestroyed += (_, _) =>
        {
            _intestazioniInSospeso = _intestazioni.Select(x => ((int)x.Inizio.Start, x.Righe)).ToList();
            _intestazioni.Clear();
        };
        _testo.HandleCreated += (_, _) =>
        {
            if (_intestazioniInSospeso is { } sospese)
                BeginInvoke(() =>
                {
                    foreach (var (inizio, righe) in sospese)
                        ImpostaIntestazione(inizio, righe);
                });
            _intestazioniInSospeso = null;
        };
    }

    /// <summary>Quante righe di intestazione ha ciascuna tabella del testo, nell'ordine.</summary>
    private int[] RigheIntestazione(IReadOnlyList<TabellaNelTesto> tabelle)
    {
        var perInizio = new Dictionary<int, int>();
        foreach (var (inizio, righe) in _intestazioni)
            perInizio[(int)inizio.Start] = righe;
        return tabelle.Select(t => perInizio.TryGetValue(t.Inizio, out int n) ? Math.Min(n, t.Righe.Count) : 0).ToArray();
    }

    private int RigheIntestazione(TabellaNelTesto tabella) =>
        _intestazioni.Where(x => (int)x.Inizio.Start == tabella.Inizio).Select(x => Math.Min(x.Righe, tabella.Righe.Count)).FirstOrDefault();

    /// <summary>La tabella che comincia lì ha <paramref name="righe"/> righe di intestazione (0 = nessuna).</summary>
    private void ImpostaIntestazione(int inizioTabella, int righe)
    {
        _intestazioni.RemoveAll(x => (int)x.Inizio.Start == inizioTabella);
        if (righe > 0 && Tom is { } tom)
            _intestazioni.Add((tom.Range(inizioTabella, inizioTabella), righe));
    }

    /// <summary>Le righe di intestazione di ogni tabella di un modello (le prime righe di fila segnate come intestazione).</summary>
    public static Dictionary<int, int> IntestazioniDelModello(ContenutoDocumento modello) =>
        modello.Paragrafi.Where(p => p.Cella is not null).GroupBy(p => p.Cella!.Tabella).ToDictionary(g => g.Key, g =>
        {
            int n = 0;
            foreach (var riga in g.GroupBy(p => p.Cella!.Riga).OrderBy(r => r.Key))
            {
                if (riga.Key != n || !riga.Any(p => p.Cella!.Intestazione))
                    break;
                n++;
            }
            return n;
        });

    /// <summary>Dopo il caricamento: le intestazioni del modello (o dell'RTF letto) sulle tabelle del testo, nell'ordine.</summary>
    private void IntestazioniDopoIlCaricamento(ContenutoDocumento? modello)
    {
        _intestazioni.Clear();
        if (modello is null)
            return;
        var conteggi = IntestazioniDelModello(modello);
        if (conteggi.Values.All(n => n == 0))
            return;
        var tabelle = StrutturaTesto.Tabelle(_testo.Text);
        for (int i = 0; i < tabelle.Count; i++)
            if (conteggi.TryGetValue(i, out int n) && n > 0)
                ImpostaIntestazione(tabelle[i].Inizio, n);
    }

    /// <summary>Segna nel modello da salvare le celle delle righe di intestazione.</summary>
    private void SegnaIntestazioni(ContenutoDocumento modello)
    {
        if (_intestazioni.Count == 0)
            return;
        var righe = RigheIntestazione(StrutturaTesto.Tabelle(_testo.Text));
        foreach (var p in modello.Paragrafi)
            if (p.Cella is { } cella && cella.Tabella < righe.Length && cella.Riga < righe[cella.Tabella])
                p.Cella = cella with { Intestazione = true };
    }

    /// <summary>
    /// Rimette \trhdr nell'RTF da salvare. Il campo di testo scrive ogni riga di tabella con il suo \trowd, nell'ordine
    /// del testo: la k-esima \trowd è la k-esima riga. Se i conti non tornano l'RTF resta com'è.
    /// </summary>
    private string IntestazioniInRtf(string rtf)
    {
        if (_intestazioni.Count == 0)
            return rtf;
        var tabelle = StrutturaTesto.Tabelle(_testo.Text);
        var righe = RigheIntestazione(tabelle);
        var daSegnare = new HashSet<int>();
        int primaRiga = 0;
        for (int t = 0; t < tabelle.Count; t++)
        {
            for (int r = 0; r < righe[t]; r++)
                daSegnare.Add(primaRiga + r);
            primaRiga += tabelle[t].Righe.Count;
        }
        if (daSegnare.Count == 0 || InizioRigaRtf().Count(rtf) != primaRiga)
            return rtf;
        int k = 0;
        return InizioRigaRtf().Replace(rtf, m => daSegnare.Contains(k++) ? m.Value + @"\trhdr" : m.Value);
    }

    /// <summary>Inserisci > Tabelle > Riga di intestazione: la prima riga fa da intestazione, o non più.</summary>
    private void CambiaIntestazione()
    {
        if (CellaQui() is not { } cella)
            return;
        bool ora = RigheIntestazione(cella.Tabella) == 0;
        ImpostaIntestazione(cella.Tabella.Inizio, ora ? 1 : 0);
        _testo.Modified = true;
        _annunciatore.Annuncia(ora
            ? "La prima riga è l'intestazione della tabella: si ripete in cima a ogni pagina"
            : "La tabella non ha più la riga di intestazione");
    }

    [GeneratedRegex(@"\\trowd(?![a-z])")]
    private static partial Regex InizioRigaRtf();
}
