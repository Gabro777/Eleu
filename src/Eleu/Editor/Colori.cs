namespace Eleu.Editor;

/// <summary>Un colore con il nome da leggere: niente tavolozze, che con lo screen reader non si usano.</summary>
public sealed record ColoreConNome(string Nome, int? Rgb);

/// <summary>Colori proposti, nomi dei colori e verifica del contrasto secondo le regole W3C (WCAG).</summary>
public static class Colori
{
    public const int Nero = 0x000000;
    public const int Bianco = 0xFFFFFF;

    public static IReadOnlyList<ColoreConNome> Testo { get; } =
    [
        new("Automatico", null),
        new("Nero", Nero),
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
        new("Bianco", Bianco),
    ];

    public static IReadOnlyList<ColoreConNome> Sfondo { get; } =
    [
        new("Nessuno", null),
        new("Giallo", 0xFFFF00),
        new("Verde chiaro", 0xCCFFCC),
        new("Azzurro chiaro", 0xCCECFF),
        new("Rosa", 0xFFCCE5),
        new("Arancione chiaro", 0xFFE0B2),
        new("Grigio chiaro", 0xD9D9D9),
        new("Blu scuro", 0x002060),
        new("Nero", Nero),
    ];

    /// <summary>Il nome del colore se è tra quelli proposti, altrimenti "personalizzato".</summary>
    public static string Nome(int? rgb, bool sfondo = false)
    {
        var elenco = sfondo ? Sfondo : Testo;
        return elenco.FirstOrDefault(c => c.Rgb == rgb)?.Nome?.ToLowerInvariant()
            ?? Testo.Concat(Sfondo).FirstOrDefault(c => c.Rgb == rgb)?.Nome?.ToLowerInvariant()
            ?? $"personalizzato ({rgb:X6})";
    }

    /// <summary>Rapporto di contrasto W3C tra due colori, da 1 (nessuno) a 21 (nero su bianco).</summary>
    public static double Contrasto(int testo, int sfondo)
    {
        double l1 = Luminanza(testo), l2 = Luminanza(sfondo);
        return (Math.Max(l1, l2) + 0.05) / (Math.Min(l1, l2) + 0.05);
    }

    /// <summary>Giudizio sul contrasto, con le soglie WCAG per il testo normale (4,5:1) e ottimale (7:1).</summary>
    public static string GiudizioContrasto(int? testo, int? sfondo)
    {
        double rapporto = Contrasto(testo ?? Nero, sfondo ?? Bianco);
        string valore = $"Contrasto {rapporto.ToString("0.#", System.Globalization.CultureInfo.GetCultureInfo("it-IT"))}:1";
        return rapporto switch
        {
            >= 7 => $"{valore}, ottimo.",
            >= 4.5 => $"{valore}, buono.",
            >= 3 => $"{valore}, sufficiente solo per testo grande.",
            _ => $"{valore}, insufficiente: difficile da leggere.",
        };
    }

    private static double Luminanza(int rgb)
    {
        static double Canale(int valore)
        {
            double c = valore / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Canale((rgb >> 16) & 0xFF) + 0.7152 * Canale((rgb >> 8) & 0xFF) + 0.0722 * Canale(rgb & 0xFF);
    }
}
