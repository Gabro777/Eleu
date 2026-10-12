using Eleu.Correzione;
using Eleu.Documento;
using Eleu.Interfaccia;

namespace Eleu;

// Correzione (Strumenti > Correzione): errori di ortografia e di punteggiatura, uno alla volta con F7 o nella
// finestra Controllo ortografico; dizionario personale; errori sottolineati mentre si scrive.
public sealed partial class FinestraPrincipale
{
    /// <summary>Parole da non segnalare fino alla chiusura di Eleu ("Ignora tutti").</summary>
    private readonly HashSet<string> _ignorateOggi = new(StringComparer.CurrentCultureIgnoreCase);

    /// <summary>Gli errori del testo: ortografia (tranne le parole del dizionario personale) e punteggiatura.</summary>
    private List<ErroreProbabile> TrovaErrori(string testo)
    {
        var correttore = Correttore;
        var personali = new HashSet<string>(_impostazioni.DizionarioPersonale, StringComparer.CurrentCultureIgnoreCase);
        bool DaSegnalare(string parola) => !personali.Contains(parola) && !_ignorateOggi.Contains(parola);

        var ortografia = ErroriProbabili.Trova(testo,
                t => correttore?.ParoleSconosciute(t) ?? [],
                p => correttore?.Suggerimenti(p) ?? [],
                p => correttore?.Corretta(p) ?? false)
            .Where(e => ParolaDaControllare(e.Parola));
        var punteggiatura = Punteggiatura.Trova(testo).Where(e => DaSegnalare(e.Parola));

        // Se due errori si sovrappongono si tiene quello di ortografia.
        var errori = new List<ErroreProbabile>();
        foreach (var errore in ortografia.Concat(punteggiatura).OrderBy(e => e.Inizio).ThenBy(e => e.Motivo is null ? 0 : 1))
            if (errori.Count == 0 || errore.Inizio >= errori[^1].Inizio + errori[^1].Lunghezza)
                errori.Add(errore);
        return errori;
    }

    /// <summary>Il suggerimento migliore per un errore di ortografia, chiesto al dizionario solo quando serve.</summary>
    private ErroreProbabile ConSuggerimento(ErroreProbabile errore) =>
        errore.Suggerimento is not null || errore.Motivo is not null || Correttore is not { } correttore
            ? errore
            : errore with { Suggerimento = ErroriProbabili.MigliorSuggerimento(errore.Parola, correttore.Suggerimenti(errore.Parola), correttore.Corretta) };

    private static string Descrivi(ErroreProbabile errore)
    {
        var cosa = errore.Motivo is null ? errore.Parola : $"{errore.Motivo}: {Parlato.Di(errore.Parola)}";
        var consiglio = errore.Suggerimento switch
        {
            null => "nessun suggerimento",
            "" => "da togliere",
            var s => $"forse {Parlato.Di(s)}",
        };
        return $"{cosa}, {consiglio}";
    }


    /// <summary>F7 / Maiusc+F7: seleziona il prossimo (o precedente) errore e lo annuncia con il suggerimento.</summary>
    private void VaiAErrore(bool avanti, string prefisso = "")
    {
        var testo = _testo.Text;
        var errori = TrovaErrori(testo);
        if (errori.Count == 0)
        {
            _ultimoErrore = null;
            _annunciatore.Annuncia(prefisso + (Correttore is null
                ? "Nessun errore. Il dizionario italiano di Windows non è installato: controllo solo punteggiatura ed errori tipici del riconoscimento."
                : "Nessun errore"));
            return;
        }

        int posizione = avanti ? _testo.SelectionStart + _testo.SelectionLength : _testo.SelectionStart;
        var errore = ErroriProbabili.Successivo(errori, posizione, avanti);
        string ricominciato = "";
        if (errore is null)
        {
            errore = avanti ? errori[0] : errori[^1];
            ricominciato = avanti ? "Ricominciato dall'inizio. " : "Ricominciato dalla fine. ";
        }
        errore = ConSuggerimento(errore);
        _ultimoErrore = errore;

        _testo.Select(errore.Inizio, errore.Lunghezza);
        _testo.ScrollToCaret();
        _annunciatore.Annuncia($"{prefisso}{ricominciato}{Descrivi(errore)}. Riga {Posizione.Calcola(testo, errore.Inizio).Riga}");
    }

    /// <summary>Ctrl+F7: sostituisce l'errore selezionato con il suggerimento e passa al successivo.</summary>
    private void CorreggiConSuggerimento()
    {
        if (_ultimoErrore is not ErroreProbabile errore || _testo.SelectionStart != errore.Inizio || _testo.SelectedText != errore.Parola)
        {
            _annunciatore.Annuncia("Prima vai a un errore con F7");
            return;
        }
        if (errore.Suggerimento is not string correzione)
        {
            _annunciatore.Annuncia("Nessun suggerimento: scrivi tu la correzione");
            return;
        }
        _testo.SelectedText = correzione;
        VaiAErrore(avanti: true, correzione.Any(char.IsLetterOrDigit) ? $"Corretto in {correzione}. " : "Corretto. ");
    }

