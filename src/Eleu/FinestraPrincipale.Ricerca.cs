using Eleu.Documento;
using Eleu.Interfaccia;

namespace Eleu;

// Trova e sostituisci.
public sealed partial class FinestraPrincipale
{
    private string _ultimaRicerca = "";
    private string _ultimaSostituzione = "";
    private OpzioniRicerca _opzioniRicerca = new();

    private void ApriTrova(bool conSostituzione)
    {
        var selezione = _testo.SelectedText;
        var iniziale = selezione.Length is > 0 and < 100 && !selezione.Contains('\n') ? selezione : _ultimaRicerca;

        using var finestra = new FinestraTrova(conSostituzione, iniziale, _ultimaSostituzione, _opzioniRicerca);
        void Memorizza()
        {
            _ultimaRicerca = finestra.TestoCercato;
            _ultimaSostituzione = finestra.TestoSostitutivo;
            _opzioniRicerca = finestra.Opzioni;
        }
        finestra.TrovaRichiesto += (_, _) =>
        {
            Memorizza();
            _annunciatore.Annuncia(Trova(avanti: true));
        };
        finestra.SostituisciRichiesto += (_, _) =>
        {
            Memorizza();
            SostituisciCorrente();
        };
        finestra.SostituisciTuttoRichiesto += (_, _) =>
        {
            Memorizza();
            SostituisciTutto();
        };

        var esito = finestra.ShowDialog(this);
        Memorizza();
        if (!conSostituzione && esito == DialogResult.OK)
            AnnunciaTraPoco(Trova(avanti: true));
    }

    private void TrovaEAnnuncia(bool avanti)
    {
        if (_ultimaRicerca.Length == 0)
        {
            ApriTrova(false);
            return;
        }
        _annunciatore.Annuncia(Trova(avanti));
    }

    /// <summary>Seleziona la prossima occorrenza (ricominciando dall'inizio se serve) e restituisce cosa annunciare.</summary>
    private string Trova(bool avanti)
    {
        if (_ultimaRicerca.Length == 0)
            return "Nessun testo da cercare";

        var testo = _testo.Text;
        int da = avanti ? _testo.SelectionStart + _testo.SelectionLength : _testo.SelectionStart;
        var trovato = Ricerca.TrovaCorrispondenza(testo, _ultimaRicerca, da, avanti, _opzioniRicerca);
        string prefisso = "";
        if (trovato is null)
        {
            trovato = Ricerca.TrovaCorrispondenza(testo, _ultimaRicerca, avanti ? 0 : testo.Length, avanti, _opzioniRicerca);
            prefisso = avanti ? "Ricerca ripresa dall'inizio. " : "Ricerca ripresa dalla fine. ";
        }
        if (trovato is not var (inizio, lunghezza))
            return $"{_ultimaRicerca} non trovato";

        _testo.Select(inizio, lunghezza);
        _testo.ScrollToCaret();
        return prefisso + DescriviRiga(testo, inizio);
    }

    /// <summary>
    /// Sostituisce l'occorrenza selezionata e passa alla successiva (F4) o alla precedente (Maiusc+F4).
    /// Senza una ricerca precedente apre la finestra Sostituisci.
    /// </summary>
    private void SostituisciCorrente(bool avanti = true)
    {
        if (_ultimaRicerca.Length == 0)
        {
            ApriTrova(true);
            return;
        }
        string prefisso = "";
        if (Ricerca.Corrisponde(_testo.SelectedText, _ultimaRicerca, _opzioniRicerca))
        {
            int inizio = _testo.SelectionStart;
            _testo.SelectedText = _ultimaSostituzione;
            if (!avanti)
                _testo.Select(inizio, 0); // all'indietro si riparte da prima della sostituzione
            prefisso = "Sostituito. ";
        }
        _annunciatore.Annuncia(prefisso + Trova(avanti));
    }

    private void SostituisciTutto()
    {
        var posizioni = Ricerca.TrovaTutteCorrispondenze(_testo.Text, _ultimaRicerca, _opzioniRicerca);
        if (posizioni.Count == 0)
        {
            _annunciatore.Annuncia($"{_ultimaRicerca} non trovato");
            return;
        }

        // Dalla fine all'inizio, così le posizioni ancora da sostituire non si spostano.
        for (int i = posizioni.Count - 1; i >= 0; i--)
        {
            _testo.Select(posizioni[i].Inizio, posizioni[i].Lunghezza);
            _testo.SelectedText = _ultimaSostituzione;
        }
        _testo.Select(posizioni[0].Inizio, _ultimaSostituzione.Length);

        _annunciatore.Annuncia(posizioni.Count == 1 ? "1 sostituzione eseguita" : $"{posizioni.Count} sostituzioni eseguite");
    }

    /// <summary>"Riga 3: testo della riga", per sapere dove si è arrivati senza muovere il cursore.</summary>
    private static string DescriviRiga(string testo, int indice)
    {
        var paragrafi = StrutturaTesto.Paragrafi(testo);
        var (inizio, fine) = paragrafi[StrutturaTesto.ParagrafoDi(paragrafi, Math.Clamp(indice, 0, testo.Length))];
        var riga = StrutturaTesto.Leggibile(testo[inizio..fine]).Trim();
        if (riga.Length > 120)
            riga = riga[..120] + "...";
        // In tabella si dice la cella, non la riga.
        if (StrutturaTesto.CellaDi(testo, indice) is { } cella)
            return $"Tabella {cella.NumeroTabella + 1}, riga {cella.Riga + 1}, colonna {cella.Colonna + 1}: {(riga.Length > 0 ? riga : "vuota")}";
        return $"Riga {Posizione.Calcola(testo, indice).Riga}: {riga}";
    }
}
