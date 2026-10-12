using Eleu.Accessibilita;

namespace Eleu.Interfaccia;

/// <summary>
/// Strumenti > Opzioni: le preferenze generali di Eleu in tre schede (Ctrl+Tab passa dall'una all'altra).
/// Generale: avvio e copia di recupero. Annunci e suoni: come Eleu segnala gli elementi del documento
/// e le parole sbagliate. Correzione: cosa segnalare.
/// </summary>
public sealed class FinestraOpzioni : Modulo
{
    private static readonly string[] Modi = ["Nessun segnale", "Suono", "Voce", "Voce e suono"];

    private readonly Impostazioni _impostazioni;
    private readonly CheckBox _riapri, _aggiornamenti, _suonoErrori, _conferme, _celle, _immagini, _sottolinea, _maiuscole, _numeri;
    private readonly NumericUpDown _minuti, _volume;
    private readonly ComboBox _elementi;

    public FinestraOpzioni(Impostazioni impostazioni) : base("Opzioni")
    {
        _impostazioni = impostazioni;

        var generale = NuovaGriglia(120);
        _riapri = Casella(generale, "All'&avvio riapri l'ultimo documento", impostazioni.RiapriUltimo);
        _minuti = Campo(generale, "Copia di &recupero ogni (minuti):", Numero(impostazioni.MinutiRipristino, 1, 30, 1, 0));
        _aggiornamenti = Casella(generale, "Controlla gli a&ggiornamenti all'avvio (una volta al giorno)", impostazioni.ControllaAggiornamenti);

        var annunci = NuovaGriglia(180);
        _elementi = Campo(annunci, "&Titoli, segnalibri, note, commenti, interruzioni di pagina:", Scelta(Modi, (int)impostazioni.SegnaleElementi));
        _suonoErrori = Casella(annunci, "Suono sulle parole &sbagliate", impostazioni.SuonoParolaSbagliata);
        _conferme = Casella(annunci, "Suoni di &conferma (fine lettura, salvataggio)", impostazioni.SuoniConferma);
        _volume = Campo(annunci, "&Volume dei suoni:", Numero(impostazioni.VolumeSuoni, 0, 100, 10, 0));
        var prova = RigaIntera(annunci, new Button { Text = "&Prova il suono", AutoSize = true });
        prova.Click += (_, _) => Suoni.Suona(Segnale.Titolo, (int)_volume.Value);
        _celle = Casella(annunci, "Annuncia le ce&lle delle tabelle", impostazioni.AnnunciaCelle);
        _immagini = Casella(annunci, "Annuncia le &immagini", impostazioni.AnnunciaImmagini);

        var correzione = NuovaGriglia(120);
        _sottolinea = Casella(correzione, "S&ottolinea gli errori mentre scrivi", impostazioni.ErroriSottolineati);
        _maiuscole = Casella(correzione, "Ignora le parole tutte &maiuscole", impostazioni.IgnoraMaiuscole);
        _numeri = Casella(correzione, "Ignora le parole con &numeri", impostazioni.IgnoraConNumeri);

        var schede = new TabControl { Dock = DockStyle.Fill, Height = 340, Width = 560, AccessibleName = "Schede" };
        schede.TabPages.Add(Scheda("Generale", generale));
        schede.TabPages.Add(Scheda("Annunci e suoni", annunci));
        schede.TabPages.Add(Scheda("Correzione", correzione));
        RigaIntera(schede);

        AggiungiPulsanti();
        ActiveControl = _riapri;
    }

    /// <summary>Scrive le scelte nelle impostazioni (dopo OK).</summary>
    public void Applica()
    {
        _impostazioni.RiapriUltimo = _riapri.Checked;
        _impostazioni.ControllaAggiornamenti = _aggiornamenti.Checked;
        _impostazioni.MinutiRipristino = (int)_minuti.Value;
        _impostazioni.SegnaleElementi = (ModoSegnale)Math.Max(0, _elementi.SelectedIndex);
        _impostazioni.SuonoParolaSbagliata = _suonoErrori.Checked;
        _impostazioni.SuoniConferma = _conferme.Checked;
        _impostazioni.VolumeSuoni = (int)_volume.Value;
        _impostazioni.AnnunciaCelle = _celle.Checked;
        _impostazioni.AnnunciaImmagini = _immagini.Checked;
        _impostazioni.ErroriSottolineati = _sottolinea.Checked;
        _impostazioni.IgnoraMaiuscole = _maiuscole.Checked;
        _impostazioni.IgnoraConNumeri = _numeri.Checked;
    }
}
