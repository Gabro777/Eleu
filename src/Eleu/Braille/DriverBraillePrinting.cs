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
public static partial class DriverBraillePrinting
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

    /// <summary>The usual sheets for Braille printers, in inches; the last choice in the window is a sheet of any size.</summary>
    public static readonly IReadOnlyList<BraillePaper> Papers =
    [
        new("11,5 × 11 pollici (carta Braille)", 11.5f, 11f),
        new("12 × 11 pollici", 12f, 11f),
        new("12 × 12 pollici", 12f, 12f),
        new("11 × 11,5 pollici", 11f, 11.5f),
        new("8,5 × 11 pollici (Lettera)", 8.5f, 11f),
        new("A4 (21 × 29,7 cm)", 8.27f, 11.69f),
    ];

    /// <summary>The installed fonts that look like Braille fonts, for the window: see <see cref="UsableFonts"/>.</summary>
    public static IReadOnlyList<string> BrailleFonts()
    {
        using var fonts = new InstalledFontCollection();
        return UsableFonts(fonts.Families.Select(f => f.Name)
            .Where(n => n.Contains("braille", StringComparison.OrdinalIgnoreCase) || n.Contains("tiger", StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// All of them, the ones with the table of BRF files first (Braille29 before Braille36), then the 6-dot ones with
    /// other tables, last the 8-dot and print ones. The driver installs them for other programs: they can be chosen,
    /// but the window and the printing warn that the dots could come out wrong.
    /// </summary>
    public static IReadOnlyList<string> UsableFonts(IEnumerable<string> fonts) =>
        [.. fonts.OrderBy(n => HasBrfTable(n) ? (n.Contains("29") ? 0 : 1) : IsEightDot(n) ? 3 : 2).ThenBy(n => n, StringComparer.OrdinalIgnoreCase)];

    /// <summary>
    /// The font to propose: the one chosen last time if it has the table of BRF files, otherwise the first one that has
    /// it (a font chosen by mistake, like «Braille36 IT», is not kept); null without Braille fonts.
    /// </summary>
    public static string? ProposedFont(IReadOnlyList<string> fonts, string? lastTime) =>
        lastTime is not null && fonts.Contains(lastTime) && (HasBrfTable(lastTime) || !fonts.Any(HasBrfTable))
            ? lastTime
            : fonts.FirstOrDefault(HasBrfTable) ?? fonts.FirstOrDefault();

    /// <summary>
    /// The 6-dot fonts with the table of BRF files: nothing after the size in the name, or "US" (Braille29, Braille36 US). The
    /// variants with a language after it (like «Braille36 IT») use that language's table: the punctuation would come out wrong.
    /// </summary>
    public static bool HasBrfTable(string font) => BrfTableFont().IsMatch(font);

    [System.Text.RegularExpressions.GeneratedRegex(@"^\s*(braille|tiger)\s*-?\s*(29|36)(\s*-?\s*us)?\s*$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
    private static partial System.Text.RegularExpressions.Regex BrfTableFont();

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

    /// <summary>How many cells and lines fit on the sheet (in inches) with this font.</summary>
    public static (int Cells, int Lines) Capacity(SizeF paper, string font)
    {
        using var braille = BrailleFont(font);
        return Capacity(paper.Width, paper.Height, CellWidth(braille), LineHeight(braille));
    }

    /// <summary>
    /// The sheet for the job, as Eleu chose it (like the other Braille programs do): one of the driver's own if it has
    /// that size, otherwise one made to measure. The size set in the driver's preferences does not count.
    /// </summary>
    public static PaperSize PaperFor(PrinterSettings settings, SizeF paper)
    {
        int width = (int)Math.Round(paper.Width * 100), height = (int)Math.Round(paper.Height * 100);
        return settings.PaperSizes.Cast<PaperSize>().FirstOrDefault(p => p.Kind != PaperKind.Custom
                   && Math.Abs(p.Width - width) <= 5 && Math.Abs(p.Height - height) <= 5)
               ?? new PaperSize("Eleu Braille", width, height);
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
    /// <param name="paper">The sheet, in inches.</param>
    public static void Print(string printer, IReadOnlyList<List<string>> pages, string font, SizeF paper, bool doubleSided, string documentName, string? toFile = null)
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
        document.DefaultPageSettings.PaperSize = PaperFor(document.PrinterSettings, paper);
        document.DefaultPageSettings.Landscape = false;
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

/// <summary>A sheet for the Braille printer: its name in the window and its size in inches.</summary>
public sealed record BraillePaper(string Name, float Width, float Height)
{
    public SizeF Size => new(Width, Height);
}
