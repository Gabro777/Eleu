using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Writer;

namespace Eleu.Braille;

/// <summary>
/// The Braille pages "in ink", for teachers, parents and colleagues who do not read Braille: every Braille line is drawn
/// as dots and has its letters written underneath, one A4 sheet per Braille page. The letters come from translating the
/// line back, so they match the cells exactly. Sizes follow the real Braille cell, reduced only to fit the sheet.
/// </summary>
public static class InkCopy
{
    private const float Dpi = 150f;
    private const float PxPerMm = Dpi / 25.4f;
    private const float SheetWidth = 210f, SheetHeight = 297f, Margin = 15f, HeaderHeight = 10f; // millimeters

    // Real Braille measures, in millimeters, and the room for the letters under each line.
    private const float CellPitch = 6.2f, DotPitch = 2.5f, Dot = 1.5f, BrailleHeight = 7.5f, LetterHeight = 5f, LineGap = 2f;

    /// <summary>The PDF: one sheet per Braille page.</summary>
    /// <param name="firstNumber">The Braille number of the first page (when only some pages are printed).</param>
    public static byte[] Pdf(IReadOnlyList<List<string>> pages, int cellsPerLine, int linesPerPage, Func<string, string> backTranslate, string title,
        int firstNumber = 1, int? total = null)
    {
        var builder = new PdfDocumentBuilder();
        for (int i = 0; i < pages.Count; i++)
        {
            using var image = Page(pages[i], cellsPerLine, linesPerPage, backTranslate,
                $"{title} — pagina Braille {firstNumber + i} di {total ?? pages.Count}");
            using var png = new MemoryStream();
            image.Save(png, ImageFormat.Png);
            // A4 in PDF points (1/72 inch).
            builder.AddPage(595, 842).AddPng(png.ToArray(), new PdfRectangle(new PdfPoint(0, 0), new PdfPoint(595, 842)));
        }
        return builder.Build();
    }

    /// <summary>One Braille page drawn on an A4 sheet at 150 dpi.</summary>
    public static Bitmap Page(List<string> lines, int cellsPerLine, int linesPerPage, Func<string, string> backTranslate, string header)
    {
        float lineHeight = BrailleHeight + LetterHeight + LineGap;
        float scale = Math.Min(1f, Math.Min((SheetWidth - 2 * Margin) / (cellsPerLine * CellPitch),
            (SheetHeight - 2 * Margin - HeaderHeight) / (Math.Max(1, linesPerPage) * lineHeight)));

        var bitmap = new Bitmap((int)(SheetWidth * PxPerMm), (int)(SheetHeight * PxPerMm));
        bitmap.SetResolution(Dpi, Dpi);
        using var g = Graphics.FromImage(bitmap);
        g.Clear(Color.White);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

        using var headerFont = new Font("Arial", 9f);
        g.DrawString(header, headerFont, Brushes.DimGray, Px(Margin), Px(Margin));

        using var letters = new Font("Arial", Math.Max(6f, 11f * scale));
        using var empty = new Pen(Color.FromArgb(200, 200, 200), 1f);
        float top = Margin + HeaderHeight;
        foreach (var line in lines)
        {
            int first = line.TakeWhile(c => c == BrailleTranslator.EmptyCell).Count();
            for (int c = first; c < line.Length; c++)
            {
                int dots = line[c] - BrailleTranslator.EmptyCell;
                if (dots == 0)
                    continue;
                float x = Margin + c * CellPitch * scale;
                // Dots 1-2-3 on the left from top to bottom, 4-5-6 on the right; the missing ones are a faint circle.
                for (int d = 0; d < 6; d++)
                {
                    float dx = x + (d < 3 ? 0 : DotPitch * scale);
                    float dy = top + d % 3 * DotPitch * scale;
                    var area = new RectangleF(Px(dx), Px(dy), Px(Dot * scale), Px(Dot * scale));
                    if ((dots & 1 << d) != 0)
                        g.FillEllipse(Brushes.Black, area);
                    else
                        g.DrawEllipse(empty, area);
                }
            }
            // Each word under its Braille word: whoever does not read Braille sees which cells are which word.
            foreach (var (start, cells) in Words(line))
            {
                var word = backTranslate(cells).Trim();
                if (word.Length > 0)
                    g.DrawString(word, letters, Brushes.Black, Px(Margin + start * CellPitch * scale), Px(top + BrailleHeight * scale));
            }
            top += lineHeight * scale;
        }
        return bitmap;
    }

    /// <summary>The Braille words of a line (runs of non-empty cells) with the cell where each starts.</summary>
    public static IEnumerable<(int Start, string Cells)> Words(string line)
    {
        int i = 0;
        while (i < line.Length)
        {
            if (line[i] == BrailleTranslator.EmptyCell)
            {
                i++;
                continue;
            }
            int start = i;
            while (i < line.Length && line[i] != BrailleTranslator.EmptyCell)
                i++;
            yield return (start, line[start..i]);
        }
    }

    private static float Px(float mm) => mm * PxPerMm;
}
