namespace Eleu.UI;

/// <summary>
/// Shows a long text in a read-only box: unlike a message box,
/// it can be reread line by line with the arrows.
/// </summary>
public sealed class TextDialog : Form
{
    public TextDialog(string heading, string text)
    {
        Text = heading;
        Width = 600;
        Height = 500;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = false;
        Font = new Font("Segoe UI", 11f);

        var field = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Dock = DockStyle.Fill,
            Text = text,
            AccessibleName = heading,
            TabIndex = 0,
        };

        var close = new Button { Text = "Chiudi", Dock = DockStyle.Bottom, Height = 40, DialogResult = DialogResult.Cancel };

        Controls.Add(field);
        Controls.Add(close);
        CancelButton = close;
        ActiveControl = field;
        field.Select(0, 0);
    }
}
