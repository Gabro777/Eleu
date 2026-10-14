using Eleu.Braille;

namespace Eleu.UI;

public enum CheckChoice
{
    Cancel,
    GoTo,
    Fix,
    FixAll,
    Continue,
}

/// <summary>
/// Check before Braille: the list of what will come out badly, each with its line. Go takes you there, Fix corrects
/// the chosen one (Fix all corrects every one that needs no question), Continue goes on to Braille anyway.
/// </summary>
public sealed class BrailleCheckDialog : Form
{
    private readonly IReadOnlyList<BrailleIssue> _issues;
    private readonly ListBox _list;
    private readonly Button _fix;

    public CheckChoice Choice { get; private set; } = CheckChoice.Cancel;

    public BrailleIssue? Chosen => _list.SelectedIndex >= 0 ? _issues[_list.SelectedIndex] : null;

    /// <param name="lines">The line of each issue, to show next to it.</param>
    /// <param name="continueText">The button that goes on: "Stampa comunque", "Salva comunque", or null when only checking.</param>
    public BrailleCheckDialog(IReadOnlyList<BrailleIssue> issues, IReadOnlyList<int> lines, string? continueText, int selected = 0)
    {
        _issues = issues;
        Text = issues.Count == 1 ? "Controllo per il Braille: 1 cosa da guardare" : $"Controllo per il Braille: {issues.Count} cose da guardare";
        Width = 760;
        Height = 460;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = false;
        Font = new Font("Segoe UI", 11f);
        Padding = new Padding(12);

        _list = new ListBox { Dock = DockStyle.Fill, AccessibleName = "Cose da guardare", IntegralHeight = false, HorizontalScrollbar = true };
        for (int i = 0; i < issues.Count; i++)
            _list.Items.Add($"Riga {lines[i]}: {issues[i].Description}");
        if (issues.Count > 0)
            _list.SelectedIndex = Math.Clamp(selected, 0, issues.Count - 1);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
        var go = Button("&Vai", CheckChoice.GoTo);
        _fix = Button("&Correggi", CheckChoice.Fix);
        var fixAll = Button("Correggi &tutto", CheckChoice.FixAll);
        fixAll.Enabled = issues.Any(i => i.AutomaticFix);
        var close = new Button { Text = continueText is null ? "Chiudi" : "Annulla", AutoSize = true, DialogResult = DialogResult.Cancel };
        buttons.Controls.AddRange([go, _fix, fixAll]);
        if (continueText is not null)
            buttons.Controls.Add(Button(continueText, CheckChoice.Continue));
        buttons.Controls.Add(close);

        Controls.Add(_list);
        Controls.Add(buttons);
        AcceptButton = go;
        CancelButton = close;
        ActiveControl = _list;
        _list.SelectedIndexChanged += (_, _) => UpdateFix();
        _list.DoubleClick += (_, _) => Finish(CheckChoice.GoTo);
        UpdateFix();
    }

    private Button Button(string text, CheckChoice choice)
    {
        var button = new Button { Text = text, AutoSize = true };
        button.Click += (_, _) => Finish(choice);
        return button;
    }

    private void UpdateFix() => _fix.Enabled = Chosen?.Fixable == true;

    private void Finish(CheckChoice choice)
    {
        Choice = choice;
        DialogResult = DialogResult.OK;
    }
}
