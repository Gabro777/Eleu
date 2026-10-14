using System.Drawing;
using System.Globalization;

namespace Eleu.Braille;

/// <summary>The bars of a chart: a name and a value for each, in table order.</summary>
public sealed record ChartData(string Title, IReadOnlyList<(string Label, double Value)> Bars);

/// <summary>
/// A bar chart made from a table, for tactile graphics: wide black bars with wide gaps and a base line, nothing else
/// (labels and values in the drawing would be unreadable to the touch). Names and values go in the Braille caption,
/// bar by bar from the left, so the fingers find the bars and the caption says what they are.
/// </summary>
public static class BarChart
{
    public const int MaxBars = 20;

    /// <summary>
    /// The chart of a table given as rows of cell texts: names from the first column, values from <paramref name="column"/>
    /// (or the first column with numbers, if that one has none). A first row without numbers is the header: its cell
    /// of the value column is the title. Null if there are no numbers.
    /// </summary>
    public static ChartData? FromTable(IReadOnlyList<IReadOnlyList<string>> rows, int column)
    {
        if (rows.Count == 0)
            return null;
        int columns = rows.Max(r => r.Count);
        bool header = rows.Count > 1 && !rows[0].Skip(1).Any(c => Number(c) is not null);
        var body = rows.Skip(header ? 1 : 0).ToList();

        bool HasNumbers(int c) => c > 0 && body.Any(r => c < r.Count && Number(r[c]) is not null);
        if (!HasNumbers(column))
            column = Enumerable.Range(1, Math.Max(0, columns - 1)).FirstOrDefault(HasNumbers);
        if (!HasNumbers(column))
            return null;

        var bars = body
            .Where(r => column < r.Count && Number(r[column]) is not null)
            .Select((r, i) => (Label: r[0].Trim().Length > 0 ? r[0].Trim() : $"Barra {i + 1}", Value: Number(r[column])!.Value))
            .ToList();
        var title = header && column < rows[0].Count ? rows[0][column].Trim() : "";
        return bars.Count == 0 ? null : new ChartData(title, bars);
    }

    /// <summary>A number as written in Italian: "1.234,5", "12%", "€ 10", "-3". Null if it is not a number.</summary>
    public static double? Number(string text)
    {
        var t = text.Trim().TrimEnd('%').Replace("€", "").Replace(" ", "").Trim();
        if (t.Length == 0 || !t.Any(char.IsDigit))
            return null;
        return double.TryParse(t, NumberStyles.Number, CultureInfo.GetCultureInfo("it-IT"), out var n) ? n : null;
    }

    /// <summary>The Braille caption: the title, then every bar from the left with its value.</summary>
    public static string Caption(ChartData chart)
    {
        var it = CultureInfo.GetCultureInfo("it-IT");
        var bars = string.Join("; ", chart.Bars.Select(b => $"{b.Label} {b.Value.ToString("0.##", it)}"));
        return $"Grafico{(chart.Title.Length > 0 ? $" {chart.Title}" : "")}. Barre da sinistra: {bars}.";
    }

    /// <summary>The drawing: bars as tall as their value (negative ones hang below the base line).</summary>
    public static Bitmap Draw(ChartData chart)
    {
        const int Width = 1200, Height = 800, Margin = 60;
        var bitmap = new Bitmap(Width, Height);
        using var g = Graphics.FromImage(bitmap);
        g.Clear(Color.White);

        double max = Math.Max(0, chart.Bars.Max(b => b.Value));
        double min = Math.Min(0, chart.Bars.Min(b => b.Value));
        double range = max - min == 0 ? 1 : max - min;
        float usable = Height - 2 * Margin;
        float baseY = Margin + (float)(max / range * usable);

        int n = chart.Bars.Count;
        // Bars and gaps the same width: a gap as wide as a bar is easy to find with a finger.
        float slot = (Width - 2f * Margin) / n;
        float bar = slot / 2;
        for (int i = 0; i < n; i++)
        {
            double value = chart.Bars[i].Value;
            float h = (float)(Math.Abs(value) / range * usable);
            float x = Margin + i * slot + (slot - bar) / 2;
            float y = value >= 0 ? baseY - h : baseY;
            g.FillRectangle(Brushes.Black, x, y, bar, Math.Max(h, 4));
        }
        using var line = new Pen(Color.Black, 8);
        g.DrawLine(line, Margin / 2f, baseY, Width - Margin / 2f, baseY);
        return bitmap;
    }
}
