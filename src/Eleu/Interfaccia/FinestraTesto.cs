namespace Eleu.Interfaccia;

/// <summary>
/// Mostra un testo lungo in un campo di sola lettura: a differenza di un messaggio,
/// si può rileggere riga per riga con le frecce.
/// </summary>
public sealed class FinestraTesto : Form
{
    public FinestraTesto(string titolo, string testo)
    {
        Text = titolo;
        Width = 600;
        Height = 500;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = false;
        Font = new Font("Segoe UI", 11f);

        var campo = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Dock = DockStyle.Fill,
            Text = testo,
            AccessibleName = titolo,
            TabIndex = 0,
        };

        var chiudi = new Button { Text = "Chiudi", Dock = DockStyle.Bottom, Height = 40, DialogResult = DialogResult.Cancel };

        Controls.Add(campo);
        Controls.Add(chiudi);
        CancelButton = chiudi;
        ActiveControl = campo;
        campo.Select(0, 0);
    }
}
