using System.Globalization;
using System.Text.RegularExpressions;

namespace Eleu.Interfaccia;

/// <summary>
/// Campo numerico che mostra l'unità di misura nel valore ("2 cm", "12 pt", "100 %"),
/// così lo screen reader legge il numero con la sua unità. Si può scrivere il numero con o senza unità.
/// </summary>
public sealed partial class NumeroConUnita : NumericUpDown
{
    public string Unita { get; }

    public NumeroConUnita(string unita, decimal valore, decimal minimo, decimal massimo, decimal passo, int decimali)
    {
        Unita = unita;
        Minimum = minimo;
        Maximum = massimo;
        Increment = passo;
        DecimalPlaces = decimali;
        Value = Math.Clamp(valore, minimo, massimo);
        UpdateEditText();
    }

    protected override void UpdateEditText()
    {
        if (Unita is null)
        {
            base.UpdateEditText(); // durante la costruzione della base
            return;
        }
        // Leggere Value mentre il testo è "in modifica" fa ripartire la validazione: prima si legge il testo.
        if (UserEdit)
            LeggiTesto();
        // Zeri decimali inutili omessi: "2 cm", non "2,0 cm".
        var formato = DecimalPlaces > 0 ? "0." + new string('#', DecimalPlaces) : "0";
        ChangingText = true;
        Text = $"{Value.ToString(formato, CultureInfo.CurrentCulture)} {Unita}";
    }

    protected override void ValidateEditText()
    {
        if (UserEdit)
            LeggiTesto();
        UpdateEditText();
    }

    /// <summary>Il numero scritto dall'utente, con o senza unità; se non è un numero il valore resta quello di prima.</summary>
    private void LeggiTesto()
    {
        var numero = Numero().Match(Text.Replace('.', ','));
        UserEdit = false;
        if (numero.Success && decimal.TryParse(numero.Value, NumberStyles.Number, CultureInfo.GetCultureInfo("it-IT"), out var valore))
            Value = Math.Clamp(Math.Round(valore, DecimalPlaces), Minimum, Maximum);
    }

    [GeneratedRegex(@"-?\d+(,\d+)?")]
    private static partial Regex Numero();
}
