using Eleu.Braille;

namespace Eleu.Interfaccia;

/// <summary>
/// Una sola finestra per il Braille: dove mandarlo (un file BRF o una stampante Braille), il formato della pagina
/// (celle, righe, numero di pagina, titolo in cima, margine, pagine a stampa, sommario) e quali pagine.
/// Dice subito quante pagine Braille verranno, e lo ridice a ogni cambiamento.
/// </summary>
public sealed class FinestraBraille : Form
{
    public const string VoceFile = "File BRF (da salvare)";

    private static readonly string[] PosizioniNumero = ["Nessuno", "In alto a destra", "In basso a destra"];

    private readonly ComboBox _destinazione, _numero;
    private readonly NumericUpDown _celle, _righe, _margine, _da, _a;
    private readonly CheckBox _titolo, _pagineStampa, _sommario, _interlinea, _fronteRetro, _piega;
    private readonly Label _pagine;
    private readonly Func<FormatoPagina, int> _contaPagine;
    private readonly string? _titoloInCelle;

    /// <summary>Nome della stampante scelta, oppure null per salvare un file BRF.</summary>
    public string? Stampante => _destinazione.SelectedItem is string scelta && scelta != VoceFile ? scelta : null;

    public FormatoPagina Formato => new((int)_celle.Value, (int)_righe.Value)
    {
        Numero = (NumeroPaginaBraille)Math.Max(0, _numero.SelectedIndex),
        Titolo = _titolo.Checked ? _titoloInCelle : null,
        MargineSinistro = (int)_margine.Value,
        PagineStampa = _pagineStampa.Checked,
        Sommario = _sommario.Checked,
        InterlineaDoppia = _interlinea.Checked,
        FronteRetro = _fronteRetro.Checked,
        PiegaAMeta = _piega.Checked,
    };

    /// <summary>Le pagine da mandare, da 1.</summary>
    public (int Da, int A) Intervallo => ((int)_da.Value, (int)Math.Max(_da.Value, _a.Value));

