using Eleu.UI;

namespace Eleu;

// Updates (Aiuto > Controlla aggiornamenti, and once a day at startup if the Options say so):
// the new version is downloaded from GitHub, installed and Eleu reopens.
public sealed partial class MainWindow
{
    /// <summary>The startup check is done only once, even if other windows are opened.</summary>
    private static bool _startupCheckDone;

    /// <summary>
    /// At startup, in the background: if there is a new version and the user has not started typing yet, the update
    /// window opens; otherwise Eleu just says so, so as not to interrupt.
    /// </summary>
    private async void CheckAtStartup()
    {
        if (_startupCheckDone || !_settings.CheckForUpdates
            || _settings.LastUpdateCheck is DateTime last && DateTime.Now - last < TimeSpan.FromHours(20))
            return;
        _startupCheckDone = true;
        Release? release;
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(3));
            release = await Updates.LatestAsync();
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException)
        {
            return; // Without internet (or with GitHub unreachable) it will try again another time.
        }
        if (IsDisposed)
            return;
        _settings.LastUpdateCheck = DateTime.Now;
        _settings.Save();
        if (release is null || release.Version <= Updates.CurrentVersion || release.Tag == _settings.SkippedVersion)
            return;
        if (!_text.Modified && (ActiveForm is null || ActiveForm == this))
            ShowUpdate(release);
        else
            _announcer.Announce($"È disponibile Eleu {release.Version}: per installarlo, Aiuto, Controlla aggiornamenti");
    }

    /// <summary>Aiuto > Controlla aggiornamenti.</summary>
    private async void CheckForUpdates()
    {
        _announcer.Announce("Controllo gli aggiornamenti...");
        Release? release;
        try
        {
            release = await Updates.LatestAsync();
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException)
        {
            MessageBox.Show(this, "Impossibile controllare gli aggiornamenti: controlla la connessione a internet e riprova.\n\n" + e.Message,
                AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        _settings.LastUpdateCheck = DateTime.Now;
        if (release is null)
        {
            MessageBox.Show(this, "Su GitHub non c'è una versione di Eleu pubblicata che si possa installare.", AppName,
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (release.Version <= Updates.CurrentVersion)
        {
            MessageBox.Show(this, $"Hai già l'ultima versione di Eleu ({Updates.CurrentVersion}).", AppName,
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        ShowUpdate(release);
    }

    private void ShowUpdate(Release release)
    {
        using var window = new UpdateDialog(release, Updates.CurrentVersion, Updates.IsInstalled);
        window.ShowDialog(this);
        switch (window.Choice)
        {
            case UpdateChoice.Skip:
                _settings.SkippedVersion = release.Tag;
                _settings.Save();
                AnnounceShortly($"Eleu non proporrà più la versione {release.Version}");
                break;
            case UpdateChoice.Install:
                BeginInvoke(() => DownloadAndInstall(release));
                break;
        }
    }

    private void DownloadAndInstall(Release release)
    {
        string? downloaded = null;
        Exception? error = null;
        bool cancelled = false;
        using (var progressDialog = new ProgressDialog($"Scaricamento di Eleu {release.Version}"))
        {
            var progress = new Progress<(long Done, long Total)>(a =>
                progressDialog.Report($"Scaricati {a.Done / 1048576} di {Math.Max(1, a.Total / 1048576)} megabyte",
                    (int)(a.Done / 1024), (int)Math.Max(1, a.Total / 1024)));
            progressDialog.Shown += async (_, _) =>
            {
                try
                {
                    downloaded = await Updates.DownloadAsync(release, progress, progressDialog.Cancellation);
                }
                catch (OperationCanceledException)
                {
                    cancelled = true;
                }
                catch (Exception e) // At the user boundary: the error becomes a message.
                {
                    error = e;
                }
                finally
                {
                    progressDialog.DialogResult = DialogResult.OK;
                }
            };
            progressDialog.ShowDialog(this);
        }

        if (cancelled)
        {
            AnnounceShortly("Aggiornamento annullato");
            return;
        }
        if (error is not null || downloaded is null)
        {
            MessageBox.Show(this, $"Impossibile scaricare l'aggiornamento.\n\n{error?.Message}", AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        // All windows are closed, each asking whether to save; if one stays open, the update waits.
        foreach (var window in EleuContext.Windows.ToList())
        {
            window.Close();
            if (!window.IsDisposed && window.Visible)
            {
                MessageBox.Show(window, "Aggiornamento rimandato: per installarlo, Eleu deve chiudere tutti i documenti. Riprova da Aiuto, Controlla aggiornamenti.",
                    AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
        }
        Updates.StartInstallation(downloaded);
    }
}
