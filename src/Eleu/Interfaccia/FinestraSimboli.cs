using System.Globalization;

namespace Eleu.Interfaccia;

/// <summary>
/// Inserisci > Simbolo: i caratteri che non sono sulla tastiera italiana, ognuno con il suo nome (così lo screen
/// reader dice cosa si sta scegliendo), oppure qualsiasi carattere dal suo codice Unicode.
/// </summary>
public sealed class FinestraSimboli : Modulo
{
    public static readonly IReadOnlyList<(string Simbolo, string Nome)> Simboli =
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

    private readonly ListBox _elenco;
    private readonly TextBox _codice;

    public FinestraSimboli() : base("Inserisci simbolo", 320)
    {
        _elenco = Campo("&Simbolo:", new ListBox { Height = 220, IntegralHeight = false });
        foreach (var (simbolo, nome) in Simboli)
            _elenco.Items.Add($"{simbolo}  {nome}");
        _elenco.SelectedIndex = 0;
        _codice = Campo("&Oppure codice Unicode:", new TextBox { AccessibleDescription = "Per esempio 20AC per l'euro. Se scritto, vale al posto del simbolo scelto" });
        AggiungiPulsanti();
        ActiveControl = _elenco;
        _elenco.DoubleClick += (_, _) => { DialogResult = DialogResult.OK; Close(); };
        FormClosing += (_, e) =>
        {
            if (DialogResult == DialogResult.OK && _codice.Text.Trim().Length > 0 && DaCodice(_codice.Text) is null)
            {
                MessageBox.Show(this, "Il codice non è valido: scrivi un numero esadecimale, per esempio 20AC.", Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _codice.Focus();
                e.Cancel = true;
            }
        };
    }

    /// <summary>Il carattere scelto: dal codice, se scritto, altrimenti dall'elenco.</summary>
    public string Scelto =>
        _codice.Text.Trim().Length > 0 ? DaCodice(_codice.Text) ?? "" : Simboli[Math.Max(0, _elenco.SelectedIndex)].Simbolo;

    /// <summary>Il carattere di un codice Unicode esadecimale ("20AC", "U+20AC", "0x20ac"), o null se non valido.</summary>
    public static string? DaCodice(string codice)
    {
        var pulito = codice.Trim().ToUpperInvariant();
        foreach (var prefisso in new[] { "U+", "0X", "\\U" })
            if (pulito.StartsWith(prefisso, StringComparison.Ordinal))
                pulito = pulito[prefisso.Length..];
        if (!int.TryParse(pulito, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int valore)
            || valore is < 0x20 or > 0x10FFFF or (>= 0xD800 and <= 0xDFFF))
            return null;
        return char.ConvertFromUtf32(valore);
    }
}
