namespace Eleu.Interfaccia;

/// <summary>Una riga dell'elenco: le colonne da mostrare e la posizione nel documento.</summary>
public sealed record VoceElenco(IReadOnlyList<string> Colonne, int Posizione);

/// <summary>
/// "Elenco..." per gli elementi del documento (interruzioni di pagina, titoli, ...):
/// l'elenco di tutti gli elementi di quel tipo, con Vai (Invio) ed Elimina (Canc).
/// Per chi non vede è il modo di sapere dove sono elementi altrimenti invisibili.
/// </summary>
public sealed class FinestraElenco : Form
{
    private readonly ListView _elenco;
    private readonly List<VoceElenco> _voci;

    /// <summary>Le posizioni degli elementi da eliminare, scelte con Elimina.</summary>
    public List<int> DaEliminare { get; } = [];

    /// <summary>La posizione a cui andare, se si è scelto Vai.</summary>
    public int? Destinazione { get; private set; }

    public FinestraElenco(string titolo, IReadOnlyList<string> colonne, IReadOnlyList<VoceElenco> voci, bool eliminabile = true)
    {
        _voci = voci.ToList();
        Text = titolo;
        Width = 620;
        Height = 440;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = false;
        Font = new Font("Segoe UI", 11f);
        Padding = new Padding(12);

        _elenco = new ListView
        {
            View = View.Details,
            FullRowSelect = true,
            HideSelection = false,
            MultiSelect = true,
            Dock = DockStyle.Fill,
            AccessibleName = titolo,
        };
        foreach (var colonna in colonne)
            _elenco.Columns.Add(colonna, colonna == colonne[0] ? 400 : 160);
        Riempi();

        var pulsanti = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
        var vai = new Button { Text = "&Vai", AutoSize = true };
        vai.Click += (_, _) => Vai();
        var elimina = new Button { Text = "&Elimina", AutoSize = true, Visible = eliminabile };
        elimina.Click += (_, _) => Elimina();
        var chiudi = new Button { Text = "Chiudi", AutoSize = true, DialogResult = DialogResult.Cancel };
        pulsanti.Controls.AddRange([vai, elimina, chiudi]);

        var vuoto = new Label { Text = "Nessun elemento nel documento.", AutoSize = true, Dock = DockStyle.Top, Visible = _voci.Count == 0 };
        Controls.Add(_elenco);
        Controls.Add(vuoto);
        Controls.Add(pulsanti);
        AcceptButton = vai;
        CancelButton = chiudi;
        ActiveControl = _elenco;

        _elenco.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Delete && eliminabile)
            {
                Elimina();
                e.Handled = true;
            }
        };
        _elenco.ItemActivate += (_, _) => Vai();
    }

    private void Riempi()
    {
        _elenco.Items.Clear();
        foreach (var voce in _voci)
            _elenco.Items.Add(new ListViewItem(voce.Colonne.ToArray()));
        if (_elenco.Items.Count > 0)
        {
            _elenco.Items[0].Selected = true;
            _elenco.Items[0].Focused = true;
        }
    }

    private void Vai()
    {
        if (_elenco.SelectedIndices.Count == 0)
            return;
        Destinazione = _voci[_elenco.SelectedIndices[0]].Posizione;
        DialogResult = DialogResult.OK;
    }

    private void Elimina()
    {
        var scelte = _elenco.SelectedIndices.Cast<int>().OrderDescending().ToList();
        if (scelte.Count == 0)
            return;
        foreach (int indice in scelte)
        {
            DaEliminare.Add(_voci[indice].Posizione);
            _voci.RemoveAt(indice);
        }
        int prossimo = Math.Min(scelte[^1], _voci.Count - 1);
        Riempi();
        if (prossimo >= 0)
        {
            _elenco.SelectedIndices.Clear();
            _elenco.Items[prossimo].Selected = true;
            _elenco.Items[prossimo].Focused = true;
        }
    }
}
