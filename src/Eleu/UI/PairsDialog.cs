using Eleu.Proofing;
using Eleu.Speech;

namespace Eleu.UI;

/// <summary>
/// A list of pairs to manage (add, remove): "find → replace with", "word → is said as".
/// Two fields for the new pair, Enter in the second adds it; in the list Del removes it.
/// </summary>
public class PairsDialog : DialogBase
{
    private readonly TextBox _before, _second;
    private readonly ListView _list;

    protected PairsDialog(string heading, string firstLabel, string secondLabel, IEnumerable<(string, string)> pairs)
        : base(heading, 380)
    {
        _before = Field(firstLabel, new TextBox());
        _second = Field(secondLabel, new TextBox());
        var add = WholeLine(new Button { Text = "&Aggiungi all'elenco", AutoSize = true });
        _list = Field("&Elenco:", new ListView
        {
            View = View.Details,
            FullRowSelect = true,
            HideSelection = false,
            Height = 180,
            HeaderStyle = ColumnHeaderStyle.Nonclickable,
        });
        _list.Columns.Add(firstLabel.Replace("&", "").TrimEnd(':'), 170);
        _list.Columns.Add(secondLabel.Replace("&", "").TrimEnd(':'), 170);
        foreach (var (a, b) in pairs)
            Add(a, b);
        var remove = WholeLine(new Button { Text = "&Togli la coppia scelta", AutoSize = true });
        ActiveControl = _before;

        add.Click += (_, _) =>
        {
            if (_before.Text.Trim().Length == 0)
            {
                _before.Focus();
                return;
            }
            Add(_before.Text, _second.Text);
            _before.Clear();
            _second.Clear();
            _before.Focus();
        };
        remove.Click += (_, _) =>
        {
            if (_list.SelectedItems.Count == 0)
                return;
            int index = _list.SelectedIndices[0];
            _list.Items.RemoveAt(index);
            if (_list.Items.Count > 0)
                _list.Items[Math.Min(index, _list.Items.Count - 1)].Selected = true;
            _list.Focus();
        };
        _list.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Delete)
                remove.PerformClick();
        };
        // Enter in the second field adds the pair, it does not close the window.
        _second.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                add.PerformClick();
            }
        };
    }

    private void Add(string before, string second) =>
        _list.Items.Add(new ListViewItem([before, second.Length > 0 ? second : "(niente)"]) { Tag = (before, second) });

    protected List<(string First, string Second)> Pairs =>
        _list.Items.Cast<ListViewItem>().Select(v => ((string, string))v.Tag!).ToList();
}

/// <summary>
/// Strumenti > Sostituzioni multiple: "find → replace with" pairs to apply all at once (for example
/// to always fix the same errors). The list is kept for next time.
/// </summary>
public sealed class ReplacementsDialog : PairsDialog
{
    private readonly CheckBox _wholeWords;

    public ReplacementsDialog(IEnumerable<ReplacementPair> pairs, bool wholeWords)
        : base("Sostituzioni multiple", "&Cerca:", "Sostituisci c&on:", pairs.Select(c => (c.Find, c.Replace)))
    {
        _wholeWords = WholeLine(new CheckBox { Text = "Solo parole &intere", Checked = wholeWords, AutoSize = true });
        WholeLine(new Label
        {
            AutoSize = true,
            MaximumSize = new Size(500, 0),
            Text = "Con OK si fanno tutte le sostituzioni dell'elenco nel documento. Maiuscole e minuscole contano.",
        });
        AddButtons();
    }

    public new List<ReplacementPair> Pairs => base.Pairs.Select(c => new ReplacementPair(c.First, c.Second)).ToList();

    public bool WholeWords => _wholeWords.Checked;
}

/// <summary>
/// Voce > Pronunce: how the voice must say certain words or acronyms ("NVDA" → "enne vu di a"). Applies to reading
/// and to audiobooks; case does not matter.
/// </summary>
public sealed class PronunciationsDialog : PairsDialog
{
    public PronunciationsDialog(IEnumerable<Pronunciation> pronunciations)
        : base("Pronunce", "&Parola o sigla:", "&Si dice:", pronunciations.Select(p => (p.Word, p.SpokenAs)))
    {
        WholeLine(new Label
        {
            AutoSize = true,
            MaximumSize = new Size(500, 0),
            Text = "Scrivi la parola come appare nel testo e, sotto, come la voce deve dirla. Vale per la lettura e gli audiolibri.",
        });
        AddButtons();
    }

    public List<Pronunciation> Pronunciations => Pairs.Select(c => new Pronunciation(c.First.Trim(), c.Second)).ToList();
}
