using System.Globalization;

namespace Eleu.UI;

/// <summary>
/// Inserisci > Simbolo: the characters that are not on the Italian keyboard, each with its name (so the screen
/// reader says what is being chosen), or any character from its Unicode code.
/// </summary>
public sealed class SymbolsDialog : DialogBase
{
    public static readonly IReadOnlyList<(string Symbol, string Name)> Symbols =
    [
        ("È", "E maiuscola con accento grave"),
        ("É", "E maiuscola con accento acuto"),
        ("À", "A maiuscola con accento"),
        ("Ì", "I maiuscola con accento"),
        ("Ò", "O maiuscola con accento"),
        ("Ù", "U maiuscola con accento"),
        ("é", "e con accento acuto"),
        ("«", "virgolette basse di apertura"),
        ("»", "virgolette basse di chiusura"),
        ("“", "virgolette alte di apertura"),
        ("”", "virgolette alte di chiusura"),
        ("’", "apostrofo tipografico"),
        ("—", "lineetta lunga"),
        ("–", "lineetta breve"),
        ("…", "puntini di sospensione"),
        ("•", "pallino"),
        ("€", "euro"),
        ("£", "sterlina"),
        ("$", "dollaro"),
        ("°", "gradi"),
        ("±", "più o meno"),
        ("×", "per, moltiplicazione"),
        ("÷", "diviso"),
        ("≠", "diverso"),
        ("≤", "minore o uguale"),
        ("≥", "maggiore o uguale"),
        ("≈", "circa uguale"),
        ("½", "un mezzo"),
        ("¼", "un quarto"),
        ("¾", "tre quarti"),
        ("²", "al quadrato"),
        ("³", "al cubo"),
        ("‰", "per mille"),
        ("√", "radice quadrata"),
        ("∞", "infinito"),
        ("π", "pi greco"),
        ("µ", "micro"),
        ("§", "paragrafo"),
        ("©", "copyright"),
        ("®", "marchio registrato"),
        ("™", "marchio"),
        ("→", "freccia a destra"),
        ("←", "freccia a sinistra"),
        ("↑", "freccia su"),
        ("↓", "freccia giù"),
        ("✓", "segno di spunta"),
        ("♥", "cuore"),
        ("☺", "faccina sorridente"),
    ];

    private readonly ListBox _list;
    private readonly TextBox _code;

    public SymbolsDialog() : base("Inserisci simbolo", 320)
    {
        _list = Field("&Simbolo:", new ListBox { Height = 220, IntegralHeight = false });
        foreach (var (symbol, name) in Symbols)
            _list.Items.Add($"{symbol}  {name}");
        _list.SelectedIndex = 0;
        _code = Field("&Oppure codice Unicode:", new TextBox { AccessibleDescription = "Per esempio 20AC per l'euro. Se scritto, vale al posto del simbolo scelto" });
        AddButtons();
        ActiveControl = _list;
        _list.DoubleClick += (_, _) => { DialogResult = DialogResult.OK; Close(); };
        FormClosing += (_, e) =>
        {
            if (DialogResult == DialogResult.OK && _code.Text.Trim().Length > 0 && FromCode(_code.Text) is null)
            {
                MessageBox.Show(this, "Il codice non è valido: scrivi un numero esadecimale, per esempio 20AC.", Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _code.Focus();
                e.Cancel = true;
            }
        };
    }

    /// <summary>The chosen character: from the code, if written, otherwise from the list.</summary>
    public string Chosen =>
        _code.Text.Trim().Length > 0 ? FromCode(_code.Text) ?? "" : Symbols[Math.Max(0, _list.SelectedIndex)].Symbol;

    /// <summary>The character of a hexadecimal Unicode code ("20AC", "U+20AC", "0x20ac"), or null if not valid.</summary>
    public static string? FromCode(string code)
    {
        var cleaned = code.Trim().ToUpperInvariant();
        foreach (var prefix in new[] { "U+", "0X", "\\U" })
            if (cleaned.StartsWith(prefix, StringComparison.Ordinal))
                cleaned = cleaned[prefix.Length..];
        if (!int.TryParse(cleaned, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int @value)
            || @value is < 0x20 or > 0x10FFFF or (>= 0xD800 and <= 0xDFFF))
            return null;
        return char.ConvertFromUtf32(@value);
    }
}
