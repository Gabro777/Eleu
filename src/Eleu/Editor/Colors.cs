namespace Eleu.Editor;

/// <summary>A color with a name to read: no palettes, which are not usable with a screen reader.</summary>
public sealed record NamedColor(string Name, int? Rgb);

/// <summary>Proposed colors, color names and contrast check according to the W3C rules (WCAG).</summary>
public static class Colors
{
    public const int Black = 0x000000;
    public const int White = 0xFFFFFF;

    public static IReadOnlyList<NamedColor> Text { get; } =
    [
        new("Automatico", null),
        new("Nero", Black),
        new("Grigio scuro", 0x595959),
        new("Grigio", 0x808080),
        new("Rosso scuro", 0xC00000),
        new("Rosso", 0xFF0000),
        new("Arancione", 0xE36C09),
        new("Marrone", 0x7F3F00),
        new("Verde scuro", 0x006400),
        new("Verde", 0x00B050),
        new("Blu scuro", 0x002060),
        new("Blu", 0x0070C0),
        new("Azzurro", 0x00B0F0),
        new("Viola", 0x7030A0),
        new("Giallo", 0xFFFF00),
        new("Bianco", White),
    ];

    public static IReadOnlyList<NamedColor> Background { get; } =
    [
        new("Nessuno", null),
        new("Giallo", 0xFFFF00),
        new("Verde chiaro", 0xCCFFCC),
        new("Azzurro chiaro", 0xCCECFF),
        new("Rosa", 0xFFCCE5),
        new("Arancione chiaro", 0xFFE0B2),
        new("Grigio chiaro", 0xD9D9D9),
        new("Blu scuro", 0x002060),
        new("Nero", Black),
    ];

    /// <summary>The color name if it is one of the proposed ones, otherwise "personalizzato".</summary>
    public static string Name(int? rgb, bool background = false)
    {
        var list = background ? Background : Text;
        return list.FirstOrDefault(c => c.Rgb == rgb)?.Name?.ToLowerInvariant()
            ?? Text.Concat(Background).FirstOrDefault(c => c.Rgb == rgb)?.Name?.ToLowerInvariant()
            ?? $"personalizzato ({rgb:X6})";
    }

    /// <summary>W3C contrast ratio between two colors, from 1 (none) to 21 (black on white).</summary>
    public static double Contrast(int text, int background)
    {
        double l1 = Luminance(text), l2 = Luminance(background);
        return (Math.Max(l1, l2) + 0.05) / (Math.Min(l1, l2) + 0.05);
    }

    /// <summary>Contrast rating, with the WCAG thresholds for normal text (4.5:1) and enhanced (7:1).</summary>
    public static string ContrastRating(int? text, int? background)
    {
        double ratio = Contrast(text ?? Black, background ?? White);
        string @value = $"Contrasto {ratio.ToString("0.#", System.Globalization.CultureInfo.GetCultureInfo("it-IT"))}:1";
        return ratio switch
        {
            >= 7 => $"{@value}, ottimo.",
            >= 4.5 => $"{@value}, buono.",
            >= 3 => $"{@value}, sufficiente solo per testo grande.",
            _ => $"{@value}, insufficiente: difficile da leggere.",
        };
    }

    private static double Luminance(int rgb)
    {
        static double Channel(int @value)
        {
            double c = @value / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel((rgb >> 16) & 0xFF) + 0.7152 * Channel((rgb >> 8) & 0xFF) + 0.0722 * Channel(rgb & 0xFF);
    }
}
