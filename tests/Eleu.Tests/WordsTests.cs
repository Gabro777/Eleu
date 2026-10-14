namespace Eleu.Tests;

public class WordsTests
{
    [Fact]
    public void Frequencies_MostUsedFirst_NoShortWords()
    {
        var f = MainWindow.Frequencies("Il gatto e il cane. Il Gatto dorme; il gatto mangia, il cane no.");
        Assert.Equal(("gatto", 3), f[0]);
        Assert.Equal(("cane", 2), f[1]);
        Assert.DoesNotContain(f, x => x.Word is "il" or "e" or "no");
    }

    [Theory]
    [InlineData("20AC", "€")]
    [InlineData("U+20ac", "€")]
    [InlineData("0xE8", "è")]
    [InlineData("1F600", "😀")]
    public void Symbol_FromCode(string code, string expected) =>
        Assert.Equal(expected, UI.SymbolsDialog.FromCode(code));

    [Theory]
    [InlineData("ciao")]
    [InlineData("D800")]
    [InlineData("1F")]
    [InlineData("")]
    public void Symbol_InvalidCode(string code) =>
        Assert.Null(UI.SymbolsDialog.FromCode(code));

    [Fact]
    public void Symbols_AllWithAName() =>
        Assert.All(UI.SymbolsDialog.Symbols, s => Assert.False(string.IsNullOrWhiteSpace(s.Name)));
}
