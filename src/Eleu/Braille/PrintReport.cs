using System.Drawing.Printing;
using System.Globalization;
using System.Text;

namespace Eleu.Braille;

/// <summary>
/// What a print through the driver asked for and what the driver really gave (sheet, hard margins, printable area),
/// written to a text file the user can send: the driver of an embosser can change the sheet or move the print, and
/// this is the only way to see it from far away.
/// </summary>
public sealed class PrintReport
{
    /// <summary>The file with the report of the last print through the driver.</summary>
    public static readonly string FilePath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Eleu", "stampa-braille.txt");

    private readonly StringBuilder _job = new(), _pages = new();

    private static string In(float inches) =>
        string.Create(CultureInfo.InvariantCulture, $"{inches:0.###} pollici = {inches * 25.4f:0.#} mm");

    private static string Hundredths(float hundredths) => In(hundredths / 100f);

    public void Job(string printer, PrintDocument document, string font, float fontPoints, float cellWidth, float lineHeight,
        SizeF paper, float left, float top, bool doubleSided, int pages)
    {
        var settings = document.PrinterSettings;
        var asked = document.DefaultPageSettings;
        _job.AppendLine(CultureInfo.InvariantCulture, $"Stampa Braille tramite driver - Eleu {typeof(PrintReport).Assembly.GetName().Version?.ToString(3)} - {DateTime.Now:yyyy-MM-dd HH:mm}");
        _job.AppendLine(CultureInfo.InvariantCulture, $"Stampante: {printer}");
        _job.AppendLine(CultureInfo.InvariantCulture, $"Font: {font} a {fontPoints} punti; cella larga {In(cellWidth)}, riga alta {In(lineHeight)}");
        _job.AppendLine(CultureInfo.InvariantCulture, $"Foglio scelto in Eleu: {In(paper.Width)} x {In(paper.Height)}");
        _job.AppendLine(CultureInfo.InvariantCulture, $"Margine sinistro {In(left)}, superiore {In(top)}");
        _job.AppendLine(CultureInfo.InvariantCulture, $"Pagine: {pages}; fronte e retro chiesto: {doubleSided}; la stampante sa farlo: {settings.CanDuplex}; impostato: {settings.Duplex}");
        _job.AppendLine(CultureInfo.InvariantCulture, $"Foglio mandato con la stampa: {Paper(asked.PaperSize)}; orizzontale: {asked.Landscape}");
        _job.AppendLine("Fogli del driver:");
        foreach (PaperSize size in settings.PaperSizes)
            _job.AppendLine(CultureInfo.InvariantCulture, $"  {Paper(size)}");
    }

    private static string Paper(PaperSize size) =>
        string.Create(CultureInfo.InvariantCulture, $"«{size.PaperName}» {Hundredths(size.Width)} x {Hundredths(size.Height)} (tipo {size.Kind}, {size.RawKind})");

    public void Page(int number, bool back, PrintPageEventArgs e, float x, float y, int leftDots)
    {
        var page = e.PageSettings;
        _pages.AppendLine(CultureInfo.InvariantCulture, $"Pagina {number} ({(back ? "retro" : "fronte")}):");
        _pages.AppendLine(CultureInfo.InvariantCulture, $"  foglio ricevuto dal driver: {Paper(page.PaperSize)}; orizzontale: {page.Landscape}");
        _pages.AppendLine(CultureInfo.InvariantCulture, $"  pagina intera: {Hundredths(e.PageBounds.Width)} x {Hundredths(e.PageBounds.Height)}");
        _pages.AppendLine(CultureInfo.InvariantCulture, $"  margini fisici della stampante: sinistro {Hundredths(page.HardMarginX)}, superiore {Hundredths(page.HardMarginY)}");
        _pages.AppendLine(CultureInfo.InvariantCulture, $"  area stampabile: x {Hundredths(page.PrintableArea.X)}, y {Hundredths(page.PrintableArea.Y)}, {Hundredths(page.PrintableArea.Width)} x {Hundredths(page.PrintableArea.Height)}");
        _pages.AppendLine(CultureInfo.InvariantCulture, $"  risoluzione: {e.Graphics?.DpiX} x {e.Graphics?.DpiY} punti per pollice");
        _pages.AppendLine(CultureInfo.InvariantCulture, $"  margine sinistro usato: {leftDots} punti della griglia = {In(leftDots * DriverBraillePrinting.DotInches)}; disegno da x {In(x)}, y {In(y)}");
    }

    /// <summary>Writes the report; a report that cannot be written must not stop the print.</summary>
    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, _job.ToString() + _pages, Encoding.UTF8);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }
}
