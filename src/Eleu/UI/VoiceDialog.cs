using Eleu.Speech;

namespace Eleu.UI;

/// <summary>Choice of voice, rate and volume, with a button to listen to it.</summary>
public sealed class VoiceDialog : Form
{
    private readonly ComboBox _items;
    private readonly NumericUpDown _rate;
    private readonly NumericUpDown _volume;
    private readonly IReadOnlyList<AvailableVoice> _list;

    public event EventHandler? TestRequest;

    public AvailableVoice? ChosenVoice => _items.SelectedIndex >= 0 ? _list[_items.SelectedIndex] : null;

    public VoiceParameters Parameters => new((int)_rate.Value, (int)_volume.Value);

    public VoiceDialog(IReadOnlyList<AvailableVoice> items, AvailableVoice? current, VoiceParameters parameters)
    {
        _list = items;
        Text = "Voce";
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
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 340));

        _items = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, AccessibleName = "Voce" };
        _items.Items.AddRange(items.Select(v => (object)v.Description).ToArray());
        _items.SelectedIndex = current is null ? (items.Count > 0 ? 0 : -1) : items.ToList().FindIndex(v => v.Id == current.Id);
        AddField(grid, "&Voce:", _items);

        _rate = new NumericUpDown
        {
            Minimum = VoiceParameters.MinRate,
            Maximum = VoiceParameters.MaxRate,
            Increment = 10,
            Value = Math.Clamp(parameters.Rate, VoiceParameters.MinRate, VoiceParameters.MaxRate),
            AccessibleName = "Velocità in percentuale",
        };
        AddField(grid, "V&elocità (%):", _rate);

        _volume = new NumericUpDown
        {
            Minimum = 10,
            Maximum = 100,
            Increment = 10,
            Value = Math.Clamp(parameters.Volume, 10, 100),
            AccessibleName = "Volume in percentuale",
        };
        AddField(grid, "V&olume (%):", _volume);

        var buttons = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 12, 0, 0) };
        var test = new Button { Text = "&Prova", AutoSize = true };
        test.Click += (_, _) => TestRequest?.Invoke(this, EventArgs.Empty);
        var ok = new Button { Text = "OK", AutoSize = true, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Annulla", AutoSize = true, DialogResult = DialogResult.Cancel };
        buttons.Controls.AddRange([test, ok, cancel]);
        grid.Controls.Add(buttons);
        grid.SetColumnSpan(buttons, 2);

        Controls.Add(grid);
        AcceptButton = ok;
        CancelButton = cancel;
        ActiveControl = _items;
    }

    private static void AddField(TableLayoutPanel grid, string label, Control field)
    {
        // The label precedes the field: Windows uses it as the name and Alt+letter goes to the field.
        grid.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 6) });
        grid.Controls.Add(field);
    }
}
