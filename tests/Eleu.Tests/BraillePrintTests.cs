using Eleu.Braille;

namespace Eleu.Tests;

/// <summary>Double spacing, double-sided and blank lines for the fold, on the Braille page.</summary>
public class BraillePrintTests
{
    private const char V = BrailleTranslator.EmptyCell;

    private static string Cells(string text) => new(text.Select(c => c == ' ' ? V : '⠿').ToArray());

    private static string Translate(string text) => "#" + text;

    /// <summary>Many one-line paragraphs, to fill several pages.</summary>
    private static List<BrailleParagraph> Lines(int howMany) =>
        Enumerable.Range(0, howMany).Select(_ => new BrailleParagraph(Cells("uno due tre"))).ToList();

    private static bool Empty(string row) => row.Trim(V).Length == 0;

    [Fact]
    public void DoubleSpacing_BlankLineBetweenLines()
    {
        var format = new PageFormat(20, 25) { DoubleSpacing = true };
        var page = Paginator.Paginate(Lines(5), format)[0];
        // text, blank, text, blank...: no text line right next to the following one.
        for (int i = 0; i + 1 < page.Count; i++)
            Assert.False(!Empty(page[i]) && !Empty(page[i + 1]), $"righe {i} e {i + 1} attaccate");
        Assert.Equal(5, page.Count(r => !Empty(r)));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(0)]
    [InlineData(4)]
    public void ParagraphIndent_AsChosen_NotForHeadings(int indent)
    {
        var cells = new string((char)0x2801, 5);
        BrailleParagraph[] text = [new(cells, IsHeading: true, Level: 1), new(cells), new(cells)];
        var page = Paginator.Paginate(text, new PageFormat(20, 25) { ParagraphIndent = indent })[0].Where(r => !Empty(r)).ToList();
        Assert.Equal(cells, page[0].TrimEnd(V));
        Assert.All(page.Skip(1), r => Assert.Equal(new string(V, indent) + cells, r.TrimEnd(V)));
    }

    [Fact]
    public void DoubleSpacing_MorePages()
    {
        var single = Paginator.Paginate(Lines(30), new PageFormat(20, 25)).Count;
        var doubled = Paginator.Paginate(Lines(30), new PageFormat(20, 25) { DoubleSpacing = true }).Count;
        Assert.True(doubled > single);
    }

    [Theory]
    [InlineData(27, BraillePageNumber.TopRight)]
    [InlineData(28, BraillePageNumber.BottomRight)]
    [InlineData(25, BraillePageNumber.None)]
    public void Fold_TwoBlankLinesAtHalfPage(int rows, BraillePageNumber number)
    {
        var format = new PageFormat(20, rows) { FoldInHalf = true, Number = number };
        var pages = Paginator.Paginate(Lines(100), format, Translate);
        var (before, second) = format.FoldLines;
        Assert.Equal(rows / 2 - 1, before);
        foreach (var page in pages.Take(pages.Count - 1)) // the last one may end before the fold
        {
            Assert.Equal(rows, page.Count);
            Assert.True(Empty(page[before]) && Empty(page[second]));
            Assert.False(Empty(page[before - 1]));
            Assert.False(Empty(page[second + 1]));
        }
    }

    [Fact]
    public void Fold_NoTextIsLost()
    {
        var without = Paginator.Paginate(Lines(60), new PageFormat(20, 27)).SelectMany(p => p).Count(r => !Empty(r));
        var withFold = Paginator.Paginate(Lines(60), new PageFormat(20, 27) { FoldInHalf = true }).SelectMany(p => p).Count(r => !Empty(r));
        Assert.Equal(60, without);
        Assert.Equal(60, withFold);
    }

    [Fact]
    public void DoubleSided_EveryPageNumbered_OnTheBackMarginAndNumberSwitchSides()
    {
        var format = new PageFormat(20, 10) { Number = BraillePageNumber.TopRight, LeftMargin = 3, DoubleSided = true };
        var pages = Paginator.Paginate(Lines(40), format, Translate);
        Assert.True(pages.Count >= 4);
        for (int i = 0; i < pages.Count; i++)
        {
            var head = pages[i][0];
            var number = "#" + (i + 1);
            Assert.Contains(number, head);
            bool back = i % 2 == 1;
            if (back)
            {
                // On the back: no left margin, the number ends 3 cells before the right edge.
                Assert.Equal(20 - 3, head.Length);
                Assert.EndsWith(number, head);
                Assert.StartsWith(new string(V, 2) + "⠿", pages[i][1]); // only the paragraph indent
            }
            else
            {
                Assert.Equal(20, head.Length);
                Assert.EndsWith(number, head);
                Assert.StartsWith(new string(V, 5) + "⠿", pages[i][1]); // margin and indent
            }
        }
    }

    [Fact]
    public void SingleSided_MarginAlwaysOnTheLeft()
    {
        var format = new PageFormat(20, 10) { LeftMargin = 2 };
        var pages = Paginator.Paginate(Lines(40), format);
        Assert.All(pages, p => Assert.StartsWith(new string(V, 2), p[0]));
    }
}
