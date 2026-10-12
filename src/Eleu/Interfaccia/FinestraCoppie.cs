using Eleu.Correzione;
using Eleu.Voce;

namespace Eleu.Interfaccia;

/// <summary>
/// Un elenco di coppie da gestire (aggiungi, togli): "cerca → sostituisci con", "parola → si dice".
/// Due campi per la coppia nuova, Invio nel secondo la aggiunge; nell'elenco Canc la toglie.
/// </summary>
public class FinestraCoppie : Modulo
{
    private readonly TextBox _prima, _seconda;
    private readonly ListView _elenco;

    protected FinestraCoppie(string titolo, string etichettaPrima, string etichettaSeconda, IEnumerable<(string, string)> coppie)
        : base(titolo, 380)
    {
        _prima = Campo(etichettaPrima, new TextBox());
        _seconda = Campo(etichettaSeconda, new TextBox());
        var aggiungi = RigaIntera(new Button { Text = "&Aggiungi all'elenco", AutoSize = true });
        _elenco = Campo("&Elenco:", new ListView
        {
            View = View.Details,
            FullRowSelect = true,
            HideSelection = false,
            Height = 180,
            HeaderStyle = ColumnHeaderStyle.Nonclickable,
        });
        _elenco.Columns.Add(etichettaPrima.Replace("&", "").TrimEnd(':'), 170);
        _elenco.Columns.Add(etichettaSeconda.Replace("&", "").TrimEnd(':'), 170);
        foreach (var (a, b) in coppie)
            Aggiungi(a, b);
        var togli = RigaIntera(new Button { Text = "&Togli la coppia scelta", AutoSize = true });
        ActiveControl = _prima;

        aggiungi.Click += (_, _) =>
        {
            if (_prima.Text.Trim().Length == 0)
            {
                _prima.Focus();
                return;
            }
            Aggiungi(_prima.Text, _seconda.Text);
            _prima.Clear();
            _seconda.Clear();
            _prima.Focus();
        };
        togli.Click += (_, _) =>
        {
            if (_elenco.SelectedItems.Count == 0)
                return;
            int indice = _elenco.SelectedIndices[0];
            _elenco.Items.RemoveAt(indice);
            if (_elenco.Items.Count > 0)
                _elenco.Items[Math.Min(indice, _elenco.Items.Count - 1)].Selected = true;
            _elenco.Focus();
        };
        _elenco.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Delete)
                togli.PerformClick();
        };
        // Invio nel secondo campo aggiunge la coppia, non chiude la finestra.
        _seconda.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                aggiungi.PerformClick();
            }
        };
    }

    private void Aggiungi(string prima, string seconda) =>
        _elenco.Items.Add(new ListViewItem([prima, seconda.Length > 0 ? seconda : "(niente)"]) { Tag = (prima, seconda) });

    protected List<(string Prima, string Seconda)> Coppie =>
        _elenco.Items.Cast<ListViewItem>().Select(v => ((string, string))v.Tag!).ToList();
}

/// <summary>
/// Strumenti > Sostituzioni multiple: coppie "cerca → sostituisci con" da applicare tutte in un colpo (per esempio
/// per correggere sempre gli stessi errori). L'elenco resta per la prossima volta.
/// </summary>
public sealed class FinestraSostituzioni : FinestraCoppie
{
    private readonly CheckBox _paroleIntere;

    public FinestraSostituzioni(IEnumerable<CoppiaSostituzione> coppie, bool paroleIntere)
        : base("Sostituzioni multiple", "&Cerca:", "Sostituisci c&on:", coppie.Select(c => (c.Cerca, c.Sostituisci)))
    {
        _paroleIntere = RigaIntera(new CheckBox { Text = "Solo parole &intere", Checked = paroleIntere, AutoSize = true });
        RigaIntera(new Label
        {
            AutoSize = true,
            MaximumSize = new Size(500, 0),
            Text = "Con OK si fanno tutte le sostituzioni dell'elenco nel documento. Maiuscole e minuscole contano.",
        });
        AggiungiPulsanti();
    }

    public new List<CoppiaSostituzione> Coppie => base.Coppie.Select(c => new CoppiaSostituzione(c.Prima, c.Seconda)).ToList();

    public bool ParoleIntere => _paroleIntere.Checked;
}

/// <summary>
/// Voce > Pronunce: come la voce deve dire certe parole o sigle ("NVDA" → "enne vu di a"). Vale per la lettura
/// e per gli audiolibri; maiuscole e minuscole non contano.
/// </summary>
public sealed class FinestraPronunce : FinestraCoppie
{
    public FinestraPronunce(IEnumerable<Pronuncia> pronunce)
        : base("Pronunce", "&Parola o sigla:", "&Si dice:", pronunce.Select(p => (p.Parola, p.Come)))
    {
        RigaIntera(new Label
        {
            AutoSize = true,
            MaximumSize = new Size(500, 0),
            Text = "Scrivi la parola come appare nel testo e, sotto, come la voce deve dirla. Vale per la lettura e gli audiolibri.",
        });
        AggiungiPulsanti();
    }

    public List<Pronuncia> Pronunce => Coppie.Select(c => new Pronuncia(c.Prima.Trim(), c.Seconda)).ToList();
}
