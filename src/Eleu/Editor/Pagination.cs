using System.ComponentModel;
using System.Drawing.Printing;
using System.Runtime.InteropServices;
using Eleu.Documents;

namespace Eleu.Editor;

/// <summary>
/// Splits the document into pages as it will be printed, with the RichEdit engine (EM_FORMATRANGE):
/// used to print and to say "pagina 2 di 5". Page breaks (form feed character)
/// always start a new page.
/// </summary>
public static class Pagination
{
    public const char PageBreak = '\f';

    private const int EmFormatRange = 0x0400 + 57;

    /// <summary>Position in the text where each page starts (the first is always 0).</summary>
    public static List<int> PageStarts(RichTextBox field, PageLayout page)
    {
        using var graphics = SizedGraphic();
        var hdc = graphics.GetHdc();
        try
        {
            return PageStarts(field, page, hdc);
        }
        finally
        {
            graphics.ReleaseHdc(hdc);
            ClearCache(field);
        }
    }

    public static List<int> PageStarts(RichTextBox field, PageLayout page, IntPtr hdc, float offsetX = 0, float offsetY = 0)
    {
        var text = field.Text;
        var starts = new List<int> { 0 };
        int length = field.TextLength;

        // Stretches between one page break and the next: each starts on a new page.
        var strokes = new List<int> { 0 };
        for (int i = text.IndexOf(PageBreak); i >= 0; i = text.IndexOf(PageBreak, i + 1))
            if (i > 0)
                strokes.Add(i);

        for (int t = 0; t < strokes.Count; t++)
        {
            int start = strokes[t];
            int end = t + 1 < strokes.Count ? strokes[t + 1] : length;
            if (t > 0)
            {
                starts.Add(start);
                // RichEdit already treats the form feed as the end of a page: measure from after it, or it would count an empty page.
                start = FirstAfterTheBreak(text, start);
            }
            while (start < end)
            {
                var range = Range(field, page, hdc, start, end, offsetX, offsetY);
                int next = (int)SendMessage(field.Handle, EmFormatRange, IntPtr.Zero, ref range);
                if (next <= start || next >= end)
                    break;
                starts.Add(next);
                start = next;
            }
        }
        return starts;
    }

    /// <summary>The position after the form feed character a page may start with.</summary>
    public static int FirstAfterTheBreak(string text, int start) =>
        start < text.Length && text[start] == PageBreak ? start + 1 : start;

    /// <summary>Draws on the hdc the text between the two positions, in the area inside the margins.</summary>
    public static void Draw(RichTextBox field, PageLayout page, IntPtr hdc, int start, int end, float offsetX, float offsetY)
    {
        var range = Range(field, page, hdc, start, end, offsetX, offsetY);
        SendMessage(field.Handle, EmFormatRange, 1, ref range);
    }

    public static void ClearCache(RichTextBox field) => SendMessage(field.Handle, EmFormatRange, IntPtr.Zero, IntPtr.Zero);

    /// <summary>Page (from 1) containing the position.</summary>
    public static int PageOf(IReadOnlyList<int> starts, int position)
    {
        int page = 1;
        for (int i = 1; i < starts.Count && starts[i] <= position; i++)
            page = i + 1;
        return page;
    }

    /// <summary>The default printer, if there is one, so the page split is the real one; otherwise the screen.</summary>
    private static Graphics SizedGraphic()
    {
        try
        {
            return new PrinterSettings().CreateMeasurementGraphics();
        }
        catch (Exception e) when (e is InvalidPrinterException or Win32Exception)
        {
            return Graphics.FromHwnd(IntPtr.Zero);
        }
    }

    private static FormatRange Range(RichTextBox field, PageLayout page, IntPtr hdc, int start, int end,
        float offsetX, float offsetY)
    {
        var (width, height) = page.Effective;
        // RECT in twips; the offset removes the printer's physical margins, where the drawing origin starts.
        int X(float points) => RtfWriter.Twip(points - offsetX);
        int Y(float points) => RtfWriter.Twip(points - offsetY);
        return new FormatRange
        {
            hdc = hdc,
            hdcTarget = hdc,
            rc = new Rect(X(page.LeftMargin), Y(page.TopMargin), X(width - page.RightMargin), Y(height - page.BottomMargin)),
            rcPage = new Rect(0, 0, RtfWriter.Twip(width), RtfWriter.Twip(height)),
            cpMin = start,
            cpMax = end,
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect(int left, int top, int right, int bottom)
    {
        public int Left = left, Top = top, Right = right, Bottom = bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FormatRange
    {
        public IntPtr hdc;
        public IntPtr hdcTarget;
        public Rect rc;
        public Rect rcPage;
        public int cpMin;
        public int cpMax;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, ref FormatRange lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, ref FormatRange lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
}
