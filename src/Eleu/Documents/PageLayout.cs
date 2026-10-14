using System.Globalization;
using System.Text.Json.Serialization;

namespace Eleu.Documents;

/// <summary>A paper format, in points, portrait.</summary>
public sealed record PaperFormat(string Name, float Width, float Height);

/// <summary>A variable to use in header and footer, e.g. {pagina}.</summary>
public sealed record PageVariable(string Placeholder, string Description, string WordField);

/// <summary>
/// How the document is printed: paper, orientation, margins, header and footer.
/// Measures in points; Width and Height are those of the sheet in portrait.
/// Header and footer are texts that may contain variables ({pagina}, {pagine}, {titolo}, {data}).
/// </summary>
public sealed record PageLayout(
    [property: JsonPropertyName("Larghezza")] float Width = 595.3f,
    [property: JsonPropertyName("Altezza")] float Height = 841.9f,
    [property: JsonPropertyName("Orizzontale")] bool Horizontal = false,
    [property: JsonPropertyName("MargineSinistro")] float LeftMargin = PageLayout.TwoCentimeters,
    [property: JsonPropertyName("MargineDestro")] float RightMargin = PageLayout.TwoCentimeters,
    [property: JsonPropertyName("MargineSopra")] float TopMargin = PageLayout.TwoCentimeters,
    [property: JsonPropertyName("MargineSotto")] float BottomMargin = PageLayout.TwoCentimeters,
    [property: JsonPropertyName("DistanzaIntestazione")] float HeaderDistance = PageLayout.OneCentimeter,
    [property: JsonPropertyName("DistanzaPiede")] float FooterDistance = PageLayout.OneCentimeter,
    [property: JsonPropertyName("Intestazione")] string Header = "",
    [property: JsonPropertyName("PiePagina")] string Footer = "",
    [property: JsonPropertyName("EscludiPrimaPagina")] bool SkipFirstPage = false)
{
    public const float PointsPerCentimeter = 72f / 2.54f;
    public const float OneCentimeter = PointsPerCentimeter;
    public const float TwoCentimeters = 2 * PointsPerCentimeter;

    public static IReadOnlyList<PaperFormat> Formats { get; } =
    [
        new("A4", 595.3f, 841.9f),
        new("A5", 419.5f, 595.3f),
        new("A3", 841.9f, 1190.6f),
        new("Lettera", 612f, 792f),
        new("Legale", 612f, 1008f),
    ];

    public static IReadOnlyList<PageVariable> Variables { get; } =
    [
        new("{pagina}", "Numero di pagina", "PAGE"),
        new("{pagine}", "Numero totale di pagine", "NUMPAGES"),
        new("{titolo}", "Titolo del documento", "TITLE"),
        new("{data}", "Data di oggi", "DATE"),
    ];

    /// <summary>Width and height of the sheet as it is printed (swapped if landscape).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public (float Width, float Height) Effective => Horizontal ? (Height, Width) : (Width, Height);

    /// <summary>The format with these measures (portrait or landscape), or null if it is custom.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public PaperFormat? Format
    {
        get
        {
            float shortText = Math.Min(Width, Height), longText = Math.Max(Width, Height);
            return Formats.FirstOrDefault(f => Math.Abs(f.Width - shortText) < 2 && Math.Abs(f.Height - longText) < 2);
        }
    }

    /// <summary>The text to print with the variables replaced; empty on the first page if it is excluded.</summary>
    public string Compose(string model, int page, int total, string heading, DateTime date)
    {
        if (model.Length == 0 || (SkipFirstPage && page == 1))
            return "";
        return model
            .Replace("{pagina}", page.ToString(CultureInfo.InvariantCulture))
            .Replace("{pagine}", total.ToString(CultureInfo.InvariantCulture))
            .Replace("{titolo}", heading)
            .Replace("{data}", date.ToString("d MMMM yyyy", CultureInfo.GetCultureInfo("it-IT")));
    }

    /// <summary>
    /// Splits a text with variables into pieces: plain text (Field null) or variable (Field = Word field name).
    /// Used to write real fields in RTF and DOCX files.
    /// </summary>
    public static IEnumerable<(string Text, string? Field)> Pieces(string model)
    {
        int i = 0;
        while (i < model.Length)
        {
            var next = Variables
                .Select(v => (Variable: v, Index: model.IndexOf(v.Placeholder, i, StringComparison.OrdinalIgnoreCase)))
                .Where(x => x.Index >= 0)
                .OrderBy(x => x.Index)
                .FirstOrDefault();
            if (next.Variable is null)
            {
                yield return (model[i..], null);
                yield break;
            }
            if (next.Index > i)
                yield return (model[i..next.Index], null);
            yield return (next.Variable.Placeholder, next.Variable.WordField);
            i = next.Index + next.Variable.Placeholder.Length;
        }
    }

    /// <summary>The variable matching a Word field (PAGE, NUMPAGES, ...), if Eleu knows it.</summary>
    public static string? FieldPlaceholder(string instruction)
    {
        var name = instruction.Trim().Split(' ', 2)[0];
        return Variables.FirstOrDefault(v => v.WordField.Equals(name, StringComparison.OrdinalIgnoreCase))?.Placeholder;
    }
}
