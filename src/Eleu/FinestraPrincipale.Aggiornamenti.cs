using Eleu.Interfaccia;

namespace Eleu;

// Aggiornamenti (Aiuto > Controlla aggiornamenti, e una volta al giorno all'avvio se le Opzioni lo vogliono):
// la nuova versione si scarica da GitHub, si installa e Eleu si riapre.
public sealed partial class FinestraPrincipale
{
    /// <summary>Il controllo all'avvio si fa una volta sola, anche se si aprono altre finestre.</summary>
    private static bool _controlloAvvioFatto;

    /// <summary>
    /// All'avvio, in sottofondo: se c'è una versione nuova e non si è ancora cominciato a scrivere, la finestra
    /// dell'aggiornamento si apre; altrimenti Eleu lo dice e basta, per non interrompere.
    /// </summary>
    private async void ControllaAllAvvio()
    {
        if (_controlloAvvioFatto || !_impostazioni.ControllaAggiornamenti
            || _impostazioni.UltimoControlloAggiornamenti is DateTime ultimo && DateTime.Now - ultimo < TimeSpan.FromHours(20))
            return;
        _controlloAvvioFatto = true;
        Rilascio? rilascio;
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(3));
            rilascio = await Aggiornamenti.UltimaAsync();
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException)
        {
            return; // Senza internet (o con GitHub irraggiungibile) si riprova un'altra volta.
        }
        if (IsDisposed)
            return;
        _impostazioni.UltimoControlloAggiornamenti = DateTime.Now;
        _impostazioni.Salva();
        if (rilascio is null || rilascio.Versione <= Aggiornamenti.VersioneAttuale || rilascio.Tag == _impostazioni.VersioneSaltata)
            return;
        if (!_testo.Modified && (ActiveForm is null || ActiveForm == this))
            MostraAggiornamento(rilascio);
        else
            _annunciatore.Annuncia($"È disponibile Eleu {rilascio.Versione}: per installarlo, Aiuto, Controlla aggiornamenti");
    }

    /// <summary>Aiuto > Controlla aggiornamenti.</summary>
    private async void ControllaAggiornamenti()
    {
        _annunciatore.Annuncia("Controllo gli aggiornamenti...");
        Rilascio? rilascio;
        try
        {
            rilascio = await Aggiornamenti.UltimaAsync();
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException)
        {
            MessageBox.Show(this, "Impossibile controllare gli aggiornamenti: controlla la connessione a internet e riprova.\n\n" + e.Message,
                NomeApp, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        _impostazioni.UltimoControlloAggiornamenti = DateTime.Now;
        if (rilascio is null)
        {
            MessageBox.Show(this, "Su GitHub non c'è una versione di Eleu pubblicata che si possa installare.", NomeApp,
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (rilascio.Versione <= Aggiornamenti.VersioneAttuale)
        {
            MessageBox.Show(this, $"Hai già l'ultima versione di Eleu ({Aggiornamenti.VersioneAttuale}).", NomeApp,
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        MostraAggiornamento(rilascio);
    }

    private void MostraAggiornamento(Rilascio rilascio)
    {
        using var finestra = new FinestraAggiornamento(rilascio, Aggiornamenti.VersioneAttuale, Aggiornamenti.Installato);
        finestra.ShowDialog(this);
        switch (finestra.Scelta)
        {
            case SceltaAggiornamento.Salta:
                _impostazioni.VersioneSaltata = rilascio.Tag;
                _impostazioni.Salva();
                AnnunciaTraPoco($"Eleu non proporrà più la versione {rilascio.Versione}");
                break;
            case SceltaAggiornamento.Installa:
                BeginInvoke(() => ScaricaEInstalla(rilascio));
                break;
        }
    }

    private void ScaricaEInstalla(Rilascio rilascio)
    {
        string? scaricato = null;
        Exception? errore = null;
        bool annullato = false;
        using (var avanzamento = new FinestraAvanzamento($"Scaricamento di Eleu {rilascio.Versione}"))
        {
            var progresso = new Progress<(long Fatti, long Totale)>(a =>
                avanzamento.Aggiorna($"Scaricati {a.Fatti / 1048576} di {Math.Max(1, a.Totale / 1048576)} megabyte",
                    (int)(a.Fatti / 1024), (int)Math.Max(1, a.Totale / 1024)));
            avanzamento.Shown += async (_, _) =>
            {
                try
                {
                    scaricato = await Aggiornamenti.ScaricaAsync(rilascio, progresso, avanzamento.Annullamento);
                }
                catch (OperationCanceledException)
                {
                    annullato = true;
                }
                catch (Exception e) // Al confine con l'utente: l'errore diventa un messaggio.
                {
                    errore = e;
                }
                finally
                {
                    avanzamento.DialogResult = DialogResult.OK;
                }
            };
            avanzamento.ShowDialog(this);
        }

        if (annullato)
        {
            AnnunciaTraPoco("Aggiornamento annullato");
            return;
        }
        if (errore is not null || scaricato is null)
        {
            MessageBox.Show(this, $"Impossibile scaricare l'aggiornamento.\n\n{errore?.Message}", NomeApp, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        // Si chiudono tutte le finestre, ognuna chiede se salvare; se una resta aperta, l'aggiornamento aspetta.
        foreach (var finestra in ContestoEleu.Finestre.ToList())
        {
            finestra.Close();
            if (!finestra.IsDisposed && finestra.Visible)
            {
                MessageBox.Show(finestra, "Aggiornamento rimandato: per installarlo, Eleu deve chiudere tutti i documenti. Riprova da Aiuto, Controlla aggiornamenti.",
                    NomeApp, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
        }
        Aggiornamenti.AvviaInstallazione(scaricato);
    }
}
