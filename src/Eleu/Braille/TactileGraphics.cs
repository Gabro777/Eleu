using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Eleu.Braille;

/// <summary>How an image becomes raised: only the outlines (usually easier to read) or the dark areas filled.</summary>
public enum TactileStroke
{
    Outlines,
    Filled,
}

/// <param name="Threshold">How much a point must stand out from the background to be raised: from 1 (a lot) to 254 (very little); 0 = Eleu chooses it.</param>
/// <param name="Invert">Raise the background instead of the figure.</param>
public sealed record GraphicsOptions(TactileStroke Stroke = TactileStroke.Outlines, int Threshold = 0, bool Invert = false);

/// <summary>
/// Tactile images. Two ways:
/// <list type="bullet">
/// <item>with the Braille printer: the image becomes a grid of cells, each dot of the cell is a raised
/// dot (dot graphics, in the BRF file like the text);</item>
/// <item>with microcapsule (or "swell") paper: a simple black-on-white drawing with thick strokes is printed,
/// which the fuser makes swell; the caption is in drawn Braille dots, which swell too.</item>
/// </list>
/// The measures are real Braille ones: dots 2.5 mm apart, cells every 6.2 mm, lines every 10 mm.
/// </summary>
public static class TactileGraphics
{
    public const double DotPitch = 2.5, CellPitch = 6.2, LinePitch = 10.0;
    private const int PixelsPerMm = 4;

    /// <summary>How many Braille cells and lines the image takes, without exceeding the maximums and without distorting it.</summary>
    public static (int Cells, int Rows) Measures(int width, int height, int maxCells, int maxLines)
    {
        double w = maxCells * CellPitch;
        double h = w * height / Math.Max(1, width);
        if (h > maxLines * LinePitch)
        {
            h = maxLines * LinePitch;
            w = h * width / Math.Max(1, height);
        }
        return (Math.Clamp((int)Math.Ceiling(w / CellPitch), 1, maxCells), Math.Clamp((int)Math.Ceiling(h / LinePitch), 1, maxLines));
    }

    /// <summary>
    /// The image as lines of Braille cells (characters from U+2800): at most <paramref name="maxCells"/>
    /// per line and <paramref name="maxLines"/> lines, centered on the line.
    /// </summary>
    public static List<string> ToCells(Image image, int maxCells, int maxLines, GraphicsOptions options)
    {
        var (cells, rows) = Measures(image.Width, image.Height, maxCells, maxLines);
        // The area of the whole line (to center), as tall as the lines needed.
        double widthMm = maxCells * CellPitch, heightMm = rows * LinePitch;
        double scale = Math.Min(cells * CellPitch / image.Width, heightMm / image.Height);
        double wMm = image.Width * scale, hMm = image.Height * scale;
        var area = new RectangleF((float)((widthMm - wMm) / 2), 0, (float)wMm, (float)hMm);

        int lp = (int)Math.Ceiling(widthMm * PixelsPerMm), ap = (int)Math.Ceiling(heightMm * PixelsPerMm);
        // Outlines as thick as the distance between two dots: so a line does not fall between one dot and the next.
        var mask = Mask(image, lp, ap, Scale(area, PixelsPerMm), options, thickness: (int)(DotPitch * PixelsPerMm));
        double min = options.Stroke == TactileStroke.Outlines ? 0.25 : 0.5;

        var result = new List<string>(rows);
        for (int r = 0; r < rows; r++)
        {
            var row = new char[maxCells];
            for (int c = 0; c < maxCells; c++)
            {
                int points = 0;
                for (int dc = 0; dc < 2; dc++)
                    for (int dr = 0; dr < 3; dr++)
                    {
                        // Each dot looks at its part of the cell (half the width, a third of the height): the
                        // areas between dots and between lines are not left out.
                        double x = c * CellPitch + dc * CellPitch / 2, y = r * LinePitch + dr * LinePitch / 3;
                        if (Coverage(mask, x, y, CellPitch / 2, LinePitch / 3) >= min)
                            points |= 1 << (dc * 3 + dr); // dots 1-2-3 on the left, 4-5-6 on the right
                    }
                row[c] = (char)(BrailleTranslator.EmptyCell + points);
            }
            result.Add(new string(row));
        }
        return result;
    }

    /// <summary>The share of "black" in a dot's rectangle (top-left corner at x, y; measures in mm).</summary>
    private static double Coverage(bool[,] mask, double xMm, double yMm, double widthMm, double heightMm)
    {
        int x0 = (int)(xMm * PixelsPerMm), y0 = (int)(yMm * PixelsPerMm);
        int x1 = (int)((xMm + widthMm) * PixelsPerMm), y1 = (int)((yMm + heightMm) * PixelsPerMm);
        int full = 0, all = 0;
        for (int y = y0; y < Math.Min(y1, mask.GetLength(0)); y++)
            for (int x = x0; x < Math.Min(x1, mask.GetLength(1)); x++)
            {
                all++;
                if (mask[y, x])
                    full++;
            }
        return all == 0 ? 0 : (double)full / all;
    }

