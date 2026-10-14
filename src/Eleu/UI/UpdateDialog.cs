using System.Diagnostics;

namespace Eleu.UI;

/// <summary>What to do with a new version: install it, skip it or think about it later.</summary>
public enum UpdateChoice { Later, Install, Skip }

/// <summary>
/// "È disponibile Eleu X": the release notes in a read-only box (the screen reader reads it on opening and it can be
/// reread with the arrows) and the buttons to install, open the version page, skip it or postpone.
/// </summary>
public sealed class UpdateDialog : DialogBase
{
    public UpdateDialog(Release release, Version current, bool installable) : base("Aggiornamento di Eleu", 520)
    {
        var text = $"È disponibile Eleu {release.Version}; ora hai la versione {current}." + Environment.NewLine +
                    (installable
                        ? "Con «Scarica e installa» Eleu scarica la nuova versione, si chiude (chiedendo di salvare i documenti), " +
                          "la installa e si riapre."
                        : "Questa copia di Eleu non è installata con l'installer: scarica la nuova versione dalla sua pagina.") +
                    Environment.NewLine + Environment.NewLine + CleanNotes(Updates.ReadableReleaseNotes(release.ReleaseNotes));
        var releaseNotes = WholeLine(new TextBox
        {
            Text = text,
            ReadOnly = true,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            Width = 640,
            Height = 300,
            AccessibleName = "Novità",
        });

        var buttons = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 12, 0, 0) };
        if (installable && release.MsiUrl is not null)
        {
            var install = new Button { Text = "&Scarica e installa", AutoSize = true };
            install.Click += (_, _) => CloseWith(UpdateChoice.Install);
            buttons.Controls.Add(install);
            AcceptButton = install;
        }
        var page = new Button { Text = "Apri la &pagina della versione", AutoSize = true };
        page.Click += (_, _) =>
        {
            try
            {
                Process.Start(new ProcessStartInfo(release.Page) { UseShellExecute = true });
            }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                MessageBox.Show(this, $"Impossibile aprire il browser. L'indirizzo è {release.Page}", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        };
        var skip = new Button { Text = "Sa&lta questa versione", AutoSize = true };
        skip.Click += (_, _) => CloseWith(UpdateChoice.Skip);
        var after = new Button { Text = "Più &tardi", AutoSize = true, DialogResult = DialogResult.Cancel };
        buttons.Controls.AddRange([page, skip, after]);
        WholeLine(buttons);
        CancelButton = after;
        ActiveControl = releaseNotes;
        Shown += (_, _) => releaseNotes.Select(0, 0);
    }

    private static string CleanNotes(string text) =>
        text.StartsWith("Novità", StringComparison.OrdinalIgnoreCase) ? text : "Novità:" + Environment.NewLine + text;

    public UpdateChoice Choice { get; private set; } = UpdateChoice.Later;

    private void CloseWith(UpdateChoice choice)
    {
        Choice = choice;
        DialogResult = DialogResult.OK;
    }
}
