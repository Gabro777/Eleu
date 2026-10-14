using Eleu.UI;
using static Eleu.Tests.Support;

namespace Eleu.Tests;

public class NumberWithUnitTests
{
    private static (decimal Value, string Text) Write(string written)
    {
        (decimal, string) result = default;
        RunSta(() =>
        {
            using var dialogBase = new Form();
            var field = new NumberWithUnit("cm", 2, 0, 10, 0.1m, 2);
            dialogBase.Controls.Add(field);
            dialogBase.CreateControl();
            field.Text = written; // as if the user typed in the field
            result = (field.Value, field.Text);
        });
        return result;
    }

    [Fact]
    public void ShowsTheValueWithTheUnit()
    {
        var (_, text) = Write("2");
        Assert.EndsWith(" cm", text);
    }

    [Theory]
    [InlineData("3,5", 3.5)]
    [InlineData("3.5 cm", 3.5)]
    [InlineData("4 cm", 4)]
    [InlineData("99", 10)]
    [InlineData("abc", 2)]
    public void InterpretWhatIsTyped(string written, double expected)
    {
        Assert.Equal((decimal)expected, Write(written).Value);
    }
}
