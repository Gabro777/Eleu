using System.Globalization;
using Eleu.Editor;

namespace Eleu.Documents;

/// <summary>
/// Spoken description of the formatting at the cursor (Shift+F2).
/// Font name and size and alignment are always said; the rest only if it differs from normal,
/// so the sentence stays short. Null values mean a selection with mixed formatting.
/// </summary>
public static class FormatDescription
{
    private static readonly CultureInfo Italian = CultureInfo.GetCultureInfo("it-IT");

    public static string Describe(int headingLevel, CharacterFormat character, ParagraphFormat paragraph)
    {
        var parts = new List<string> { headingLevel > 0 ? $"Titolo {headingLevel}" : "Testo normale" };

        var size = character.Size is float d ? $"{N(d)} punti" : "dimensioni miste";
        parts.Add(character.Font is string name ? $"{name} {size}" : $"caratteri misti, {size}");

        Effect(parts, character.Bold, "grassetto");
        Effect(parts, character.Italic, "corsivo");
        Effect(parts, character.Underline, "sottolineato");
        Effect(parts, character.Strikethrough, "barrato");

        switch (character.Position)
        {
            case TextPosition.Superscript:
                parts.Add("apice");
                break;
            case TextPosition.Subscript:
                parts.Add("pedice");
                break;
            case null:
                parts.Add("in parte apice o pedice");
                break;
        }

        if (character.Color is null)
            parts.Add("colori misti");
        else if (character.Color.Value.Rgb is int color)
            parts.Add($"colore {Colors.Name(color)}");
        if (character.Background is null)
            parts.Add("sfondi misti");
        else if (character.Background.Value.Rgb is int background)
            parts.Add($"sfondo {Colors.Name(background, background: true)}");
        if (character.Spacing is float spacing and not 0)
            parts.Add($"spaziatura {N(spacing)} punti");

        parts.Add(paragraph.Alignment switch
        {
            Alignment.Center => "centrato",
            Alignment.Right => "allineato a destra",
            Alignment.Justified => "giustificato",
            Alignment.Left => "allineato a sinistra",
            _ => "allineamenti misti",
        });

        switch (paragraph.ListType)
        {
            case ListKind.Bulleted:
                parts.Add("elenco puntato");
                break;
            case ListKind.Numbered:
                parts.Add("elenco numerato");
                break;
        }

        if (paragraph.LineSpacing is float lineSpacing && Math.Abs(lineSpacing - 1) > 0.01f)
            parts.Add(lineSpacing switch
            {
                1.5f => "interlinea una e mezza",
                2f => "interlinea doppia",
                _ => $"interlinea {N(lineSpacing, "0.##")}",
            });
        Measure(parts, paragraph.LeftIndent, "rientro sinistro");
        Measure(parts, paragraph.RightIndent, "rientro destro");
        if (paragraph.FirstLineIndent is float firstLine && Math.Abs(firstLine) > 0.5f)
            parts.Add($"prima riga {(firstLine > 0 ? "rientrata" : "sporgente")} di {Centimeters(Math.Abs(firstLine))}");
        if (paragraph.SpaceBefore is float before and > 0)
            parts.Add($"spazio prima {N(before)} punti");
        if (paragraph.SpaceAfter is float after and > 0)
            parts.Add($"spazio dopo {N(after)} punti");

        return string.Join(", ", parts);
    }

    private static void Effect(List<string> parts, bool? active, string name)
    {
        if (active == true)
            parts.Add(name);
        else if (active is null)
            parts.Add($"in parte {name}");
    }

    private static void Measure(List<string> parts, float? points, string name)
    {
        if (points is float @value && @value > 0.5f)
            parts.Add($"{name} {Centimeters(@value)}");
    }

    private static string N(float @value, string format = "0.#") => @value.ToString(format, Italian);

    private static string Centimeters(float points) => $"{N(points / (72f / 2.54f))} centimetri";
}
