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
/// <remarks>
/// The layout is the one that works with BrailleBlaster on ViewPlus embossers: the Braille29 font at 29 points, one
/// line under the other at the distance the font itself gives (no extra space), the text in a near-black colour,
/// margins on the embosser's grid of dots (20 to the inch) and, on the back of the sheet, the margin moved by the dots
/// that put the cells between the ones on the front.
/// </remarks>
public static class DriverBraillePrinting
{
    /// <summary>The embosser's grid: 20 dots to the inch.</summary>
    public const float DotInches = 0.05f;

    /// <summary>Space left at the top and on the left (half an inch): the embosser cannot emboss on the very edge of the sheet.</summary>
    public const float MarginInches = 0.5f;

    /// <summary>Space left at the bottom.</summary>
    public const float BottomMarginInches = 0.4f;

    /// <summary>A Braille cell is 5 dots of the grid wide: how many more dots the back margin needs, by the rest of the division by 5.</summary>
    private static readonly int[] BackOffsets = [1, 0, 1, 2, 0];

    /// <summary>The colour of the Braille text: almost black, the one ViewPlus Braille fonts are printed with.</summary>
    private static readonly Color BrailleColor = Color.FromArgb(6, 7, 8);

    /// <summary>The installed fonts that look like Braille fonts, the 6-dot ones first (Braille29 before Braille36: it is the one BrailleBlaster uses).</summary>
    public static IReadOnlyList<string> BrailleFonts()
    {
        using var fonts = new InstalledFontCollection();
        return [.. fonts.Families.Select(f => f.Name)
            .Where(n => n.Contains("braille", StringComparison.OrdinalIgnoreCase) || n.Contains("tiger", StringComparison.OrdinalIgnoreCase))
            .OrderBy(n => n.Contains("29") ? 0 : n.Contains("36") ? 1 : 2).ThenBy(n => n, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>
    /// The font size: the 6-dot ViewPlus fonts carry it in their name (Braille29 at 29 points, Braille36 at 36); the
    /// 8-dot ones go at 43 points, as the ViewPlus manual says.
    /// </summary>
    public static float FontSize(string font) => font.Contains("36") ? 36f : font.Contains("29") ? 29f : 43f;

    /// <summary>
    /// The 8-dot fonts (no "29" or "36" in the name, like the "Computer" Braille fonts): their dots 7 and 8 fall in the
    /// space between Braille lines, and their table is not the BRF one.
    /// </summary>
    public static bool IsEightDot(string font) => !font.Contains("29") && !font.Contains("36");

    /// <summary>
    /// A line of cells as the characters to write with the driver's font: the BRF ones, with letters in lowercase.
    /// BRF letters are the same in either case, but in 8-dot "computer" fonts an uppercase letter gets dot 7.
    /// </summary>
    public static string LineText(string cells) =>
        string.Concat(cells.Select(c => char.ToLowerInvariant(BrfFormat.ToAscii(c)))).TrimEnd();

    /// <summary>A length in whole dots of the grid, rounded up.</summary>
    public static int WholeDots(float inches) => (int)Math.Ceiling(inches / DotInches - 0.001f);

    /// <summary>
    /// The left margin on the back of the sheet, in dots: the desired one plus the dots that put the back cells between
    /// the front ones (the back is embossed from the other side, so its left edge is the front's right edge).
    /// </summary>
    public static int BackMarginDots(float pageWidthInches, int frontDots, int desiredDots)
    {
        int rest = (WholeDots(pageWidthInches) - frontDots - desiredDots) % 5;
        return desiredDots + BackOffsets[rest < 0 ? rest + 5 : rest];
    }

    /// <summary>How many cells and lines fit on a page, with the margins, given the size of a cell and of a line.</summary>
    public static (int Cells, int Lines) Capacity(float pageWidthInches, float pageHeightInches, float cellWidthInches, float lineHeightInches)
    {
        float width = pageWidthInches - 2 * MarginInches, height = pageHeightInches - MarginInches - BottomMarginInches;
        return ((int)Math.Floor(width / cellWidthInches + 0.01f), (int)Math.Floor(height / lineHeightInches + 0.01f));
    }

    /// <summary>
    /// How many cells and lines fit on the sheet the printer's driver uses, with this font; also the sheet size in
    /// inches, to tell the user.
    /// </summary>
    public static (int Cells, int Lines, float Width, float Height) Capacity(string printer, string font)
    {
        var settings = new PrinterSettings { PrinterName = printer };
        if (!settings.IsValid)
            throw new InvalidOperationException($"La stampante {printer} non è disponibile.");
        var page = settings.DefaultPageSettings;
        float width = page.PaperSize.Width / 100f, height = page.PaperSize.Height / 100f;
        if (page.Landscape)
            (width, height) = (height, width);
        using var braille = BrailleFont(font);
        var (cells, lines) = Capacity(width, height, CellWidth(braille), LineHeight(braille));
        return (cells, lines, width, height);
    }

    /// <summary>The distance from one line to the next: the font's own line spacing, in inches.</summary>
    public static float LineHeight(Font font) =>
        font.SizeInPoints * font.FontFamily.GetLineSpacing(font.Style) / font.FontFamily.GetEmHeight(font.Style) / 72f;

    /// <summary>The width of a cell (all six dots), in inches.</summary>
    public static float CellWidth(Font font)
    {
        using var bitmap = new Bitmap(1, 1);
        using var g = Graphics.FromImage(bitmap);
        g.PageUnit = GraphicsUnit.Inch;
        return g.MeasureString(new string('=', 10), font, PointF.Empty, StringFormat.GenericTypographic).Width / 10;
    }

    private static Font BrailleFont(string font)
    {
        var braille = new Font(font, FontSize(font), GraphicsUnit.Point);
        if (string.Equals(braille.Name, font, StringComparison.OrdinalIgnoreCase))
            return braille;
        // Windows silently puts another font in place of a missing one: that would emboss letters, not Braille.
        braille.Dispose();
        throw new InvalidOperationException($"Il font {font} non è installato.");
    }

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

        using var braille = BrailleFont(font);
        using var ink = new SolidBrush(BrailleColor);
        float lineHeight = LineHeight(braille);
        var format = StringFormat.GenericTypographic;
        int frontDots = WholeDots(MarginInches), topDots = WholeDots(MarginInches);
        int next = 0;
        document.PrintPage += (_, e) =>
        {
            var g = e.Graphics!;
            g.PageUnit = GraphicsUnit.Inch;
            g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;
            bool back = doubleSided && next % 2 == 1;
            float pageWidth = e.PageBounds.Width / 100f;
            int leftDots = back ? BackMarginDots(pageWidth, frontDots, frontDots) : frontDots;
            // The drawing starts where the printer can print (its hard margins), the margins count from the edge of the sheet.
            float x = leftDots * DotInches - e.PageSettings.HardMarginX / 100f;
            float y = topDots * DotInches - e.PageSettings.HardMarginY / 100f;
            var page = pages[next++];
            for (int line = 0; line < page.Count; line++)
            {
                var text = LineText(page[line]);
                if (text.Length > 0)
                    g.DrawString(text, braille, ink, x, y + line * lineHeight, format);
            }
            e.HasMorePages = next < pages.Count;
        };
        document.Print();
    }
}
