using System.Drawing;
using Eleu.Braille;
using static Eleu.Tests.Support;

namespace Eleu.Tests;

public class TactileGraphicsTests
{
    /// <summary>A filled black square in the middle of a white sheet.</summary>
    private static Bitmap Square()
    {
        var b = new Bitmap(400, 400);
        using var g = Graphics.FromImage(b);
        g.Clear(Color.White);
        g.FillRectangle(Brushes.Black, 50, 50, 300, 300);
        return b;
    }

    private static int Points(IEnumerable<string> rows) => rows.Sum(r => r.Sum(c => int.PopCount(c - BrailleTranslator.EmptyCell)));

    [Fact]
    public void Measures_KeepProportionsAndMaximums()
    {
        // Square 30 cells wide (186 mm): 186 mm tall, that is 19 lines of 10 mm.
        Assert.Equal((30, 19), TactileGraphics.Measures(100, 100, 30, 27));
        // Very tall: the height rules, the width shrinks.
        var (cells, rows) = TactileGraphics.Measures(100, 1000, 30, 10);
        Assert.Equal(10, rows);
        Assert.True(cells <= 3);
    }

    [Fact]
    public void Filled_WithInside_Outlines_OnlyBorder()
    {
        using var q = Square();
        var full = TactileGraphics.ToCells(q, 20, 30, new GraphicsOptions(TactileStroke.Filled));
        var outlines = TactileGraphics.ToCells(q, 20, 30, new GraphicsOptions(TactileStroke.Outlines));
        Assert.All(full.Concat(outlines), r => Assert.Equal(20, r.Length));
        Assert.All(full.SelectMany(r => r), c => Assert.InRange(c, '⠀', '⠿'));

        // In the middle: filled in the first, empty in the second.
        int middle = full.Count / 2;
        Assert.Equal('⠿', full[middle][10]);
        Assert.Equal('⠀', outlines[middle][10]);
        Assert.True(Points(outlines) > 0);
        Assert.True(Points(outlines) < Points(full));
        // The left edge of the square is raised.
        Assert.Contains(outlines[middle].Take(6), c => c != '⠀');
    }

    [Fact]
    public void Invert_SwapsLightAndDark()
    {
        using var q = Square();
        var normal = TactileGraphics.ToCells(q, 20, 30, new GraphicsOptions(TactileStroke.Filled));
        var inverted = TactileGraphics.ToCells(q, 20, 30, new GraphicsOptions(TactileStroke.Filled, Invert: true));
        int middle = normal.Count / 2;
        Assert.Equal('⠿', normal[middle][10]);
        Assert.Equal('⠀', inverted[middle][10]);
    }

    [Fact]
    public void Pages_CaptionOnTopThenImage()
    {
        var image = Enumerable.Repeat(new string('⠿', 5), 30).ToList();
        var pages = TactileGraphics.Pages(image, 10, 25, "⠁⠃⠀⠉");
        Assert.Equal(2, pages.Count);
        Assert.Equal("⠁⠃⠀⠉", pages[0][0]);
        Assert.Equal("", pages[0][1]);
        Assert.Equal(25, pages[0].Count);
        Assert.Equal(32 - 25, pages[1].Count);
        Assert.Contains("\f", BrfFormat.Text(pages));
    }

    [Fact]
    public void WrapWords_OnWordBoundaries()
    {
        var word = new string('⠁', 4);
        var text = string.Join('⠀', Enumerable.Repeat(word, 5)); // 5 words of 4 cells
        var rows = TactileGraphics.WrapWords(text, 10);
        Assert.All(rows, r => Assert.True(r.Length <= 10));
        Assert.Equal(3, rows.Count); // 4+1+4, 4+1+4, 4
        Assert.Equal(new string('⠁', 15), TactileGraphics.WrapWords(new string('⠁', 15), 10).Aggregate((a, b) => a + b));
    }

    [Fact]
    public void SwellPaper_BlackOnlyWhereNeeded()
    {
        using var q = Square();
        using var sheet = TactileGraphics.ForSwellPaper(q, 100, 120, new GraphicsOptions(TactileStroke.Outlines), ["⠇"]);
        Assert.Equal(1000, sheet.Width);
        Assert.Equal(1200, sheet.Height);
        // Dot 1 of the caption (top left) is black, the center of the square white (outlines only).
        Assert.Equal(Color.Black.ToArgb(), sheet.GetPixel(8, 8).ToArgb());
        Assert.Equal(Color.White.ToArgb(), sheet.GetPixel(500, 650).ToArgb());
    }

    [Fact]
    public void Dialog_OpensWithPreview()
    {
        RunSta(() =>
        {
            using var q = Square();
            using var window = new UI.TactileGraphicsDialog(q, "Un quadrato", ["Stampante"], null, 32, 27, _ => "⠁");
            var preview = window.Controls.Cast<Control>().SelectMany(All).OfType<TextBox>().First(t => t.ReadOnly);
            Assert.Contains('⠀', preview.Text);
        });

        static IEnumerable<Control> All(Control c) => c.Controls.Cast<Control>().SelectMany(All).Prepend(c);
    }
}
