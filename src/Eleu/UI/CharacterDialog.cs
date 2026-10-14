using Eleu.Documents;
using Eleu.Editor;

namespace Eleu.UI;

/// <summary>
/// Formato > Carattere: font, style, size, effects, position, colors and spacing in a single window.
/// Applies only what is changed, so a selection with mixed formats is not flattened.
/// </summary>
public sealed class CharacterDialog : DialogBase
{
    private const float MillimetersPerDot = 25.4f / 72f;
    private static readonly string[] Styles = ["Normale", "Corsivo", "Grassetto", "Grassetto corsivo"];
    private static readonly string[] Positions = ["Normale", "Apice", "Pedice"];

    private readonly CharacterFormat _initial;
    private readonly IReadOnlyList<string> _characters;
    private readonly ComboBox _kind, _style, _position, _color, _background;
    private readonly NumberWithUnit _size, _spacing;
    private readonly CheckBox _underline, _strikethrough, _default;
    private readonly Label _contrast, _preview;
    private readonly List<NamedColor> _textColors, _backgroundColors;
    private readonly int _initialStyle;
    private bool _sizeChanged, _spacingChanged;

    public CharacterDialog(CharacterFormat current, IReadOnlyList<string> characters) : base("Carattere")
    {
        _initial = current;
        _characters = characters;
        _textColors = WithCustom(Colors.Text, current.Color);
        _backgroundColors = WithCustom(Colors.Background, current.Background);
        _initialStyle = current is { Bold: bool g, Italic: bool c } ? (g ? 2 : 0) + (c ? 1 : 0) : -1;

        _kind = Field("&Carattere:", ComboBox(characters, CharacterIndex(current.Font)));
        _style = Field("S&tile:", ComboBox(Styles, _initialStyle));
        _size = Field("&Dimensione:", new NumberWithUnit("pt", (decimal)(current.Size ?? DocumentContent.BaseSize), 6, 96, 1, 1));
        _size.ValueChanged += (_, _) => _sizeChanged = true;

        var effects = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 4, 0, 4) };
        _underline = TriStateCheckBox("&Sottolineato", current.Underline);
        _strikethrough = TriStateCheckBox("&Barrato", current.Strikethrough);
        effects.Controls.AddRange([_underline, _strikethrough]);
        WholeLine(effects);

        _position = Field("&Posizione:", ComboBox(Positions, current.Position is TextPosition p ? (int)p : -1));
        _color = Field("C&olore del testo:", ComboBox(_textColors.Select(c => c.Name), Index(_textColors, current.Color)));
        _background = Field("S&fondo (evidenziato):", ComboBox(_backgroundColors.Select(c => c.Name), Index(_backgroundColors, current.Background)));
        _spacing = Field("Sp&aziatura:", new NumberWithUnit("mm", (decimal)Math.Round((current.Spacing ?? 0) * MillimetersPerDot, 1), -1, 7, 0.1m, 1));
        _spacing.ValueChanged += (_, _) => _spacingChanged = true;

        _contrast = WholeLine(new Label { AutoSize = true, Margin = new Padding(0, 8, 0, 0) });
        _preview = WholeLine(new Label
        {
            Text = "AaBbCc àèìòù 123",
            AutoSize = false,
            Height = 56,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            BorderStyle = BorderStyle.FixedSingle,
            AccessibleName = "Esempio",
        });

        foreach (var choice in new[] { _kind, _style, _position, _color, _background })
            choice.SelectedIndexChanged += (_, _) => UpdatePreview();
        _size.ValueChanged += (_, _) => UpdatePreview();
        _underline.CheckStateChanged += (_, _) => UpdatePreview();
        _strikethrough.CheckStateChanged += (_, _) => UpdatePreview();
        UpdatePreview();

        _default = DefaultCheckBox(false);
        AddButtons();
        ActiveControl = _kind;
    }

    /// <summary>Whether to use the chosen font and size for new documents too.</summary>
    public bool Default => _default.Checked;

    /// <summary>Font and size shown in the window (for "Predefinito").</summary>
    public (string? Character, float Size) Base =>
        (_kind.SelectedIndex >= 0 ? _characters[_kind.SelectedIndex] : null, (float)_size.Value);

    /// <summary>Only the properties changed by the user; the others stay null ("do not change").</summary>
    public CharacterFormat Edits
    {
        get
        {
            bool styleChanged = _style.SelectedIndex >= 0 && _style.SelectedIndex != _initialStyle;
            return new CharacterFormat(
                _kind.SelectedIndex >= 0 && _kind.SelectedIndex != CharacterIndex(_initial.Font) ? _characters[_kind.SelectedIndex] : null,
                _sizeChanged ? (float)_size.Value : null,
                styleChanged ? _style.SelectedIndex >= 2 : null,
                styleChanged ? _style.SelectedIndex % 2 == 1 : null,
                Changed(_underline, _initial.Underline),
                Changed(_strikethrough, _initial.Strikethrough),
                _position.SelectedIndex >= 0 && (TextPosition)_position.SelectedIndex != _initial.Position ? (TextPosition)_position.SelectedIndex : null,
                _color.SelectedIndex >= 0 && _color.SelectedIndex != Index(_textColors, _initial.Color) ? new ColorValue(_textColors[_color.SelectedIndex].Rgb) : null,
                _background.SelectedIndex >= 0 && _background.SelectedIndex != Index(_backgroundColors, _initial.Background) ? new ColorValue(_backgroundColors[_background.SelectedIndex].Rgb) : null,
                _spacingChanged ? (float)_spacing.Value / MillimetersPerDot : null);
        }
    }

    private void UpdatePreview()
    {
        int? text = _color.SelectedIndex >= 0 ? _textColors[_color.SelectedIndex].Rgb : null;
        int? background = _background.SelectedIndex >= 0 ? _backgroundColors[_background.SelectedIndex].Rgb : null;
        _contrast.Text = Colors.ContrastRating(text, background);

        var style = FontStyle.Regular;
        if (_style.SelectedIndex >= 2) style |= FontStyle.Bold;
        if (_style.SelectedIndex % 2 == 1) style |= FontStyle.Italic;
        if (_underline.CheckState == CheckState.Checked) style |= FontStyle.Underline;
        if (_strikethrough.CheckState == CheckState.Checked) style |= FontStyle.Strikeout;
        var family = _kind.SelectedIndex >= 0 ? _characters[_kind.SelectedIndex] : "Segoe UI";
        try
        {
            var old = _preview.Font;
            _preview.Font = new Font(family, Math.Min(28f, (float)_size.Value), style);
            if (old != Font)
                old.Dispose();
        }
        catch (ArgumentException)
        {
            // Font that does not support the requested style: the sample stays as it was.
        }
        _preview.ForeColor = Color.FromArgb(unchecked((int)0xFF000000) | (text ?? Colors.Black));
        _preview.BackColor = Color.FromArgb(unchecked((int)0xFF000000) | (background ?? Colors.White));
    }

    private int CharacterIndex(string? name) =>
        name is null ? -1 : _characters.ToList().FindIndex(c => c.Equals(name, StringComparison.OrdinalIgnoreCase));

    private static List<NamedColor> WithCustom(IReadOnlyList<NamedColor> list, ColorValue? current)
    {
        var colors = list.ToList();
        if (current is { Rgb: int rgb } && colors.All(c => c.Rgb != rgb))
            colors.Add(new NamedColor($"Personalizzato ({rgb:X6})", rgb));
        return colors;
    }

    private static int Index(List<NamedColor> colors, ColorValue? current) =>
        current is ColorValue @value ? colors.FindIndex(c => c.Rgb == @value.Rgb) : -1;

    private static CheckBox TriStateCheckBox(string text, bool? @value) => new()
    {
        Text = text,
        AutoSize = true,
        ThreeState = @value is null,
        CheckState = @value switch { true => CheckState.Checked, false => CheckState.Unchecked, null => CheckState.Indeterminate },
    };

    private static bool? Changed(CheckBox checkBox, bool? initial) =>
        checkBox.CheckState == CheckState.Indeterminate ? null
        : (checkBox.CheckState == CheckState.Checked) == initial ? null
        : checkBox.CheckState == CheckState.Checked;
}