    /// <summary>
    /// Strumenti > Correzione > Controllo ortografico: una finestra che passa gli errori dal cursore in poi, con
    /// i suggerimenti, e permette di cambiare, ignorare o aggiungere la parola al dizionario personale.
    /// </summary>
    private void ApriControlloOrtografico()
    {
        using var finestra = new FinestraOrtografia();
        int da = _testo.SelectionStart;
        int corretti = 0;
        ErroreProbabile? corrente = null;

        void Prossimo()
        {
            var errori = TrovaErrori(_testo.Text);
            var errore = errori.FirstOrDefault(e => e.Inizio >= da) ?? (da > 0 ? errori.FirstOrDefault() : null);
            if (errore is null)
            {
                finestra.Fine(corretti == 0 ? "Nessun errore nel documento." : $"Controllo finito: {corretti} correzioni.");
                return;
            }
            corrente = ConSuggerimento(errore);
            _testo.Select(corrente.Inizio, corrente.Lunghezza);
            _testo.ScrollToCaret();
            var suggerimenti = corrente.Motivo is not null || Correttore is null
                ? (corrente.Suggerimento is null ? [] : [corrente.Suggerimento])
                : Correttore.Suggerimenti(corrente.Parola).Prepend(corrente.Suggerimento).OfType<string>().Distinct().Take(8).ToList();
            finestra.Mostra(corrente, Contesto(_testo.Text, corrente), suggerimenti);
        }

        finestra.Cambia += (correzione, tutti) =>
        {
            if (corrente is null)
                return;
            if (tutti && corrente.Motivo is null)
            {
                var occorrenze = Ricerca.TrovaTutte(_testo.Text, corrente.Parola, new OpzioniRicerca(MaiuscoleMinuscole: true, ParolaIntera: true));
                foreach (var posizione in occorrenze.OrderDescending())
                {
                    _testo.Select(posizione, corrente.Parola.Length);
                    _testo.SelectedText = correzione;
                }
                corretti += occorrenze.Count;
            }
            else
            {
                _testo.Select(corrente.Inizio, corrente.Lunghezza);
                _testo.SelectedText = correzione;
                corretti++;
            }
            da = corrente.Inizio + correzione.Length;
            Prossimo();
        };
        finestra.Ignora += tutti =>
        {
            if (corrente is null)
                return;
            if (tutti)
                _ignorateOggi.Add(corrente.Parola);
            da = corrente.Inizio + corrente.Lunghezza;
            Prossimo();
        };
        finestra.AggiungiAlDizionario += () =>
        {
            if (corrente is null || corrente.Motivo is not null)
                return;
            if (!_impostazioni.DizionarioPersonale.Contains(corrente.Parola, StringComparer.CurrentCultureIgnoreCase))
                _impostazioni.DizionarioPersonale.Add(corrente.Parola);
            da = corrente.Inizio + corrente.Lunghezza;
            Prossimo();
        };

        finestra.Shown += (_, _) => Prossimo();
        finestra.ShowDialog(this);
        _testo.Select(_testo.SelectionStart, 0);
        if (corretti > 0)
            AnnunciaTraPoco(corretti == 1 ? "1 correzione" : $"{corretti} correzioni");
    }

    /// <summary>La frase (o il pezzo di paragrafo) attorno all'errore, per capire di che si tratta.</summary>
    private static string Contesto(string testo, ErroreProbabile errore)
    {
        var paragrafi = StrutturaTesto.Paragrafi(testo);
        var (inizio, fine) = paragrafi[StrutturaTesto.ParagrafoDi(paragrafi, errore.Inizio)];
        int da = Math.Max(inizio, errore.Inizio - 60), a = Math.Min(fine, errore.Inizio + errore.Lunghezza + 60);
        return (da > inizio ? "..." : "") + StrutturaTesto.Leggibile(testo[da..a]).Trim() + (a < fine ? "..." : "");
    }

    private void GestisciDizionario()
    {
        using var finestra = new FinestraDizionario(_impostazioni.DizionarioPersonale);
        if (finestra.ShowDialog(this) != DialogResult.OK)
            return;
        _impostazioni.DizionarioPersonale = finestra.Parole;
        _impostazioni.Salva();
        AnnunciaTraPoco($"Dizionario personale: {finestra.Parole.Count} parole");
    }

    private void CambiaSottolineatura()
    {
        _impostazioni.ErroriSottolineati = !_impostazioni.ErroriSottolineati;
        Editor.RichEditNativo.ControlloMentreScrivi(_testo, _impostazioni.ErroriSottolineati);
        _annunciatore.Annuncia(_impostazioni.ErroriSottolineati ? "Errori sottolineati mentre scrivi" : "Errori non sottolineati");
    }
}
