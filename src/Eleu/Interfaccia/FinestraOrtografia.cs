using Eleu.Correzione;

namespace Eleu.Interfaccia;

/// <summary>
/// Strumenti > Correzione > Controllo ortografico. Mostra un errore alla volta: la parola (o il motivo, per la
/// punteggiatura), la frase in cui sta, i suggerimenti e il campo "Cambia in". A ogni errore nuovo il focus
/// torna su "Cambia in", così lo screen reader legge subito la proposta; la frase si legge con Tab.
/// Invio = Cambia, Esc = Chiudi.
/// </summary>
public sealed class FinestraOrtografia : Form
{
    private readonly Label _errore;
    private readonly TextBox _contesto;
    private readonly ListBox _suggerimenti;
    private readonly TextBox _cambiaIn;
    private readonly Button _cambia, _cambiaTutti, _ignora, _ignoraTutti, _aggiungi, _chiudi;

    /// <summary>Cambia con il testo dato; true = tutte le volte che la parola compare.</summary>
    public event Action<string, bool>? Cambia;

    /// <summary>Lascia com'è; true = anche le altre volte, fino alla chiusura di Eleu.</summary>
    public event Action<bool>? Ignora;

    public event Action? AggiungiAlDizionario;

    public FinestraOrtografia()
    {
        Text = "Controllo ortografico";
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
        griglia.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 420));

        _errore = new Label { AutoSize = true, MaximumSize = new Size(560, 0), Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, 0, 0, 8) };
        griglia.Controls.Add(_errore);
        griglia.SetColumnSpan(_errore, 2);

        _contesto = Campo(griglia, "&Frase:", new TextBox { ReadOnly = true, Multiline = true, Height = 70, TabStop = true });
        _suggerimenti = Campo(griglia, "&Suggerimenti:", new ListBox { Height = 120, IntegralHeight = false });
        _cambiaIn = Campo(griglia, "Ca&mbia in:", new TextBox());
        _suggerimenti.SelectedIndexChanged += (_, _) =>
        {
            if (_suggerimenti.SelectedItem is string scelto)
                _cambiaIn.Text = scelto;
        };
        _suggerimenti.DoubleClick += (_, _) => FaiCambia(false);

        var pulsanti = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 12, 0, 0), MaximumSize = new Size(580, 0) };
        _cambia = Pulsante(pulsanti, "&Cambia", () => FaiCambia(false));
        _cambiaTutti = Pulsante(pulsanti, "Cambia &tutti", () => FaiCambia(true));
        _ignora = Pulsante(pulsanti, "&Ignora", () => Ignora?.Invoke(false));
        _ignoraTutti = Pulsante(pulsanti, "Ignora t&utti", () => Ignora?.Invoke(true));
        _aggiungi = Pulsante(pulsanti, "&Aggiungi al dizionario", () => AggiungiAlDizionario?.Invoke());
        _chiudi = Pulsante(pulsanti, "C&hiudi", Close);
        _chiudi.DialogResult = DialogResult.Cancel;
        griglia.Controls.Add(pulsanti);
        griglia.SetColumnSpan(pulsanti, 2);

        Controls.Add(griglia);
        AcceptButton = _cambia;
        CancelButton = _chiudi;
    }

    private void FaiCambia(bool tutti) => Cambia?.Invoke(_cambiaIn.Text, tutti);

    /// <summary>Mostra un errore. Il focus va su "Cambia in", con la proposta già scritta e selezionata.</summary>
    public void Mostra(ErroreProbabile errore, string contesto, IReadOnlyList<string> suggerimenti)
    {
        bool ortografia = errore.Motivo is null;
        _errore.Text = ortografia ? $"Non nel dizionario: {errore.Parola}" : $"{Maiuscola(errore.Motivo!)}: {Parlato.Di(errore.Parola)}";
        _contesto.Text = contesto;
        _suggerimenti.Items.Clear();
        foreach (var s in suggerimenti)
            _suggerimenti.Items.Add(s);
        _cambiaIn.Text = suggerimenti.Count > 0 ? suggerimenti[0] : errore.Parola;
        _cambiaTutti.Enabled = ortografia;
        _ignoraTutti.Enabled = true;
        _aggiungi.Enabled = ortografia;
        _cambia.Enabled = true;
        _ignora.Enabled = true;
        // Il nome accessibile del campo dice anche l'errore: lo screen reader lo legge prendendo il focus.
        _cambiaIn.AccessibleName = $"{_errore.Text}. Cambia in";
        _cambiaIn.Focus();
        _cambiaIn.SelectAll();
    }

    /// <summary>Niente più errori: lo dice e lascia solo Chiudi.</summary>
    public void Fine(string messaggio)
    {
        _errore.Text = messaggio;
        _contesto.Text = "";
        _suggerimenti.Items.Clear();
        _cambiaIn.Text = "";
        foreach (var b in new[] { _cambia, _cambiaTutti, _ignora, _ignoraTutti, _aggiungi })
            b.Enabled = false;
        _chiudi.AccessibleName = $"{messaggio} Chiudi";
        AcceptButton = _chiudi;
        _chiudi.Focus();
    }

    private static string Maiuscola(string testo) => testo.Length == 0 ? testo : char.ToUpper(testo[0]) + testo[1..];

    private static T Campo<T>(TableLayoutPanel griglia, string etichetta, T campo) where T : Control
    {
        griglia.Controls.Add(new Label { Text = etichetta, AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Top, Margin = new Padding(0, 6, 8, 6) });
        campo.Dock = DockStyle.Fill;
        campo.AccessibleName = etichetta.Replace("&", "").TrimEnd(':');
        griglia.Controls.Add(campo);
        return campo;
    }

    private static Button Pulsante(FlowLayoutPanel pannello, string testo, Action azione)
    {
        var pulsante = new Button { Text = testo, AutoSize = true, Padding = new Padding(8, 2, 8, 2) };
        pulsante.Click += (_, _) => azione();
        pannello.Controls.Add(pulsante);
        return pulsante;
    }
}

