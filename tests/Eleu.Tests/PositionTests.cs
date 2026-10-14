using Eleu.Documents;

namespace Eleu.Tests;

public class PositionTests
{
    [Fact]
    public void EmptyDocument_LineOneColumnOne()
    {
        Assert.Equal(new Position(1, 1, 1), Position.Compute("", 0));
    }

    [Fact]
    public void CursorInMiddleOfFirstLine()
    {
        Assert.Equal(new Position(1, 4, 1), Position.Compute("ciao mondo", 3));
    }

    [Fact]
    public void CursorAtStartOfSecondLine()
    {
        Assert.Equal(new Position(2, 1, 3), Position.Compute("uno\ndue\ntre", 4));
    }

    [Fact]
    public void CursorAtEndOfDocument()
    {
        Assert.Equal(new Position(3, 4, 3), Position.Compute("uno\ndue\ntre", 11));
    }

    [Fact]
    public void FinalBlankLineCountsAsALine()
    {
        Assert.Equal(new Position(2, 1, 2), Position.Compute("uno\n", 4));
    }

    [Theory]
    [InlineData(-5)]
    [InlineData(999)]
    public void OutOfRangeIndexIsClampedToText(int index)
    {
        var position = Position.Compute("abc", index);
        Assert.InRange(position.Column, 1, 4);
    }

    [Fact]
    public void Description_InItalian()
    {
        Assert.Equal("Riga 2 di 5, colonna 7", new Position(2, 7, 5).Description());
    }
}
