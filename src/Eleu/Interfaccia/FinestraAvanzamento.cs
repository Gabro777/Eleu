using System.Windows.Forms.Automation;

namespace Eleu.Interfaccia;

/// <summary>Avanzamento di un lavoro lungo (es. creazione dell'audiolibro), con il pulsante Annulla.</summary>
public sealed class FinestraAvanzamento : Form
{
    private readonly Label _stato;
    private readonly ProgressBar _barra;
    private readonly CancellationTokenSource _annulla = new();
    private DateTime _ultimoAnnuncio = DateTime.MinValue;
    private string _ultimoStato = "";

    public CancellationToken Annullamento => _annulla.Token;

    public FinestraAvanzamento(string titolo)
    {
        Text = titolo;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ControlBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Font = new Font("Segoe UI", 11f);
        Padding = new Padding(12);

        var colonna = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, Dock = DockStyle.Fill };
        _stato = new Label { AutoSize = true, MaximumSize = new Size(420, 0), Text = "Preparazione..." };
        _barra = new ProgressBar { Width = 420, Height = 24, AccessibleName = "Avanzamento" };
        var annulla = new Button { Text = "Annulla", AutoSize = true };
        annulla.Click += (_, _) =>
        {
            _annulla.Cancel();
            annulla.Enabled = false;
            _stato.Text = "Annullamento in corso...";
        };
        colonna.Controls.AddRange([_stato, _barra, annulla]);
        Controls.Add(colonna);
        CancelButton = annulla;
        ActiveControl = annulla;
    }

    /// <summary>
    /// Aggiorna testo e barra. Lo screen reader sente il punto a cui si è ogni pochi secondi (non a ogni passo,
    /// che coprirebbe tutto il resto), così chi non vede la barra sa che il lavoro va avanti.
    /// </summary>
    public void Aggiorna(string stato, int valore, int massimo)
    {
        _stato.Text = stato;
        _barra.Maximum = Math.Max(1, massimo);
        _barra.Value = Math.Clamp(valore, 0, _barra.Maximum);
        if (stato != _ultimoStato && DateTime.Now - _ultimoAnnuncio > TimeSpan.FromSeconds(5) && ActiveControl is { } attivo)
        {
            _ultimoAnnuncio = DateTime.Now;
            _ultimoStato = stato;
            attivo.AccessibilityObject.RaiseAutomationNotification(AutomationNotificationKind.Other,
                AutomationNotificationProcessing.MostRecent, $"{stato}, {100 * _barra.Value / _barra.Maximum} per cento");
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _annulla.Dispose();
        base.Dispose(disposing);
    }
}
