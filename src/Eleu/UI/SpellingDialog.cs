using Eleu.Proofing;

namespace Eleu.UI;

/// <summary>
/// Strumenti > Correzione > Controllo ortografico. Shows one error at a time: the word (or the reason, for
/// punctuation), the sentence it is in, the suggestions and the "Cambia in" field. At every new error the focus
/// returns to "Cambia in", so the screen reader reads the proposal right away; the sentence is read with Tab.
/// Enter = Change, Esc = Close.
/// </summary>
public sealed class SpellingDialog : Form
{
    private readonly Label _error;
    private readonly TextBox _context;
    private readonly ListBox _suggestions;
    private readonly TextBox _changeTo;
    private readonly Button _change, _changeAll, _ignore, _ignoreAll, _add, _close;

    /// <summary>Change to the given text; true = every time the word appears.</summary>
    public event Action<string, bool>? Change;

    /// <summary>Leave it as it is; true = the other times too, until Eleu is closed.</summary>
    public event Action<bool>? Ignore;

    public event Action? AddToDictionary;

    public SpellingDialog()
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

        var grid = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Fill };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 420));

        _error = new Label { AutoSize = true, MaximumSize = new Size(560, 0), Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, 0, 0, 8) };
        grid.Controls.Add(_error);
        grid.SetColumnSpan(_error, 2);

        _context = Field(grid, "&Frase:", new TextBox { ReadOnly = true, Multiline = true, Height = 70, TabStop = true });
        _suggestions = Field(grid, "&Suggerimenti:", new ListBox { Height = 120, IntegralHeight = false });
        _changeTo = Field(grid, "Ca&mbia in:", new TextBox());
        _suggestions.SelectedIndexChanged += (_, _) =>
        {
            if (_suggestions.SelectedItem is string chosen)
                _changeTo.Text = chosen;
        };
        _suggestions.DoubleClick += (_, _) => DoChange(false);

        var buttons = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 12, 0, 0), MaximumSize = new Size(580, 0) };
        _change = Button(buttons, "&Cambia", () => DoChange(false));
        _changeAll = Button(buttons, "Cambia &tutti", () => DoChange(true));
        _ignore = Button(buttons, "&Ignora", () => Ignore?.Invoke(false));
        _ignoreAll = Button(buttons, "Ignora t&utti", () => Ignore?.Invoke(true));
        _add = Button(buttons, "&Aggiungi al dizionario", () => AddToDictionary?.Invoke());
        _close = Button(buttons, "C&hiudi", Close);
        _close.DialogResult = DialogResult.Cancel;
        grid.Controls.Add(buttons);
        grid.SetColumnSpan(buttons, 2);

        Controls.Add(grid);
        AcceptButton = _change;
        CancelButton = _close;
    }

    private void DoChange(bool all) => Change?.Invoke(_changeTo.Text, all);

    /// <summary>Shows an error. The focus goes to "Cambia in", with the proposal already written and selected.</summary>
    public void ShowError(LikelyError error, string context, IReadOnlyList<string> suggestions)
    {
        bool spelling = error.Reason is null;
        _error.Text = spelling ? $"Non nel dizionario: {error.Word}" : $"{Capital(error.Reason!)}: {Spoken.Of(error.Word)}";
        _context.Text = context;
        _suggestions.Items.Clear();
        foreach (var s in suggestions)
            _suggestions.Items.Add(s);
        _changeTo.Text = suggestions.Count > 0 ? suggestions[0] : error.Word;
        _changeAll.Enabled = spelling;
        _ignoreAll.Enabled = true;
        _add.Enabled = spelling;
        _change.Enabled = true;
        _ignore.Enabled = true;
        // The field's accessible name also says the error: the screen reader reads it when taking the focus.
        _changeTo.AccessibleName = $"{_error.Text}. Cambia in";
        _changeTo.Focus();
        _changeTo.SelectAll();
    }

    /// <summary>No more errors: says so and leaves only Close.</summary>
    public void End(string message)
    {
        _error.Text = message;
        _context.Text = "";
        _suggestions.Items.Clear();
        _changeTo.Text = "";
        foreach (var b in new[] { _change, _changeAll, _ignore, _ignoreAll, _add })
            b.Enabled = false;
        _close.AccessibleName = $"{message} Chiudi";
        AcceptButton = _close;
        _close.Focus();
    }

    private static string Capital(string text) => text.Length == 0 ? text : char.ToUpper(text[0]) + text[1..];

    private static T Field<T>(TableLayoutPanel grid, string label, T field) where T : Control
    {
        grid.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Top, Margin = new Padding(0, 6, 8, 6) });
        field.Dock = DockStyle.Fill;
        field.AccessibleName = label.Replace("&", "").TrimEnd(':');
        grid.Controls.Add(field);
        return field;
    }

    private static Button Button(FlowLayoutPanel panel, string text, Action action)
    {
        var button = new Button { Text = text, AutoSize = true, Padding = new Padding(8, 2, 8, 2) };
        button.Click += (_, _) => action();
        panel.Controls.Add(button);
        return button;
    }
}

/// <summary>Strumenti > Correzione > Dizionario personale: the words not to report, to add or remove.</summary>
public sealed class DictionaryDialog : DialogBase
{
    private readonly ListBox _words;
    private readonly TextBox _new;

    public DictionaryDialog(IEnumerable<string> words) : base("Dizionario personale", 300)
    {
        _new = Field("&Nuova parola:", new TextBox());
        var add = new Button { Text = "&Aggiungi", AutoSize = true };
        WholeLine(add);
        _words = Field("&Parole:", new ListBox { Height = 200, IntegralHeight = false, Sorted = true });
        foreach (var p in words)
            _words.Items.Add(p);
        var remove = new Button { Text = "&Togli la parola scelta", AutoSize = true };
        WholeLine(remove);
        AddButtons();
        ActiveControl = _new;

        add.Click += (_, _) =>
        {
            var word = _new.Text.Trim();
            if (word.Length > 0 && !_words.Items.Cast<string>().Contains(word, StringComparer.CurrentCultureIgnoreCase))
                _words.Items.Add(word);
            _new.Clear();
            _new.Focus();
        };
        remove.Click += (_, _) =>
        {
            if (_words.SelectedIndex < 0)
                return;
            int index = _words.SelectedIndex;
            _words.Items.RemoveAt(index);
            if (_words.Items.Count > 0)
                _words.SelectedIndex = Math.Min(index, _words.Items.Count - 1);
            _words.Focus();
        };
        // Enter in the field adds the word, it does not close the window.
        _new.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                add.PerformClick();
            }
        };
        _words.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Delete)
                remove.PerformClick();
        };
    }

    public List<string> Words => _words.Items.Cast<string>().ToList();
}
