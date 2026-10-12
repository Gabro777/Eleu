using System.Text.RegularExpressions;
using Eleu.Documento;

namespace Eleu.Correzione;

/// <summary>Cosa ripulire (Strumenti > Pulizia del testo).</summary>
public sealed record OpzioniPulizia(
    bool SpaziDoppi = true,
    bool SpaziAiBordi = true,
    bool RigheVuote = true,
    bool Sillabazione = true,
    bool ParagrafiSpezzati = false);

/// <summary>Una modifica al testo: sostituire <see cref="Lunghezza"/> caratteri da <see cref="Inizio"/> con <see cref="Nuovo"/>.</summary>
public sealed record Modifica(int Inizio, int Lunghezza, string Nuovo);

/// <summary>
/// Pulizia del testo a comando, tipica dei testi copiati da PDF o dal web: spazi doppi, spazi a inizio e fine
/// paragrafo, righe vuote ripetute, parole spezzate dal trattino a fine riga ("rico-\nstruire") e paragrafi
/// spezzati a metà frase. Restituisce le modifiche da fare, così chi le applica tocca solo quei punti e la
/// formattazione del resto resta. Le tabelle non si toccano.
/// </summary>
public static partial class Pulizia
{
    public static List<Modifica> Modifiche(string testo, OpzioniPulizia opzioni, int da = 0, int a = -1)
    {
        a = a < 0 ? testo.Length : Math.Min(a, testo.Length);
        var modifiche = new List<Modifica>();
        var tabelle = StrutturaTesto.Tabelle(testo);
        bool InTabella(int posizione) => tabelle.Any(t => posizione >= t.Inizio && posizione < t.Fine);
        void Aggiungi(Regex regola, Func<Match, string> nuovo)
        {
            foreach (Match m in regola.Matches(testo))
                if (m.Index >= da && m.Index + m.Length <= a && !testo.AsSpan(m.Index, m.Length).ContainsAny(Tecnici)
                    && !InTabella(m.Index))
                    modifiche.Add(new Modifica(m.Index, m.Length, nuovo(m)));
        }

        if (opzioni.Sillabazione)
            Aggiungi(Sillabata(), _ => "");
        if (opzioni.ParagrafiSpezzati)
            Aggiungi(Spezzato(), _ => " ");
        if (opzioni.RigheVuote)
            Aggiungi(RigheVuoteRipetute(), _ => "\n\n");
        if (opzioni.SpaziAiBordi)
        {
            Aggiungi(SpaziInizio(), _ => "");
            Aggiungi(SpaziFine(), _ => "");
        }
        if (opzioni.SpaziDoppi)
            Aggiungi(SpaziMultipli(), _ => " ");

        // Se due regole toccano lo stesso punto vince la prima (l'ordine sopra).
        var risultato = new List<Modifica>();
        foreach (var m in modifiche.OrderBy(m => m.Inizio))
            if (risultato.Count == 0 || m.Inizio >= risultato[^1].Inizio + risultato[^1].Lunghezza)
                risultato.Add(m);
        return risultato;
    }

    /// <summary>Applica le modifiche a un testo (per i test e per chi lavora su stringhe).</summary>
    public static string Applica(string testo, IEnumerable<Modifica> modifiche)
    {
        foreach (var m in modifiche.OrderByDescending(m => m.Inizio))
            testo = testo.Remove(m.Inizio, m.Lunghezza).Insert(m.Inizio, m.Nuovo);
        return testo;
    }

    private static readonly char[] Tecnici = [StrutturaTesto.InizioRiga, StrutturaTesto.FineRiga, StrutturaTesto.FineCella];

    // "rico-\nstruire": trattino a fine riga con una minuscola dopo
    [GeneratedRegex(@"(?<=\p{L})-[ \t]*\n[ \t]*(?=\p{Ll})")]
    private static partial Regex Sillabata();

    // Un a capo dopo una riga che non finisce la frase, prima di una minuscola
    [GeneratedRegex(@"(?<=[\p{L},])[ \t]*\n[ \t]*(?=\p{Ll})")]
    private static partial Regex Spezzato();

    [GeneratedRegex(@"\n(?:[ \t]*\n){2,}")]
    private static partial Regex RigheVuoteRipetute();

    [GeneratedRegex(@"(?<=^|\n)[ \t]+(?=\S)")]
    private static partial Regex SpaziInizio();

    [GeneratedRegex(@"(?<=\S)[ \t]+(?=\n|$)")]
    private static partial Regex SpaziFine();

    [GeneratedRegex(@"(?<=\S)[ \t]{2,}(?=\S)")]
    private static partial Regex SpaziMultipli();
}

/// <summary>Una coppia di Sostituzioni multiple: cosa cercare e con cosa sostituirlo.</summary>
public sealed record CoppiaSostituzione(string Cerca, string Sostituisci);

public static class SostituzioniMultiple
{
    /// <summary>
    /// Le modifiche per applicare tutte le coppie in un colpo, cercando ogni coppia nel testo originale
    /// (così una sostituzione non ne innesca un'altra). Se due coppie toccano lo stesso punto vince la prima.
    /// </summary>
    public static List<Modifica> Modifiche(string testo, IReadOnlyList<CoppiaSostituzione> coppie, OpzioniRicerca opzioni)
    {
        var modifiche = new List<Modifica>();
        foreach (var coppia in coppie.Where(c => c.Cerca.Length > 0))
            foreach (var (inizio, lunghezza) in Ricerca.TrovaTutteCorrispondenze(testo, coppia.Cerca, opzioni))
                if (!modifiche.Any(m => inizio < m.Inizio + m.Lunghezza && m.Inizio < inizio + lunghezza))
                    modifiche.Add(new Modifica(inizio, lunghezza, coppia.Sostituisci));
        return modifiche.OrderBy(m => m.Inizio).ToList();
    }
}
