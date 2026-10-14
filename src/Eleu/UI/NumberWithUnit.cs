using System.Globalization;
using System.Text.RegularExpressions;

namespace Eleu.UI;

/// <summary>
/// Numeric field that shows the unit of measure in the value ("2 cm", "12 pt", "100 %"),
/// so the screen reader reads the number with its unit. The number can be typed with or without the unit.
/// </summary>
public sealed partial class NumberWithUnit : NumericUpDown
{
    public string Units { get; }

    public NumberWithUnit(string units, decimal @value, decimal min, decimal max, decimal step, int decimals)
    {
        Units = units;
        Minimum = min;
        Maximum = max;
        Increment = step;
        DecimalPlaces = decimals;
        Value = Math.Clamp(@value, min, max);
        UpdateEditText();
    }

    protected override void UpdateEditText()
    {
        if (Units is null)
        {
            base.UpdateEditText(); // while the base class is being constructed
            return;
        }
        // Reading Value while the text is "being edited" restarts validation: read the text first.
        if (UserEdit)
            ReadText();
        // Useless decimal zeros omitted: "2 cm", not "2,0 cm".
        var format = DecimalPlaces > 0 ? "0." + new string('#', DecimalPlaces) : "0";
        ChangingText = true;
        Text = $"{Value.ToString(format, CultureInfo.CurrentCulture)} {Units}";
    }

    protected override void ValidateEditText()
    {
        if (UserEdit)
            ReadText();
        UpdateEditText();
    }

    /// <summary>The number typed by the user, with or without the unit; if it is not a number the value stays as before.</summary>
    private void ReadText()
    {
        var number = Number().Match(Text.Replace('.', ','));
        UserEdit = false;
        if (number.Success && decimal.TryParse(number.Value, NumberStyles.Number, CultureInfo.GetCultureInfo("it-IT"), out var @value))
            Value = Math.Clamp(Math.Round(@value, DecimalPlaces), Minimum, Maximum);
    }

    [GeneratedRegex(@"-?\d+(,\d+)?")]
    private static partial Regex Number();
}
