using System.Diagnostics;

namespace Eleu.Interfaccia;

/// <summary>Cosa fare con una versione nuova: installarla, saltarla o pensarci più tardi.</summary>
public enum SceltaAggiornamento { PiuTardi, Installa, Salta }

/// <summary>
/// "È disponibile Eleu X": le novità in un campo di sola lettura (lo screen reader lo legge all'apertura e lo si
/// rilegge con le frecce) e i pulsanti per installare, aprire la pagina della versione, saltarla o rimandare.
/// </summary>
public sealed class FinestraAggiornamento : Modulo
{
    public FinestraAggiornamento(Rilascio rilascio, Version attuale, bool installabile) : base("Aggiornamento di Eleu", 520)
    {
        var testo = $"È disponibile Eleu {rilascio.Versione}; ora hai la versione {attuale}." + Environment.NewLine +
                    (installabile
                        ? "Con «Scarica e installa» Eleu scarica la nuova versione, si chiude (chiedendo di salvare i documenti), " +
                          "la installa e si riapre."
                        : "Questa copia di Eleu non è installata con l'installer: scarica la nuova versione dalla sua pagina.") +
                    Environment.NewLine + Environment.NewLine + Novita(Aggiornamenti.NovitaLeggibili(rilascio.Novita));
        var novita = RigaIntera(new TextBox
        {
            Text = testo,
            ReadOnly = true,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            Width = 640,
            Height = 300,
            AccessibleName = "Novità",
        });

        var pulsanti = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 12, 0, 0) };
        if (installabile && rilascio.IndirizzoMsi is not null)
        {
            var installa = new Button { Text = "&Scarica e installa", AutoSize = true };
            installa.Click += (_, _) => Chiudi(SceltaAggiornamento.Installa);
            pulsanti.Controls.Add(installa);
            AcceptButton = installa;
        }
        var pagina = new Button { Text = "Apri la &pagina della versione", AutoSize = true };
        pagina.Click += (_, _) =>
        {
            try
            {
                Process.Start(new ProcessStartInfo(rilascio.Pagina) { UseShellExecute = true });
            }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                MessageBox.Show(this, $"Impossibile aprire il browser. L'indirizzo è {rilascio.Pagina}", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        };
        var salta = new Button { Text = "Sa&lta questa versione", AutoSize = true };
        salta.Click += (_, _) => Chiudi(SceltaAggiornamento.Salta);
        var dopo = new Button { Text = "Più &tardi", AutoSize = true, DialogResult = DialogResult.Cancel };
        pulsanti.Controls.AddRange([pagina, salta, dopo]);
        RigaIntera(pulsanti);
        CancelButton = dopo;
        ActiveControl = novita;
        Shown += (_, _) => novita.Select(0, 0);
    }

    private static string Novita(string testo) =>
        testo.StartsWith("Novità", StringComparison.OrdinalIgnoreCase) ? testo : "Novità:" + Environment.NewLine + testo;

    public SceltaAggiornamento Scelta { get; private set; } = SceltaAggiornamento.PiuTardi;

    private void Chiudi(SceltaAggiornamento scelta)
    {
        Scelta = scelta;
        DialogResult = DialogResult.OK;
    }
}
