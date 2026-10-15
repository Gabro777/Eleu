using System.Drawing.Printing;
using System.Drawing.Text;

namespace Eleu.Braille;

/// <summary>
/// Braille printed through the Windows printer driver instead of sending the BRF file as it is. Embossers like the
/// ViewPlus ones install Braille fonts with their driver: a character written in one of those fonts is embossed as the
/// Braille cell of its ASCII code (the same code a BRF file uses). Printing that way, the job itself carries what
/// Eleu decides: one Braille page per sheet, the lines where Eleu puts them and double-sided printing; the printer's
/// "legacy" settings are not used.
/// </summary>
public static class DriverBraillePrinting
{
    /// <summary>Braille line pitch: 10 mm (0.4 inch), the usual distance between Braille lines.</summary>
    private const float LinePitchInches = 0.4f;

    /// <summary>The installed fonts that look like Braille fonts, the 6-dot ones ("29" in the name) first.</summary>
    public static IReadOnlyList<string> BrailleFonts()
    {
        using var fonts = new InstalledFontCollection();
        return [.. fonts.Families.Select(f => f.Name)
            .Where(n => n.Contains("braille", StringComparison.OrdinalIgnoreCase) || n.Contains("tiger", StringComparison.OrdinalIgnoreCase))
            .OrderBy(n => n.Contains("29") ? 0 : 1).ThenBy(n => n, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>The font size: 29 points for the 6-dot fonts, 43 for the 8-dot ones, as the ViewPlus manual says.</summary>
    public static float FontSize(string font) => font.Contains("29") ? 29f : 43f;

    /// <summary>Prints the pages (Unicode Braille cells) through the driver with the given Braille font.</summary>
    /// <param name="toFile">Print to this file instead of the printer (for drivers that write files, like Microsoft Print to PDF).</param>
    public static void Print(string printer, IReadOnlyList<List<string>> pages, string font, bool doubleSided, string documentName, string? toFile = null)
    {
        using var document = new PrintDocument { DocumentName = documentName };
        document.PrinterSettings.PrinterName = printer;
        if (!document.PrinterSettings.IsValid)
            throw new InvalidOperationException($"La stampante {printer} non è disponibile.");
        if (doubleSided && document.PrinterSettings.CanDuplex)
            document.PrinterSettings.Duplex = Duplex.Vertical;
        else if (document.PrinterSettings.CanDuplex)
            document.PrinterSettings.Duplex = Duplex.Simplex;
        if (toFile is not null)
        {
            document.PrinterSettings.PrintToFile = true;
            document.PrinterSettings.PrintFileName = toFile;
        }
        document.DefaultPageSettings.Margins = new Margins(0, 0, 0, 0);
        document.OriginAtMargins = false;

        using var braille = new Font(font, FontSize(font), GraphicsUnit.Point);
        var format = StringFormat.GenericTypographic;
        int next = 0;
        document.PrintPage += (_, e) =>
        {
            var g = e.Graphics!;
            g.PageUnit = GraphicsUnit.Inch;
            g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;
            var page = pages[next++];
            for (int line = 0; line < page.Count; line++)
            {
                var text = string.Concat(page[line].Select(BrfFormat.ToAscii)).TrimEnd();
                if (text.Length > 0)
                    g.DrawString(text, braille, Brushes.Black, 0, line * LinePitchInches, format);
            }
            e.HasMorePages = next < pages.Count;
        };
        document.Print();
    }
}
