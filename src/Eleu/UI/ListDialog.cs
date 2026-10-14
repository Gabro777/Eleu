namespace Eleu.UI;

/// <summary>A list row: the columns to show and the position in the document.</summary>
public sealed record ListItem(IReadOnlyList<string> Columns, int Position);

/// <summary>
/// "Elenco..." for document elements (page breaks, headings, ...):
/// the list of all elements of that kind, with Go (Enter) and Delete (Del).
/// For blind users it is the way to know where otherwise invisible elements are.
/// </summary>
public sealed class ListDialog : Form
{
    private readonly ListView _list;
    private readonly List<ListItem> _items;

    /// <summary>The positions of the elements to delete, chosen with Delete.</summary>
    public List<int> ToDelete { get; } = [];

    /// <summary>The position to go to, if Go was chosen.</summary>
    public int? Target { get; private set; }

    public ListDialog(string heading, IReadOnlyList<string> columns, IReadOnlyList<ListItem> items, bool deletable = true)
    {
        _items = items.ToList();
        Text = heading;
        Width = 620;
        Height = 440;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = false;
        Font = new Font("Segoe UI", 11f);
        Padding = new Padding(12);

        _list = new ListView
        {
            View = View.Details,
            FullRowSelect = true,
            HideSelection = false,
            MultiSelect = true,
            Dock = DockStyle.Fill,
            AccessibleName = heading,
        };
        foreach (var column in columns)
            _list.Columns.Add(column, column == columns[0] ? 400 : 160);
        Fill();

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
        var go = new Button { Text = "&Vai", AutoSize = true };
        go.Click += (_, _) => Go();
        var delete = new Button { Text = "&Elimina", AutoSize = true, Visible = deletable };
        delete.Click += (_, _) => Delete();
        var close = new Button { Text = "Chiudi", AutoSize = true, DialogResult = DialogResult.Cancel };
        buttons.Controls.AddRange([go, delete, close]);

        var empty = new Label { Text = "Nessun elemento nel documento.", AutoSize = true, Dock = DockStyle.Top, Visible = _items.Count == 0 };
        Controls.Add(_list);
        Controls.Add(empty);
        Controls.Add(buttons);
        AcceptButton = go;
        CancelButton = close;
        ActiveControl = _list;

        _list.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Delete && deletable)
            {
                Delete();
                e.Handled = true;
            }
        };
        _list.ItemActivate += (_, _) => Go();
    }

    private void Fill()
    {
        _list.Items.Clear();
        foreach (var item in _items)
            _list.Items.Add(new ListViewItem(item.Columns.ToArray()));
        if (_list.Items.Count > 0)
        {
            _list.Items[0].Selected = true;
            _list.Items[0].Focused = true;
        }
    }

    private void Go()
    {
        if (_list.SelectedIndices.Count == 0)
            return;
        Target = _items[_list.SelectedIndices[0]].Position;
        DialogResult = DialogResult.OK;
    }

    private void Delete()
    {
        var choices = _list.SelectedIndices.Cast<int>().OrderDescending().ToList();
        if (choices.Count == 0)
            return;
        foreach (int index in choices)
        {
            ToDelete.Add(_items[index].Position);
            _items.RemoveAt(index);
        }
        int next = Math.Min(choices[^1], _items.Count - 1);
        Fill();
        if (next >= 0)
        {
            _list.SelectedIndices.Clear();
            _list.Items[next].Selected = true;
            _list.Items[next].Focused = true;
        }
    }
}
