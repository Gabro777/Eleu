using Eleu.Accessibilita;
using Eleu.Documento;
using Eleu.Editor;
using Eleu.Interfaccia;

namespace Eleu;

// Segnali mentre ci si muove nel testo: arrivando su un titolo, un segnalibro, una nota, un commento o un'interruzione di
// pagina Eleu lo fa sapere con un suono breve (o a voce, secondo le Opzioni), senza coprire lo screen reader
// che legge la riga. Un suono grave avverte quando il cursore arriva su una parola sbagliata.
// Mentre si scrive i segnali tacciono: servono a orientarsi, non a disturbare.
public sealed partial class FinestraPrincipale
{
    private int _ultimaLunghezza = -1;
    private int _ultimoParagrafoSegnalato = -1;
    private int _ultimaNotaSegnalata;
    private int? _ultimoCommentoSegnalato;
    private (int Inizio, int Fine) _ultimaParolaControllata = (-1, -1);

    private void SegnalaElementi()
    {
        if (_celleInSilenzio || !_testo.Focused)
            return;
        var testo = _testo.Text;
        int qui = Math.Min(_testo.SelectionStart, testo.Length);
        int inizio = qui == 0 ? 0 : testo.LastIndexOf('\n', qui - 1) + 1;
        int fine = testo.IndexOf('\n', qui);
        if (fine < 0)
            fine = testo.Length;
        bool nuovoParagrafo = inizio != _ultimoParagrafoSegnalato;
        _ultimoParagrafoSegnalato = inizio;
        var parola = ParolaIntorno(testo, qui);

        // Testo cambiato: si sta scrivendo o modificando, niente segnali.
        if (testo.Length != _ultimaLunghezza)
        {
            _ultimaLunghezza = testo.Length;
            _ultimaParolaControllata = parola;
            return;
        }

        var segnali = new List<(Segnale Suono, string Voce)>();
        var modo = _impostazioni.SegnaleElementi;
        if (modo != ModoSegnale.Nessuno)
        {
            if (nuovoParagrafo)
            {
                int livello = RichEditNativo.LivelloTitolo(_testo);
                if (livello > 0)
                    segnali.Add((Segnale.Titolo, $"Titolo {livello}"));
                if (testo.AsSpan(inizio, fine - inizio).Contains(Paginatore.SaltoPagina))
                    segnali.Add((Segnale.InterruzionePagina, "Interruzione di pagina"));
                if (_segnalibri.Count > 0)
                    foreach (var (nome, posizione) in ElencoSegnalibri().Where(s => s.Posizione >= inizio && s.Posizione <= fine))
                        segnali.Add((Segnale.Segnalibro, $"Segnalibro {nome}"));
            }
            int nota = _note.Count > 0 && NotaQui() is var (numero, _, _, _) ? numero : 0;
            if (nota != _ultimaNotaSegnalata && nota > 0)
                segnali.Add((Segnale.Nota, $"Nota {nota}"));
            _ultimaNotaSegnalata = nota;
            var commento = _commenti.Count > 0 ? CommentoQui() : null;
            if (commento is var (_, datiCommento, inizioCommento, _) && inizioCommento != _ultimoCommentoSegnalato)
                segnali.Add((Segnale.Commento, datiCommento.Autore.Length > 0 ? $"Commento di {datiCommento.Autore}" : "Commento"));
            _ultimoCommentoSegnalato = commento?.Inizio;
        }

        if (parola != _ultimaParolaControllata)
        {
            _ultimaParolaControllata = parola;
            if (_impostazioni.SuonoParolaSbagliata && segnali.Count == 0 && _testo.SelectionLength == 0 && ParolaSbagliata(testo[parola.Inizio..parola.Fine]))
            {
                Suoni.Suona(Segnale.ParolaSbagliata, _impostazioni.VolumeSuoni);
                return;
            }
        }

        if (segnali.Count == 0)
            return;
        if (modo is ModoSegnale.Suono or ModoSegnale.VoceESuono)
            Suoni.Suona(segnali[0].Suono, _impostazioni.VolumeSuoni);
        if (modo is ModoSegnale.Voce or ModoSegnale.VoceESuono)
            _annunciatore.Annuncia(string.Join(", ", segnali.Select(s => s.Voce)));
    }

    /// <summary>La parola (lettere, cifre e apostrofi interni) in cui si trova la posizione, o un intervallo vuoto.</summary>
    private static (int Inizio, int Fine) ParolaIntorno(string testo, int posizione)
    {
        static bool DiParola(char c) => char.IsLetterOrDigit(c) || c is '\'' or '’';
        int inizio = posizione, fine = posizione;
        while (inizio > 0 && DiParola(testo[inizio - 1]))
            inizio--;
        while (fine < testo.Length && DiParola(testo[fine]))
            fine++;
        while (inizio < fine && !char.IsLetterOrDigit(testo[inizio]))
            inizio++;
        while (fine > inizio && !char.IsLetterOrDigit(testo[fine - 1]))
            fine--;
        return (inizio, fine);
    }

    private bool ParolaSbagliata(string parola) =>
        parola.Length > 1 && ParolaDaControllare(parola) && Correttore is { } correttore && !correttore.Corretta(parola);

    /// <summary>
    /// Se una parola va controllata: non se è nel dizionario personale, ignorata fino alla chiusura, o se le
    /// Opzioni dicono di saltare le sigle in maiuscolo e le parole con numeri.
    /// </summary>
    private bool ParolaDaControllare(string parola)
    {
        if (_ignorateOggi.Contains(parola) || _impostazioni.DizionarioPersonale.Contains(parola, StringComparer.CurrentCultureIgnoreCase))
            return false;
        if (_impostazioni.IgnoraConNumeri && parola.Any(char.IsDigit))
            return false;
        if (_impostazioni.IgnoraMaiuscole && parola.Length > 1 && parola.Where(char.IsLetter).All(char.IsUpper) && parola.Any(char.IsLetter))
            return false;
        return true;
    }

    /// <summary>Un suono di conferma (fine lettura, salvataggio), se le Opzioni lo vogliono.</summary>
    private void SuonoConferma(Segnale segnale)
    {
        if (_impostazioni.SuoniConferma)
            Suoni.Suona(segnale, _impostazioni.VolumeSuoni);
    }

    /// <summary>Strumenti > Opzioni.</summary>
    private void ApriOpzioni()
    {
        using var finestra = new FinestraOpzioni(_impostazioni);
        if (finestra.ShowDialog(this) != DialogResult.OK)
            return;
        finestra.Applica();
        _impostazioni.Salva();
        RichEditNativo.ControlloMentreScrivi(_testo, _impostazioni.ErroriSottolineati);
        _timerRipristino.Interval = _impostazioni.MinutiRipristino * 60_000;
        AnnunciaTraPoco("Opzioni salvate");
    }
}
