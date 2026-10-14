using Eleu.Documents;

namespace Eleu.UI;

/// <summary>
/// Find / Replace window. In Find mode, Enter searches and closes (then F3 continues);
/// in Replace mode it stays open until Esc is pressed.
/// </summary>
public sealed class FindDialog : Form
{
    private readonly TextBox _find;
    private readonly TextBox? _replace;
    private readonly CheckBox _uppercase;
    private readonly CheckBox _wholeWord;
    private readonly CheckBox _wildcards;

    public event EventHandler? FindRequested;
    public event EventHandler? ReplaceRequested;
    public event EventHandler? ReplaceAllRequested;

    public string SearchText => _find.Text;

    public string ReplacementText => _replace?.Text ?? "";

    public SearchOptions Options => new(_uppercase.Checked, _wholeWord.Checked, _wildcards.Checked);

    public FindDialog(bool withReplacement, string find, string replace, SearchOptions options)
    {
        Text = withReplacement ? "Sostituisci" : "Trova";
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
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 320));

        _find = AddField(grid, "&Cerca:", find);
        if (withReplacement)
            _replace = AddField(grid, "Sostituisci c&on:", replace);

        _uppercase = new CheckBox { Text = "Distingui &maiuscole e minuscole", Checked = options.MatchCase, AutoSize = true };
        _wholeWord = new CheckBox { Text = "Solo parole &intere", Checked = options.WholeWord, AutoSize = true };
        grid.Controls.Add(_uppercase);
        grid.SetColumnSpan(_uppercase, 2);
        grid.Controls.Add(_wholeWord);
        grid.SetColumnSpan(_wholeWord, 2);
        _wildcards = new CheckBox { Text = "Caratteri &jolly: ? un carattere, * più caratteri, # una cifra", Checked = options.Wildcards, AutoSize = true };
        grid.Controls.Add(_wildcards);
        grid.SetColumnSpan(_wildcards, 2);

        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0, 12, 0, 0) };
        // In Find mode the search is done by whoever opened the window, after it closes:
        // so the announcement of the result is not covered by the focus returning to the document.
        var findButton = Button(withReplacement ? "&Trova successivo" : "&Trova", (_, _) =>
        {
            if (withReplacement)
                FindRequested?.Invoke(this, EventArgs.Empty);
            else
                DialogResult = DialogResult.OK;
        });
        buttons.Controls.Add(findButton);
        if (withReplacement)
        {
            buttons.Controls.Add(Button("&Sostituisci", (_, _) => ReplaceRequested?.Invoke(this, EventArgs.Empty)));
            buttons.Controls.Add(Button("Sostituisci t&utto", (_, _) => ReplaceAllRequested?.Invoke(this, EventArgs.Empty)));
        }
        var close = Button("Chiudi", (_, _) => Close());
        close.DialogResult = DialogResult.Cancel;
        buttons.Controls.Add(close);
        grid.Controls.Add(buttons);
        grid.SetColumnSpan(buttons, 2);

        Controls.Add(grid);
        AcceptButton = findButton;
        CancelButton = close;
        ActiveControl = _find;
        _find.SelectAll();
    }

    private static TextBox AddField(TableLayoutPanel grid, string labelText, string @value)
    {
        // The label precedes the field in tab order: Windows uses it as the accessible name
        // and the underlined letter (Alt+letter) moves the focus straight to the field.
        var label = new Label { Text = labelText, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 6) };
        var field = new TextBox { Text = @value, Dock = DockStyle.Fill, AccessibleName = labelText.Replace("&", "").TrimEnd(':') };
        grid.Controls.Add(label);
        grid.Controls.Add(field);
        return field;
    }

    private static Button Button(string text, EventHandler click)
    {
        var button = new Button { Text = text, AutoSize = true, Margin = new Padding(0, 0, 8, 0) };
        button.Click += click;
        return button;
    }
}
