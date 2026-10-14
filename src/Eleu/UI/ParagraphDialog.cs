using Eleu.Documents;
using Eleu.Editor;

namespace Eleu.UI;

/// <summary>
/// Formato > Paragrafo: alignment, outline level (text or heading), indents, line spacing and spacing.
/// The values show the unit: indents and spacing in centimeters, line spacing as a percentage.
/// A negative first line is hanging.
/// </summary>
public sealed class ParagraphDialog : DialogBase
{
    public const float PointsPerCentimeter = 72f / 2.54f;

    private static readonly string[] Alignments = ["A sinistra", "Centrato", "A destra", "Giustificato"];
    private static readonly string[] Levels =
        ["Corpo del testo", .. Enumerable.Range(1, 9).Select(n => $"Titolo {n}")];

    private readonly ComboBox _alignment, _level;
    private readonly NumberWithUnit _left, _right, _firstLine, _lineSpacing, _before, _after;
    private readonly CheckBox _default;
    private readonly int _initialLevel;
    private readonly HashSet<Control> _changed = [];

    public ParagraphDialog(ParagraphFormat current, int headingLevel = 0) : base("Paragrafo", 220)
    {
        _initialLevel = headingLevel;
        _alignment = Field("&Allineamento paragrafo:", ComboBox(Alignments, current.Alignment is Alignment a ? (int)a : -1));
        _level = Field("&Livello struttura:", ComboBox(Levels, headingLevel));
        _left = Field("A &sinistra:", Centimeters(current.LeftIndent, 0));
        _right = Field("A &destra:", Centimeters(current.RightIndent, 0));
        _firstLine = Field("&Prima linea:", Centimeters(current.FirstLineIndent, -10));
        _lineSpacing = Field("&Interlinea:", new NumberWithUnit("%", (decimal)Math.Round((current.LineSpacing ?? 1f) * 100), 50, 400, 5, 0));
        _before = Field("Spazio p&rima:", Centimeters(current.SpaceBefore, 0));
        _after = Field("Spazio d&opo:", Centimeters(current.SpaceAfter, 0));

        foreach (var number in new NumericUpDown[] { _left, _right, _firstLine, _lineSpacing, _before, _after })
            number.ValueChanged += (_, _) => _changed.Add(number);
        foreach (var choice in new[] { _alignment, _level })
            choice.SelectedIndexChanged += (_, _) => _changed.Add(choice);

        _default = DefaultCheckBox(false);
        AddButtons();
        ActiveControl = _alignment;
    }

    /// <summary>Whether to use these indents, spacing and line spacing for new documents too.</summary>
    public bool Default => _default.Checked;

    /// <summary>The chosen outline level, if changed: 0 = body text, 1-9 = heading.</summary>
    public int? HeadingLevel => _changed.Contains(_level) && _level.SelectedIndex != _initialLevel ? _level.SelectedIndex : null;

    /// <summary>Only the properties changed by the user; the others stay null ("do not change").</summary>
    public ParagraphFormat Edits => new(
        Changed(_alignment) && _alignment.SelectedIndex >= 0 ? (Alignment)_alignment.SelectedIndex : null,
        Changed(_left) ? Points(_left) : null,
        Changed(_right) ? Points(_right) : null,
        Changed(_firstLine) ? Points(_firstLine) : null,
        Changed(_before) ? Points(_before) : null,
        Changed(_after) ? Points(_after) : null,
        Changed(_lineSpacing) ? (float)_lineSpacing.Value / 100f : null);

    /// <summary>All the values shown, for "Predefinito".</summary>
    public ParagraphFormat All => new(
        _alignment.SelectedIndex >= 0 ? (Alignment)_alignment.SelectedIndex : Alignment.Left,
        Points(_left), Points(_right), Points(_firstLine), Points(_before), Points(_after), (float)_lineSpacing.Value / 100f);

    private bool Changed(Control control) => _changed.Contains(control);

    private static NumberWithUnit Centimeters(float? points, decimal min) =>
        new("cm", (decimal)Math.Round((points ?? 0) / PointsPerCentimeter, 2), min, 20, 0.1m, 2);

    private static float Points(NumericUpDown field) => (float)field.Value * PointsPerCentimeter;
}
