namespace Eleu.UI;

/// <summary>
/// Common base of the settings windows: label + field in two columns, OK and Cancel buttons.
/// The label always precedes the field, so Windows uses it as the accessible name and Alt+letter
/// goes straight to the field.
/// </summary>
public abstract class DialogBase : Form
{
    protected TableLayoutPanel Grid { get; }

    protected DialogBase(string heading, int fieldsWidth = 300)
    {
        Text = heading;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Font = new Font("Segoe UI", 11f);
        Padding = new Padding(12);

        Grid = NewGrid(fieldsWidth);
        Controls.Add(Grid);
    }

    /// <summary>A label + field grid, for example to put in a tab.</summary>
    protected static TableLayoutPanel NewGrid(int fieldsWidth = 300)
    {
        var grid = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Fill };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, fieldsWidth));
        return grid;
    }

    protected T Field<T>(string label, T field) where T : Control => Field(Grid, label, field);

    protected static T Field<T>(TableLayoutPanel grid, string label, T field) where T : Control
    {
        grid.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 6) });
        field.Dock = DockStyle.Fill;
        field.AccessibleName ??= label.Replace("&", "").TrimEnd(':');
        grid.Controls.Add(field);
        return field;
    }

    protected T WholeLine<T>(T control) where T : Control => WholeLine(Grid, control);

    protected static T WholeLine<T>(TableLayoutPanel grid, T control) where T : Control
    {
        grid.Controls.Add(control);
        grid.SetColumnSpan(control, 2);
        return control;
    }

    /// <summary>"Predefinito": the choices also become the starting point for new documents.</summary>
    protected CheckBox DefaultCheckBox(bool isChecked) =>
        WholeLine(new CheckBox { Text = "Pr&edefinito (usa anche per i documenti nuovi)", AutoSize = true, Checked = isChecked, Margin = new Padding(0, 8, 0, 0) });

    /// <summary>A tab for a TabControl, with a grid of fields inside.</summary>
    protected static TabPage Tab(string heading, Control content)
    {
        var tab = new TabPage(heading) { Padding = new Padding(8), AutoScroll = true };
        content.Dock = DockStyle.Top; // with Fill the last row would stretch to the bottom of the tab
        tab.Controls.Add(content);
        return tab;
    }

    /// <summary>A check box spanning the whole grid row.</summary>
    protected static CheckBox CheckBox(TableLayoutPanel grid, string text, bool isChecked) =>
        WholeLine(grid, new CheckBox { Text = text, AutoSize = true, Checked = isChecked, Margin = new Padding(0, 4, 0, 4) });

    protected static ComboBox ComboBox(IEnumerable<string> items, int selected)
    {
        var choice = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        choice.Items.AddRange(items.Cast<object>().ToArray());
        choice.SelectedIndex = Math.Clamp(selected, -1, choice.Items.Count - 1);
        return choice;
    }

    protected static NumericUpDown Number(decimal @value, decimal min, decimal max, decimal step, int decimals) =>
        new()
        {
            Minimum = min,
            Maximum = max,
            Increment = step,
            DecimalPlaces = decimals,
            Value = Math.Clamp(@value, min, max),
        };

    protected void AddButtons()
    {
        var buttons = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 12, 0, 0) };
        var ok = new Button { Text = "OK", AutoSize = true, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Annulla", AutoSize = true, DialogResult = DialogResult.Cancel };
        buttons.Controls.AddRange([ok, cancel]);
        WholeLine(buttons);
        AcceptButton = ok;
        CancelButton = cancel;
    }
}