    /// <summary>
    /// The pages for the BRF file: the caption on top (already in Braille, wrapped on words) and a blank line,
    /// then the image; extra lines go to the next page.
    /// </summary>
    public static List<List<string>> Pages(IReadOnlyList<string> image, int cells, int linesPerPage, string? caption)
    {
        var rows = new List<string>();
        if (!string.IsNullOrWhiteSpace(caption?.Replace(BrailleTranslator.EmptyCell, ' ')))
        {
            rows.AddRange(WrapWords(caption!, cells));
            rows.Add("");
        }
        rows.AddRange(image.Select(r => r.TrimEnd(BrailleTranslator.EmptyCell)));
        return rows.Chunk(Math.Max(1, linesPerPage)).Select(p => p.ToList()).ToList();
    }

    /// <summary>A Braille text split into lines of at most <paramref name="cells"/> cells, between words.</summary>
    public static List<string> WrapWords(string braille, int cells)
    {
        var rows = new List<string>();
        var row = "";
        foreach (var word in braille.Split(BrailleTranslator.EmptyCell, StringSplitOptions.RemoveEmptyEntries))
        {
            var piece = word;
            while (piece.Length > cells)
            {
                if (row.Length > 0)
                    rows.Add(row);
                rows.Add(piece[..cells]);
                row = "";
                piece = piece[cells..];
            }
            if (row.Length == 0)
                row = piece;
            else if (row.Length + 1 + piece.Length <= cells)
                row += BrailleTranslator.EmptyCell + piece;
            else
            {
                rows.Add(row);
                row = piece;
            }
        }
        if (row.Length > 0)
            rows.Add(row);
        return rows;
    }

    /// <summary>
    /// The drawing for microcapsule paper: pure black on white, strokes one and a half millimeters thick, at
    /// print resolution (10 pixels per millimeter). The Braille caption is drawn with real dots,
    /// on top, so it can be read with the fingers after the fuser.
    /// </summary>
    public static Bitmap ForSwellPaper(Image image, float widthMm, float heightMm, GraphicsOptions options, IReadOnlyList<string>? caption)
    {
        const int Resolution = 10; // pixels per millimeter, about 254 dpi
        int lp = (int)(widthMm * Resolution), ap = (int)(heightMm * Resolution);
        float top = caption is { Count: > 0 } ? (float)(caption.Count * LinePitch + LinePitch / 2) : 0;

        // The image in the space below the caption, as large as possible without distorting it.
        float spaceMm = heightMm - top;
        float scale = Math.Min(widthMm / image.Width, spaceMm / image.Height);
        var area = new RectangleF((widthMm - image.Width * scale) / 2, top, image.Width * scale, image.Height * scale);
        var mask = Mask(image, lp, ap, Scale(area, Resolution), options, thickness: (int)(1.5 * Resolution));

        var sheet = new Bitmap(lp, ap, PixelFormat.Format24bppRgb);
        sheet.SetResolution(254, 254);
        var data = sheet.LockBits(new Rectangle(0, 0, lp, ap), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
        var row = new byte[data.Stride];
        for (int y = 0; y < ap; y++)
        {
            for (int x = 0; x < lp; x++)
            {
                byte v = mask[y, x] ? (byte)0 : (byte)255;
                row[x * 3] = row[x * 3 + 1] = row[x * 3 + 2] = v;
            }
            Marshal.Copy(row, 0, data.Scan0 + y * data.Stride, data.Stride);
        }
        sheet.UnlockBits(data);

        if (caption is { Count: > 0 })
        {
            using var g = Graphics.FromImage(sheet);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float diameter = 1.6f * Resolution;
            for (int r = 0; r < caption.Count; r++)
                for (int c = 0; c < caption[r].Length; c++)
                {
                    int points = caption[r][c] - BrailleTranslator.EmptyCell;
                    for (int p = 0; p < 6; p++)
                        if ((points & (1 << p)) != 0)
                        {
                            float x = (float)((c * CellPitch + (p / 3) * DotPitch) * Resolution);
                            float y = (float)((r * LinePitch + (p % 3) * DotPitch) * Resolution);
                            g.FillEllipse(Brushes.Black, x, y, diameter, diameter);
                        }
                }
        }
        return sheet;
    }

    private static RectangleF Scale(RectangleF r, float factor) => new(r.X * factor, r.Y * factor, r.Width * factor, r.Height * factor);

    /// <summary>
    /// Where the relief goes, pixel by pixel: what stands out from the background more than the threshold
    /// (chosen automatically if not given), then, for outlines, the figure edges thickened up to the stroke width.
    /// </summary>
    public static bool[,] Mask(Image image, int width, int height, RectangleF area, GraphicsOptions options, int thickness)
    {
        var similar = Similarity(image, width, height, area);
        int threshold = options.Threshold is > 0 and < 255 ? options.Threshold : Otsu(similar, area);
        var black = new bool[height, width];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                black[y, x] = area.Contains(x + 0.5f, y + 0.5f) && (similar[y, x] < threshold) != options.Invert;
        if (options.Stroke == TactileStroke.Filled)
            return black;

        var borders = new bool[height, width];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                borders[y, x] = black[y, x] && (x == 0 || y == 0 || x == width - 1 || y == height - 1
                    || !black[y, x - 1] || !black[y, x + 1] || !black[y - 1, x] || !black[y + 1, x]);
        return Dilate(borders, Math.Max(0, thickness / 2));
    }

    /// <summary>Every lit pixel also lights those in the square of side 2r+1 around it (in two passes, fast).</summary>
    private static bool[,] Dilate(bool[,] m, int r)
    {
        if (r == 0)
            return m;
        int h = m.GetLength(0), w = m.GetLength(1);
        var horizontal = new bool[h, w];
        for (int y = 0; y < h; y++)
        {
            int last = int.MinValue / 2;
            for (int x = 0; x < w + r; x++)
            {
                if (x < w && m[y, x])
                    last = x;
                int c = x - r;
                if (c >= 0 && x - last <= 2 * r)
                    horizontal[y, c] = true;
            }
        }
        var result = new bool[h, w];
        for (int x = 0; x < w; x++)
        {
            int last = int.MinValue / 2;
            for (int y = 0; y < h + r; y++)
            {
                if (y < h && horizontal[y, x])
                    last = y;
                int c = y - r;
                if (c >= 0 && y - last <= 2 * r)
                    result[c, x] = true;
            }
        }
        return result;
    }

    /// <summary>
    /// How much each pixel resembles the background: 255 equal, 0 very far. The background is the most frequent color
    /// on the image border (the median of the border pixels), so a yellow figure on white or a white drawing
    /// on black is raised like a black figure on white. Outside the image everything is background.
    /// </summary>
    private static byte[,] Similarity(Image image, int width, int height, RectangleF area)
    {
        using var canvas = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(canvas))
        {
            g.Clear(Color.White);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(image, area);
        }
        var colors = new byte[height, width * 3];
        var data = canvas.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        var row = new byte[data.Stride];
        for (int y = 0; y < height; y++)
        {
            Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, data.Stride);
            Buffer.BlockCopy(row, 0, colors, y * width * 3, width * 3);
        }
        canvas.UnlockBits(data);