/// <summary>Strumenti > Correzione > Dizionario personale: le parole da non segnalare, da aggiungere o togliere.</summary>
public sealed class FinestraDizionario : Modulo
{
    private readonly ListBox _parole;
    private readonly TextBox _nuova;

    public FinestraDizionario(IEnumerable<string> parole) : base("Dizionario personale", 300)
    {
        _nuova = Campo("&Nuova parola:", new TextBox());
        var aggiungi = new Button { Text = "&Aggiungi", AutoSize = true };
        RigaIntera(aggiungi);
        _parole = Campo("&Parole:", new ListBox { Height = 200, IntegralHeight = false, Sorted = true });
        foreach (var p in parole)
            _parole.Items.Add(p);
        var togli = new Button { Text = "&Togli la parola scelta", AutoSize = true };
        RigaIntera(togli);
        AggiungiPulsanti();
        ActiveControl = _nuova;

        aggiungi.Click += (_, _) =>
        {
            var parola = _nuova.Text.Trim();
            if (parola.Length > 0 && !_parole.Items.Cast<string>().Contains(parola, StringComparer.CurrentCultureIgnoreCase))
                _parole.Items.Add(parola);
            _nuova.Clear();
            _nuova.Focus();
        };
        togli.Click += (_, _) =>
        {
            if (_parole.SelectedIndex < 0)
                return;
            int indice = _parole.SelectedIndex;
            _parole.Items.RemoveAt(indice);
            if (_parole.Items.Count > 0)
                _parole.SelectedIndex = Math.Min(indice, _parole.Items.Count - 1);
            _parole.Focus();
        };
        // Invio nel campo aggiunge la parola, non chiude la finestra.
        _nuova.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                aggiungi.PerformClick();
            }
        };
        _parole.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Delete)
                togli.PerformClick();
        };
    }

    public List<string> Parole => _parole.Items.Cast<string>().ToList();
}