    /// <param name="titoloInCelle">Il titolo del documento già in Braille, per la riga in cima a ogni pagina.</param>
    public FinestraBraille(IReadOnlyList<string> stampanti, string? stampanteScelta, FormatoPagina formato, Func<FormatoPagina, int> contaPagine,
        string titolo = "Braille", bool conFile = true, string? titoloInCelle = null)
    {
        _contaPagine = contaPagine;
        _titoloInCelle = titoloInCelle;
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

        var griglia = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Fill };
        griglia.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        griglia.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 340));

        _destinazione = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, AccessibleName = "Invia a" };
        if (conFile)
            _destinazione.Items.Add(VoceFile);
        _destinazione.Items.AddRange(stampanti.Cast<object>().ToArray());
        int indice = stampanteScelta is null ? 0 : _destinazione.Items.IndexOf(stampanteScelta);
        _destinazione.SelectedIndex = Math.Max(0, indice);
        AggiungiCampo(griglia, "&Invia a:", _destinazione);

        _celle = Numero(formato.CellePerRiga, FormatoPagina.MinimoCelle, FormatoPagina.MassimoCelle, "Celle per riga");
        AggiungiCampo(griglia, "&Celle per riga:", _celle);
        _righe = Numero(formato.RighePerPagina, FormatoPagina.MinimoRighe, FormatoPagina.MassimoRighe, "Righe per pagina");
        AggiungiCampo(griglia, "&Righe per pagina:", _righe);
        _margine = Numero(formato.MargineSinistro, 0, 10, "Margine per la rilegatura, in celle");
        AggiungiCampo(griglia, "&Margine per la rilegatura (celle):", _margine);

        _numero = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, AccessibleName = "Numero di pagina" };
        _numero.Items.AddRange(PosizioniNumero);
        _numero.SelectedIndex = (int)formato.Numero;
        AggiungiCampo(griglia, "&Numero di pagina:", _numero);

        _titolo = Casella(griglia, "&Titolo del documento in cima a ogni pagina", formato.Titolo is not null && titoloInCelle is not null);
        _titolo.Enabled = titoloInCelle is not null;
        _pagineStampa = Casella(griglia, "Segna dove cambia la pagina a &stampa", formato.PagineStampa);
        _sommario = Casella(griglia, "S&ommario all'inizio, con le pagine Braille", formato.Sommario);
        _interlinea = Casella(griglia, "Inter&linea doppia (una riga vuota tra una riga e l'altra)", formato.InterlineaDoppia);
        _fronteRetro = Casella(griglia, "Stampa &fronte e retro (sul retro margine e numero passano dalla parte giusta)", formato.FronteRetro);
        _piega = Casella(griglia, "Due righe libere a metà pagina, per &piegare il foglio senza schiacciare i punti", formato.PiegaAMeta);

        _da = Numero(1, 1, 9999, "Da pagina");
        AggiungiCampo(griglia, "&Da pagina:", _da);
        _a = Numero(9999, 1, 9999, "A pagina");
        AggiungiCampo(griglia, "&A pagina:", _a);

        _pagine = new Label { AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
        griglia.Controls.Add(_pagine);
        griglia.SetColumnSpan(_pagine, 2);
        foreach (var n in new[] { _celle, _righe, _margine })
            n.ValueChanged += (_, _) => AggiornaPagine();
        _numero.SelectedIndexChanged += (_, _) => AggiornaPagine();
        foreach (var c in new[] { _titolo, _pagineStampa, _sommario, _interlinea, _fronteRetro, _piega })
            c.CheckedChanged += (_, _) => AggiornaPagine();
        AggiornaPagine(primaVolta: true);

        var pulsanti = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 12, 0, 0) };
        var ok = new Button { Text = "OK", AutoSize = true, DialogResult = DialogResult.OK };
        var annulla = new Button { Text = "Annulla", AutoSize = true, DialogResult = DialogResult.Cancel };
        pulsanti.Controls.AddRange([ok, annulla]);
        griglia.Controls.Add(pulsanti);
        griglia.SetColumnSpan(pulsanti, 2);

        Controls.Add(griglia);
        AcceptButton = ok;
        CancelButton = annulla;
        ActiveControl = _destinazione;
    }

    private void AggiornaPagine(bool primaVolta = false)
    {
        int pagine = _contaPagine(Formato);
        _pagine.Text = pagine == 1 ? "Il documento occupa 1 pagina Braille." : $"Il documento occupa {pagine} pagine Braille.";
        // L'intervallo segue il numero di pagine: se si prendeva fino all'ultima, si continua a prendere fino all'ultima.
        bool finoAllUltima = primaVolta || _a.Value >= _a.Maximum;
        _da.Maximum = Math.Max(1, pagine);
        _a.Maximum = Math.Max(1, pagine);
        if (finoAllUltima || _a.Value > pagine)
            _a.Value = Math.Max(1, pagine);
        _pagine.AccessibleName = _pagine.Text;
    }

    private static NumericUpDown Numero(int valore, int minimo, int massimo, string nome) =>
        new() { Minimum = minimo, Maximum = massimo, Value = Math.Clamp(valore, minimo, massimo), AccessibleName = nome };

    private static CheckBox Casella(TableLayoutPanel griglia, string testo, bool spuntata)
    {
        var casella = new CheckBox { Text = testo, Checked = spuntata, AutoSize = true };
        griglia.Controls.Add(casella);
        griglia.SetColumnSpan(casella, 2);
        return casella;
    }

    private static void AggiungiCampo(TableLayoutPanel griglia, string etichetta, Control campo)
    {
        // L'etichetta precede il campo: Windows la usa come nome e Alt+lettera porta al campo.
        griglia.Controls.Add(new Label { Text = etichetta, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 6) });
        griglia.Controls.Add(campo);
    }
}
