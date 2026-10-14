using Eleu.Braille;

namespace Eleu.Tests;

public class BrailleTests
{
    /// <summary>Cells written as dot numbers separated by dashes; 0 = empty cell. E.g. "46-14" = capital, c.</summary>
    private static string Points(string cells) =>
        string.Concat(cells.Split('-').Select(c => (char)(0x2800 + c.Where(char.IsAsciiDigit).Sum(p => p == '0' ? 0 : 1 << (p - '1')))));

    [Fact]
    public void Points_TestNotation()
    {
        Assert.Equal((char)0x2801, Points("1")[0]);
        Assert.Equal((char)0x283F, Points("123456")[0]);
        Assert.Equal(BrailleTranslator.EmptyCell, Points("0")[0]);
    }

    [Fact]
    public void Translates_CapitalsAndAccentedLetters()
    {
        // ⠨ capital, c i t t à, space, è, space, b e l l a
        Assert.Equal(Points("46-14-24-2345-2345-12356-0-2346-0-12-15-123-123-1"), BrailleTranslator.Translate("Città è bella"));
    }

    [Fact]
    public void Translates_NumbersAndPunctuation()
    {
        // h o, space, number sign 1 2, space, k g period
        Assert.Equal(Points("125-135-0-3456-1-12-0-13-1245-256"), BrailleTranslator.Translate("ho 12 kg."));
    }

    [Fact]
    public void LongText_FullyTranslated()
    {
        var text = string.Concat(Enumerable.Repeat("casa ", 3000)).TrimEnd();
        var braille = BrailleTranslator.Translate(text);
        Assert.Equal(text.Length, braille.Length);
        Assert.Equal(Points("14-1-234-1"), braille[^4..]);
    }

    [Fact]
    public void Paginate_WrapsBetweenWordsWithIndent()
    {
        var cells = Points("1-1-1-0-12-12-12-0-14-14-14-0-145-145-145");
        var pages = Paginator.Paginate([new BrailleParagraph(cells)], new PageFormat(CellsPerLine: 9, LinesPerPage: 20));

        var rows = Assert.Single(pages);
        Assert.Equal([Points("0-0-1-1-1-0-12-12-12"), Points("14-14-14-0-145-145-145")], rows);
        Assert.All(rows, r => Assert.True(r.Length <= 9));
    }

    [Fact]
    public void Paginate_HeadingWithoutIndentAndBlankLineBefore()
    {
        var pages = Paginator.Paginate(
            [new BrailleParagraph(Points("1")), new BrailleParagraph(Points("12"), IsHeading: true), new BrailleParagraph(Points("14"))],
            new PageFormat(20, 20));
        Assert.Equal([Points("0-0-1"), "", Points("12"), Points("0-0-14")], pages[0]);
    }

    [Fact]
    public void Paginate_SplitsIntoPagesAndBreaksTooLongWords()
    {
        var longWord = Points(string.Join("-", Enumerable.Repeat("1", 25)));
        var pages = Paginator.Paginate([new BrailleParagraph(longWord)], new PageFormat(CellsPerLine: 10, LinesPerPage: 2));

        Assert.Equal(2, pages.Count);
        Assert.Equal([10, 10], pages[0].Select(r => r.Length));
        Assert.Equal([7], pages[1].Select(r => r.Length)); // 2 of indent + 8 + 10 + 7 = 25 cells
    }

    [Fact]
    public void Brf_StandardAsciiTable()
    {
        Assert.Equal(64, BrfFormat.AsciiTable.Length);
        Assert.Equal(' ', BrfFormat.ToAscii(Points("0")[0]));
        Assert.Equal('A', BrfFormat.ToAscii(Points("1")[0]));
        Assert.Equal('B', BrfFormat.ToAscii(Points("12")[0]));
        Assert.Equal('L', BrfFormat.ToAscii(Points("123")[0]));
        Assert.Equal('#', BrfFormat.ToAscii(Points("3456")[0]));
        Assert.Equal('.', BrfFormat.ToAscii(Points("46")[0]));
        Assert.Equal('4', BrfFormat.ToAscii(Points("256")[0]));
        Assert.Equal('\\', BrfFormat.ToAscii(Points("1256")[0]));
        Assert.Equal('=', BrfFormat.ToAscii(Points("123456")[0]));
    }

    [Fact]
    public void Brf_LinesAndPages()
    {
        var pages = new List<List<string>> { new() { Points("1-12"), Points("14") }, new() { Points("145") } };
        Assert.Equal("AB\r\nC\r\n\fD\r\n\f", BrfFormat.Text(pages));
    }
}