        int x0 = Math.Clamp((int)Math.Ceiling(area.Left), 0, width - 1), x1 = Math.Clamp((int)area.Right - 1, x0, width - 1);
        int y0 = Math.Clamp((int)Math.Ceiling(area.Top), 0, height - 1), y1 = Math.Clamp((int)area.Bottom - 1, y0, height - 1);
        var border = new List<(int X, int Y)>();
        for (int x = x0; x <= x1; x++)
            border.AddRange([(x, y0), (x, y1)]);
        for (int y = y0; y <= y1; y++)
            border.AddRange([(x0, y), (x1, y)]);
        byte Median(int channel)
        {
            var values = border.Select(p => colors[p.Y, p.X * 3 + channel]).Order().ToList();
            return values[values.Count / 2];
        }
        byte b = Median(0), v = Median(1), r = Median(2);

        var similarity = new byte[height, width];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int distance = Math.Max(Math.Abs(colors[y, x * 3] - b), Math.Max(Math.Abs(colors[y, x * 3 + 1] - v), Math.Abs(colors[y, x * 3 + 2] - r)));
                similarity[y, x] = (byte)(255 - distance);
            }
        return similarity;
    }

    /// <summary>Otsu's threshold: the one that best separates figure and background, computed only inside the image.</summary>
    public static int Otsu(byte[,] brightness, RectangleF area)
    {
        var histogram = new long[256];
        long total = 0;
        for (int y = Math.Max(0, (int)area.Top); y < Math.Min(brightness.GetLength(0), (int)Math.Ceiling(area.Bottom)); y++)
            for (int x = Math.Max(0, (int)area.Left); x < Math.Min(brightness.GetLength(1), (int)Math.Ceiling(area.Right)); x++)
            {
                histogram[brightness[y, x]]++;
                total++;
            }
        if (total == 0)
            return 128;
        double sum = 0;
        for (int i = 0; i < 256; i++)
            sum += i * (double)histogram[i];
        double darksSum = 0, best = -1;
        long darks = 0;
        int threshold = 128;
        for (int t = 0; t < 256; t++)
        {
            darks += histogram[t];
            if (darks == 0)
                continue;
            long lights = total - darks;
            if (lights == 0)
                break;
            darksSum += t * (double)histogram[t];
            double darksMean = darksSum / darks, lightsMean = (sum - darksSum) / lights;
            double variance = (double)darks * lights * (darksMean - lightsMean) * (darksMean - lightsMean);
            if (variance > best)
            {
                best = variance;
                threshold = t + 1;
            }
        }
        return threshold;
    }
}
