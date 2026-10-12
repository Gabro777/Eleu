namespace Eleu.Interfaccia;

/// <summary>
/// Base comune delle finestre di impostazione: etichetta + campo su due colonne, pulsanti OK e Annulla.
/// L'etichetta precede sempre il campo, così Windows la usa come nome accessibile e Alt+lettera
/// porta direttamente al campo.
/// </summary>
public abstract class Modulo : Form
{
    protected TableLayoutPanel Griglia { get; }

    protected Modulo(string titolo, int larghezzaCampi = 300)
    {
        Text = titolo;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Font = new Font("Segoe UI", 11f);
        Padding = new Padding(12);

        Griglia = NuovaGriglia(larghezzaCampi);
        Controls.Add(Griglia);
    }

    /// <summary>Una griglia etichetta + campo, per esempio da mettere in una scheda.</summary>
    protected static TableLayoutPanel NuovaGriglia(int larghezzaCampi = 300)
    {
        var griglia = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Fill };
        griglia.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        griglia.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, larghezzaCampi));
        return griglia;
    }

    protected T Campo<T>(string etichetta, T campo) where T : Control => Campo(Griglia, etichetta, campo);

    protected static T Campo<T>(TableLayoutPanel griglia, string etichetta, T campo) where T : Control
    {
        griglia.Controls.Add(new Label { Text = etichetta, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 6) });
        campo.Dock = DockStyle.Fill;
        campo.AccessibleName ??= etichetta.Replace("&", "").TrimEnd(':');
        griglia.Controls.Add(campo);
        return campo;
    }

    protected T RigaIntera<T>(T controllo) where T : Control => RigaIntera(Griglia, controllo);

    protected static T RigaIntera<T>(TableLayoutPanel griglia, T controllo) where T : Control
    {
        griglia.Controls.Add(controllo);
        griglia.SetColumnSpan(controllo, 2);
        return controllo;
    }

    /// <summary>"Predefinito": le scelte valgono anche come punto di partenza per i documenti nuovi.</summary>
    protected CheckBox CasellaPredefinito(bool spuntata) =>
        RigaIntera(new CheckBox { Text = "Pr&edefinito (usa anche per i documenti nuovi)", AutoSize = true, Checked = spuntata, Margin = new Padding(0, 8, 0, 0) });

    /// <summary>Una scheda per un TabControl, con dentro una griglia di campi.</summary>
    protected static TabPage Scheda(string titolo, Control contenuto)
    {
        var scheda = new TabPage(titolo) { Padding = new Padding(8), AutoScroll = true };
        contenuto.Dock = DockStyle.Top; // con Fill l'ultima riga si allungherebbe fino in fondo alla scheda
        scheda.Controls.Add(contenuto);
        return scheda;
    }

    /// <summary>Una casella di spunta su tutta la riga della griglia.</summary>
    protected static CheckBox Casella(TableLayoutPanel griglia, string testo, bool spuntata) =>
        RigaIntera(griglia, new CheckBox { Text = testo, AutoSize = true, Checked = spuntata, Margin = new Padding(0, 4, 0, 4) });

    protected static ComboBox Scelta(IEnumerable<string> voci, int selezionata)
    {
        var scelta = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        scelta.Items.AddRange(voci.Cast<object>().ToArray());
        scelta.SelectedIndex = Math.Clamp(selezionata, -1, scelta.Items.Count - 1);
        return scelta;
    }

    protected static NumericUpDown Numero(decimal valore, decimal minimo, decimal massimo, decimal passo, int decimali) =>
        new()
        {
            Minimum = minimo,
            Maximum = massimo,
            Increment = passo,
            DecimalPlaces = decimali,
            Value = Math.Clamp(valore, minimo, massimo),
        };

    protected void AggiungiPulsanti()
    {
        var pulsanti = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 12, 0, 0) };
        var ok = new Button { Text = "OK", AutoSize = true, DialogResult = DialogResult.OK };
        var annulla = new Button { Text = "Annulla", AutoSize = true, DialogResult = DialogResult.Cancel };
        pulsanti.Controls.AddRange([ok, annulla]);
        RigaIntera(pulsanti);
        AcceptButton = ok;
        CancelButton = annulla;
    }
}
