using System.Drawing.Printing;
using Eleu.Documents;

namespace Eleu.Editor;

/// <summary>
/// Paper printing of the text box content, page by page with the RichEdit engine,
/// with header, footer and page numbers. Also used for print preview.
/// </summary>
public sealed class DocumentPrinter : PrintDocument
{
    private const float PointsPerHundredthInch = 0.72f;

    private readonly RichTextBox _field;
    private readonly PageLayout _page;
    private List<int> _starts = [];
    private int _index;
    private int _last;

    public DocumentPrinter(RichTextBox field, PageLayout page, string heading)
    {
        _field = field;
        _page = page;
        DocumentName = heading;
        var (width, height) = page.Effective;
        // The sheet in hundredths of an inch; Eleu handles the margins.
        DefaultPageSettings.PaperSize = new PaperSize("Eleu", (int)Math.Round(Math.Min(width, height) / PointsPerHundredthInch),
            (int)Math.Round(Math.Max(width, height) / PointsPerHundredthInch));
        DefaultPageSettings.Landscape = page.Horizontal;
        DefaultPageSettings.Margins = new Margins(0, 0, 0, 0);
    }

    /// <summary>Number of pages that will be printed.</summary>
    public int Pages => _starts.Count;

    protected override void OnBeginPrint(PrintEventArgs e)
    {
        base.OnBeginPrint(e);
        using var measure = PrinterSettings.CreateMeasurementGraphics(DefaultPageSettings);
        var hdc = measure.GetHdc();
        try
        {
            _starts = Pagination.PageStarts(_field, _page, hdc, PhysicalMarginX(), PhysicalMarginY());
        }
        finally
        {
            measure.ReleaseHdc(hdc);
        }

        bool some = PrinterSettings.PrintRange == PrintRange.SomePages;
        _index = some ? Math.Clamp(PrinterSettings.FromPage, 1, _starts.Count) - 1 : 0;
        _last = some ? Math.Clamp(PrinterSettings.ToPage, _index + 1, _starts.Count) - 1 : _starts.Count - 1;
    }

    protected override void OnPrintPage(PrintPageEventArgs e)
    {
        base.OnPrintPage(e);
        int start = Pagination.FirstAfterTheBreak(_field.Text, _starts[_index]);
        int end = _index + 1 < _starts.Count ? _starts[_index + 1] : _field.TextLength;

        var hdc = e.Graphics!.GetHdc();
        try
        {
            Pagination.Draw(_field, _page, hdc, start, end, PhysicalMarginX(), PhysicalMarginY());
        }
        finally
        {
            e.Graphics.ReleaseHdc(hdc);
        }

        DrawBorders(e.Graphics, _index + 1);
        _index++;
        e.HasMorePages = _index <= _last;
    }

    protected override void OnEndPrint(PrintEventArgs e)
    {
        Pagination.ClearCache(_field);
        base.OnEndPrint(e);
    }

    /// <summary>Header at the top and footer with the number at the bottom, centered within the margins.</summary>
    private void DrawBorders(Graphics graphics, int number)
    {
        var (width, height) = _page.Effective;
        graphics.PageUnit = GraphicsUnit.Point;
        using var character = new Font("Segoe UI", 9f);
        using var centered = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        float x = _page.LeftMargin - PhysicalMarginX();
        float amplitude = width - _page.LeftMargin - _page.RightMargin;

        // Header and footer at their distance from the sheet edge, with the variables replaced.
        var header = _page.Compose(_page.Header, number, _starts.Count, DocumentName, DateTime.Today);
        if (header.Length > 0)
            graphics.DrawString(header, character, Brushes.Black,
                new RectangleF(x, _page.HeaderDistance - PhysicalMarginY(), amplitude, 20),
                new StringFormat(centered) { LineAlignment = StringAlignment.Near });

        var footer = _page.Compose(_page.Footer, number, _starts.Count, DocumentName, DateTime.Today);
        if (footer.Length > 0)
            graphics.DrawString(footer, character, Brushes.Black,
                new RectangleF(x, height - _page.FooterDistance - 20 - PhysicalMarginY(), amplitude, 20),
                new StringFormat(centered) { LineAlignment = StringAlignment.Far });
    }

    // Physical printer margins (non-printable area), in points: the drawing origin starts there.
    private float PhysicalMarginX() => DefaultPageSettings.HardMarginX * PointsPerHundredthInch;

    private float PhysicalMarginY() => DefaultPageSettings.HardMarginY * PointsPerHundredthInch;
}
