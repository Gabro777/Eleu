using Eleu.Braille;

namespace Eleu.Tests;

/// <summary>The Braille test page: numbered cells and lines, exactly as many as the chosen format.</summary>
public class TestPageTests
{
    private const char Empty = BrailleTranslator.EmptyCell;

    [Fact]
    public void OnePage_AsManyLinesAsChosen_RulerToTheLastCell()
    {
        var page = Assert.Single(TestPage.Pages(new PageFormat(40, 29), BrailleTranslator.Translate));
        Assert.Equal(29, page.Count);
        Assert.Equal(40, page[0].Length);
        Assert.Equal('⠁', page[0][0]);   // 1
        Assert.Equal('⠚', page[0][9]);   // 10: the digit 0
        Assert.Equal('⠚', page[0][39]);  // 40
        Assert.Equal("40 celle, 29 righe", BrailleTranslator.BackTranslate(page[1]));
        // Some Italian Braille signs share their cells ("?" and "("): the cells are compared, not the text.
        Assert.Equal(BrailleTranslator.Translate(TestPage.Punctuation), page[2]);
        Assert.Equal(BrailleTranslator.Translate(TestPage.Accents), page[3]);
        Assert.Equal("riga 5", BrailleTranslator.BackTranslate(page[4]));
        Assert.Equal("ultima riga 29", BrailleTranslator.BackTranslate(page[^1]));
        Assert.All(page, line => Assert.True(line.Length <= 40));
    }

    [Fact]
    public void DoubleSided_ABackPage_WithoutTheMarginOnTheBack()
    {
        var pages = TestPage.Pages(new PageFormat(32, 10) { LeftMargin = 3, DoubleSided = true }, BrailleTranslator.Translate);
        Assert.Equal(2, pages.Count);
        Assert.All(pages[0], line => Assert.StartsWith(new string(Empty, 3), line));
        Assert.Equal(29, pages[0][0].Length - 3);                // the ruler fills what is left after the margin
        Assert.StartsWith("retro", BrailleTranslator.BackTranslate(pages[1][1]));
        Assert.NotEqual(Empty, pages[1][0][0]);                  // no margin on the back
    }

    [Fact]
    public void Brf_LinesAndAPageBreak()
    {
        var brf = BrfFormat.Text(TestPage.Pages(new PageFormat(40, 25), BrailleTranslator.Translate));
        Assert.Equal(25, brf.Split("\r\n").Length - 1);
        Assert.EndsWith("\f", brf);
    }
}
