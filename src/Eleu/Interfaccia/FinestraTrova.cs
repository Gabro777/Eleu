using Eleu.Documento;

namespace Eleu.Interfaccia;

/// <summary>
/// Finestra Trova / Sostituisci. In modalità Trova, Invio cerca e chiude (poi F3 continua);
/// in modalità Sostituisci resta aperta finché non si preme Esc.
/// </summary>
public sealed class FinestraTrova : Form
{
    private readonly TextBox _cerca;
    private readonly TextBox? _sostituisci;
    private readonly CheckBox _maiuscole;
    private readonly CheckBox _parolaIntera;
    private readonly CheckBox _jolly;

    public event EventHandler? TrovaRichiesto;
    public event EventHandler? SostituisciRichiesto;
    public event EventHandler? SostituisciTuttoRichiesto;

    public string TestoCercato => _cerca.Text;

    public string TestoSostitutivo => _sostituisci?.Text ?? "";

    public OpzioniRicerca Opzioni => new(_maiuscole.Checked, _parolaIntera.Checked, _jolly.Checked);

    public FinestraTrova(bool conSostituzione, string cerca, string sostituisci, OpzioniRicerca opzioni)
    {
        Text = conSostituzione ? "Sostituisci" : "Trova";
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
        griglia.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 320));

        _cerca = AggiungiCampo(griglia, "&Cerca:", cerca);
        if (conSostituzione)
            _sostituisci = AggiungiCampo(griglia, "Sostituisci c&on:", sostituisci);

        _maiuscole = new CheckBox { Text = "Distingui &maiuscole e minuscole", Checked = opzioni.MaiuscoleMinuscole, AutoSize = true };
        _parolaIntera = new CheckBox { Text = "Solo parole &intere", Checked = opzioni.ParolaIntera, AutoSize = true };
        griglia.Controls.Add(_maiuscole);
        griglia.SetColumnSpan(_maiuscole, 2);
        griglia.Controls.Add(_parolaIntera);
        griglia.SetColumnSpan(_parolaIntera, 2);
        _jolly = new CheckBox { Text = "Caratteri &jolly: ? un carattere, * più caratteri, # una cifra", Checked = opzioni.CaratteriJolly, AutoSize = true };
        griglia.Controls.Add(_jolly);
        griglia.SetColumnSpan(_jolly, 2);

        var pulsanti = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0, 12, 0, 0) };
        // In modalità Trova la ricerca la fa chi ha aperto la finestra, dopo la chiusura:
        // così l'annuncio del risultato non viene coperto dal ritorno del focus al documento.
        var trova = Pulsante(conSostituzione ? "&Trova successivo" : "&Trova", (_, _) =>
        {
            if (conSostituzione)
                TrovaRichiesto?.Invoke(this, EventArgs.Empty);
            else
                DialogResult = DialogResult.OK;
        });
        pulsanti.Controls.Add(trova);
        if (conSostituzione)
        {
            pulsanti.Controls.Add(Pulsante("&Sostituisci", (_, _) => SostituisciRichiesto?.Invoke(this, EventArgs.Empty)));
            pulsanti.Controls.Add(Pulsante("Sostituisci t&utto", (_, _) => SostituisciTuttoRichiesto?.Invoke(this, EventArgs.Empty)));
        }
        var chiudi = Pulsante("Chiudi", (_, _) => Close());
        chiudi.DialogResult = DialogResult.Cancel;
        pulsanti.Controls.Add(chiudi);
        griglia.Controls.Add(pulsanti);
        griglia.SetColumnSpan(pulsanti, 2);

        Controls.Add(griglia);
        AcceptButton = trova;
        CancelButton = chiudi;
        ActiveControl = _cerca;
        _cerca.SelectAll();
    }

    private static TextBox AggiungiCampo(TableLayoutPanel griglia, string etichetta, string valore)
    {
        // L'etichetta precede il campo nell'ordine di tabulazione: Windows la usa come nome accessibile
        // e la lettera sottolineata (Alt+lettera) porta il focus direttamente al campo.
        var label = new Label { Text = etichetta, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 6) };
        var campo = new TextBox { Text = valore, Dock = DockStyle.Fill, AccessibleName = etichetta.Replace("&", "").TrimEnd(':') };
        griglia.Controls.Add(label);
        griglia.Controls.Add(campo);
        return campo;
    }

    private static Button Pulsante(string testo, EventHandler clic)
    {
        var pulsante = new Button { Text = testo, AutoSize = true, Margin = new Padding(0, 0, 8, 0) };
        pulsante.Click += clic;
        return pulsante;
    }
}
