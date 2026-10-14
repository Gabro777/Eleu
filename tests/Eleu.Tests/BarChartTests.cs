using Eleu.Braille;

namespace Eleu.Tests;

/// <summary>Bar charts in relief from a table.</summary>
public class BarChartTests
{
    private static readonly IReadOnlyList<IReadOnlyList<string>> Sales =
    [
        ["Mese", "Vendite", "Note"],
        ["Gennaio", "10", "freddo"],
        ["Febbraio", "1.250,5", ""],
        ["Marzo", "30%", "sole"],
    ];

    [Theory]
    [InlineData("12", 12.0)]
    [InlineData("1.234,5", 1234.5)]
    [InlineData("30%", 30.0)]
    [InlineData("€ 10", 10.0)]
    [InlineData("-3", -3.0)]
    [InlineData("parola", null)]
    [InlineData("", null)]
    public void Number_ItalianWay(string text, double? expected) => Assert.Equal(expected, BarChart.Number(text));

    [Fact]
    public void FromTable_NamesFromTheFirstColumn_TitleFromTheHeader()
    {
        var chart = BarChart.FromTable(Sales, 1)!;
        Assert.Equal("Vendite", chart.Title);
        Assert.Equal([("Gennaio", 10.0), ("Febbraio", 1250.5), ("Marzo", 30.0)], chart.Bars);
    }

    [Fact]
    public void FromTable_CursorOnAColumnWithoutNumbers_TakesTheFirstOneWithNumbers()
    {
        Assert.Equal("Vendite", BarChart.FromTable(Sales, 2)!.Title);
        Assert.Equal("Vendite", BarChart.FromTable(Sales, 0)!.Title);
    }

    [Fact]
    public void FromTable_NoNumbers_Null() =>
        Assert.Null(BarChart.FromTable([["Nome", "Città"], ["Anna", "Roma"]], 1));

    [Fact]
    public void FromTable_WithoutHeader_AllRowsAreBars()
    {
        var chart = BarChart.FromTable([["A", "1"], ["B", "2"]], 1)!;
        Assert.Equal("", chart.Title);
        Assert.Equal(2, chart.Bars.Count);
    }

    [Fact]
    public void Caption_SaysEveryBarFromTheLeft()
    {
        var caption = BarChart.Caption(BarChart.FromTable(Sales, 1)!);
        Assert.Equal("Grafico Vendite. Barre da sinistra: Gennaio 10; Febbraio 1250,5; Marzo 30.", caption);
    }

    [Fact]
    public void Draw_TallerBarForTheBiggerValue()
    {
        using var image = BarChart.Draw(new ChartData("", [("A", 10), ("B", 20)]));
        // Column of black pixels in the middle of each bar.
        int Height(int x) => Enumerable.Range(0, image.Height).Count(y => image.GetPixel(x, y).GetBrightness() < 0.5f);
        int slot = (image.Width - 120) / 2;
        int first = Height(60 + slot / 2), second = Height(60 + slot + slot / 2);
        Assert.True(second > first * 1.7, $"{first} {second}");
    }
}
