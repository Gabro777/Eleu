namespace Eleu.UI;

/// <summary>A command of the menus: its path ("Formato > Grassetto"), its default keys and the ones in use.</summary>
public sealed record ShortcutCommand(string Path, Keys Default, Keys Current);

/// <summary>
/// Strumenti > Cambia le scorciatoie da tastiera: every command of the menus with its keys. Cambia records a new combination (Eleu asks
/// before taking it from another command), Togli la scorciatoia leaves the command without one, Ripristina goes back to the
/// default, for one command or for all. The combinations Eleu keeps for itself cannot be taken.
/// </summary>
public sealed class ShortcutsDialog : Form
{
    private readonly List<ShortcutCommand> _commands;
    private readonly Dictionary<Keys, string> _reserved;
    private readonly Dictionary<string, Keys> _keys;
    private readonly ListView _list;

    /// <param name="reserved">Combinations Eleu keeps for itself, with what they do.</param>
    public ShortcutsDialog(IReadOnlyList<ShortcutCommand> commands, IReadOnlyDictionary<Keys, string> reserved)
    {
        _commands = [.. commands];
        _reserved = new(reserved);
        _keys = commands.ToDictionary(c => c.Path, c => c.Current);
        Text = "Cambia le scorciatoie da tastiera";
        Width = 760;
        Height = 520;
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
            MultiSelect = false,
            Dock = DockStyle.Fill,
            AccessibleName = "Comandi e scorciatoie",
        };
        _list.Columns.Add("Comando", 460);
        _list.Columns.Add("Scorciatoia", 240);
        foreach (var command in _commands)
            _list.Items.Add(new ListViewItem([command.Path, ""]));
        RefreshRows();
        if (_list.Items.Count > 0)
        {
            _list.Items[0].Selected = true;
            _list.Items[0].Focused = true;
        }

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
        var change = Button("&Cambia...", Change);
        var remove = Button("&Togli la scorciatoia", () => Set(Selected, Keys.None));
        var reset = Button("&Ripristina", () => { if (Selected is { } c) Set(c, c.Default); });
        var resetAll = Button("Ripristina t&utti", ResetAll);
        var ok = new Button { Text = "OK", AutoSize = true, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Annulla", AutoSize = true, DialogResult = DialogResult.Cancel };
        buttons.Controls.AddRange([change, remove, reset, resetAll, ok, cancel]);

        Controls.Add(_list);
        Controls.Add(buttons);
        CancelButton = cancel;
        ActiveControl = _list;
        _list.ItemActivate += (_, _) => Change();
        _list.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Delete)
            {
                Set(Selected, Keys.None);
                e.Handled = true;
            }
        };
    }

    /// <summary>The keys of every command after the changes (only meaningful after OK).</summary>
    public IReadOnlyDictionary<string, Keys> Result => _keys;

    private ShortcutCommand? Selected => _list.SelectedIndices.Count > 0 ? _commands[_list.SelectedIndices[0]] : null;

    private static Button Button(string text, Action action)
    {
        var button = new Button { Text = text, AutoSize = true };
        button.Click += (_, _) => action();
        return button;
    }

    private void RefreshRows()
    {
        for (int i = 0; i < _commands.Count; i++)
        {
            var keys = _keys[_commands[i].Path];
            var text = keys == Keys.None ? "nessuno" : KeyNames.Italian(keys);
            if (keys != _commands[i].Default)
                text += " (cambiata)";
            _list.Items[i].SubItems[1].Text = text;
        }
    }

    private void Change()
    {
        if (Selected is not { } command)
            return;
        Keys keys;
        using (var capture = new KeyCaptureDialog(command.Path))
        {
            if (capture.ShowDialog(this) != DialogResult.OK)
                return;
            keys = capture.Pressed;
        }
        if (!KeyNames.IsValidShortcut(keys))
        {
            MessageBox.Show(this, $"{KeyNames.Italian(keys)} non si può usare come scorciatoia: serve Ctrl o Alt, oppure un tasto funzione (da F1 a F12).",
                Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (_reserved.TryGetValue(keys, out var use))
        {
            MessageBox.Show(this, $"{KeyNames.Italian(keys)} è riservato: {use}.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var other = _commands.FirstOrDefault(c => c != command && _keys[c.Path] == keys);
        if (other is not null)
        {
            var move = MessageBox.Show(this, $"{KeyNames.Italian(keys)} è già la scorciatoia di «{other.Path}». Vuoi spostarlo su «{command.Path}»? Quel comando resterà senza scorciatoia.",
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (move != DialogResult.Yes)
                return;
            _keys[other.Path] = Keys.None;
        }
        Set(command, keys);
    }

    private void Set(ShortcutCommand? command, Keys keys)
    {
        if (command is null)
            return;
        // Going back to the default must not leave two commands with the same keys.
        if (keys != Keys.None && _commands.FirstOrDefault(c => c != command && _keys[c.Path] == keys) is { } other)
            _keys[other.Path] = Keys.None;
        _keys[command.Path] = keys;
        RefreshRows();
        _list.Focus();
    }

    private void ResetAll()
    {
        var answer = MessageBox.Show(this, "Rimettere tutte le scorciatoie come all'inizio?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (answer != DialogResult.Yes)
            return;
        foreach (var command in _commands)
            _keys[command.Path] = command.Default;
        RefreshRows();
    }
}

/// <summary>Waits for the new combination: whatever is pressed (with Ctrl, Alt, Maiusc) is the answer; Esc alone gives up.</summary>
public sealed class KeyCaptureDialog : Form
{
    public Keys Pressed { get; private set; }

    public KeyCaptureDialog(string command)
    {
        Text = $"Premi il nuovo tasto per {command} (Esc annulla)";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Segoe UI", 11f);
        Padding = new Padding(16);
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        var label = new Label
        {
            Text = $"Premi la combinazione di tasti da usare per «{command}»,\nper esempio Ctrl+Alt+B o F9. Esc annulla.",
            AutoSize = true,
        };
        Controls.Add(label);
        AccessibleDescription = label.Text;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        var key = keyData & Keys.KeyCode;
        if (key is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin)
            return true; // only a modifier so far: wait for the key
        if (keyData == Keys.Escape)
        {
            DialogResult = DialogResult.Cancel;
            return true;
        }
        Pressed = keyData;
        DialogResult = DialogResult.OK;
        return true;
    }
}
