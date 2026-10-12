using Eleu.Voce;

namespace Eleu.Interfaccia;

/// <summary>Scelta della voce, della velocità e del volume, con un pulsante per ascoltarla.</summary>
public sealed class FinestraVoce : Form
{
    private readonly ComboBox _voci;
    private readonly NumericUpDown _velocita;
    private readonly NumericUpDown _volume;
    private readonly IReadOnlyList<VoceDisponibile> _elenco;

    public event EventHandler? ProvaRichiesta;

    public VoceDisponibile? VoceScelta => _voci.SelectedIndex >= 0 ? _elenco[_voci.SelectedIndex] : null;

    public ParametriVoce Parametri => new((int)_velocita.Value, (int)_volume.Value);

    public FinestraVoce(IReadOnlyList<VoceDisponibile> voci, VoceDisponibile? attuale, ParametriVoce parametri)
    {
        _elenco = voci;
        Text = "Voce";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Font = new Font("Segoe UI", 11f);
        Padding = new Padding(12);

        var griglia = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Fill };
        griglia.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        griglia.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 340));

        _voci = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, AccessibleName = "Voce" };
        _voci.Items.AddRange(voci.Select(v => (object)v.Descrizione).ToArray());
        _voci.SelectedIndex = attuale is null ? (voci.Count > 0 ? 0 : -1) : voci.ToList().FindIndex(v => v.Id == attuale.Id);
        AggiungiCampo(griglia, "&Voce:", _voci);

        _velocita = new NumericUpDown
        {
            Minimum = ParametriVoce.VelocitaMinima,
            Maximum = ParametriVoce.VelocitaMassima,
            Increment = 10,
            Value = Math.Clamp(parametri.Velocita, ParametriVoce.VelocitaMinima, ParametriVoce.VelocitaMassima),
            AccessibleName = "Velocità in percentuale",
        };
        AggiungiCampo(griglia, "V&elocità (%):", _velocita);

        _volume = new NumericUpDown
        {
            Minimum = 10,
            Maximum = 100,
            Increment = 10,
            Value = Math.Clamp(parametri.Volume, 10, 100),
            AccessibleName = "Volume in percentuale",
        };
        AggiungiCampo(griglia, "V&olume (%):", _volume);

        var pulsanti = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 12, 0, 0) };
        var prova = new Button { Text = "&Prova", AutoSize = true };
        prova.Click += (_, _) => ProvaRichiesta?.Invoke(this, EventArgs.Empty);
        var ok = new Button { Text = "OK", AutoSize = true, DialogResult = DialogResult.OK };
        var annulla = new Button { Text = "Annulla", AutoSize = true, DialogResult = DialogResult.Cancel };
        pulsanti.Controls.AddRange([prova, ok, annulla]);
        griglia.Controls.Add(pulsanti);
        griglia.SetColumnSpan(pulsanti, 2);

        Controls.Add(griglia);
        AcceptButton = ok;
        CancelButton = annulla;
        ActiveControl = _voci;
    }

    private static void AggiungiCampo(TableLayoutPanel griglia, string etichetta, Control campo)
    {
        // L'etichetta precede il campo: Windows la usa come nome e Alt+lettera porta al campo.
        griglia.Controls.Add(new Label { Text = etichetta, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 6) });
        griglia.Controls.Add(campo);
    }
}
