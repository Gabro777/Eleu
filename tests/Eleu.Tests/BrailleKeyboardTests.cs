using Eleu.Braille;

namespace Eleu.Tests;

public class BrailleKeyboardTests
{
    private static readonly BrailleKeyboard Keyboard = new(BrailleTranslator.Translate);

    /// <summary>Presses the given dot keys together (for example "125") and releases them: returns what is typed.</summary>
    private static string? Cell(BrailleKeyboard keyboard, string points)
    {
        Keys[] keys = [Keys.F, Keys.D, Keys.S, Keys.J, Keys.K, Keys.L];
        var pressed = points.Select(p => keys[p - '1']).ToList();
        foreach (var t in pressed)
            keyboard.Press(t);
        string? written = null;
        foreach (var t in pressed)
            written = keyboard.Release(t) ?? written;
        return written;
    }

    private static string Cell(string points) =>
        ((char)(BrailleTranslator.EmptyCell + points.Sum(p => 1 << (p - '1')))).ToString();

    [Theory]
    [InlineData("1", "a")]
    [InlineData("12", "b")]
    [InlineData("14", "c")]
    [InlineData("1345", "n")]
    [InlineData("2345", "t")]
    public void Letters(string points, string letter) => Assert.Equal(letter, Cell(new BrailleKeyboard(BrailleTranslator.Translate), points));

    [Fact]
    public void NothingIsTypedUntilAllKeysAreReleased()
    {
        var keyboard = new BrailleKeyboard(BrailleTranslator.Translate);
        keyboard.Press(Keys.F);
        keyboard.Press(Keys.D);
        Assert.Null(keyboard.Release(Keys.F)); // D is still pressed
        Assert.Equal("b", keyboard.Release(Keys.D));
    }

    [Fact]
    public void EveryCharacterIsRewrittenFromItsCells()
    {
        // For every character: the cells liblouis gives it, typed one at a time, must give that character back.
        foreach (var c in "abcxyzàèéìòù.,;:!?ABZ0123456789")
        {
            var keyboard = new BrailleKeyboard(BrailleTranslator.Translate);
            string? written = null;
            foreach (var cell in BrailleTranslator.Translate(c.ToString()))
            {
                int bit = cell - BrailleTranslator.EmptyCell;
                var points = string.Concat(Enumerable.Range(0, 6).Where(i => (bit & (1 << i)) != 0).Select(i => (char)('1' + i)));
                written = Cell(keyboard, points);
            }
            Assert.True(written == c.ToString() || char.ToLower(written?[0] ?? ' ') == char.ToLower(c), $"«{c}» è tornato «{written}»");
        }
    }

    [Fact]
    public void Numbers_OneSignForAllDigits_ThenLettersAgain()
    {
        var keyboard = new BrailleKeyboard(BrailleTranslator.Translate);
        var marker = BrailleTranslator.Translate("1")[0] - BrailleTranslator.EmptyCell;
        var markDots = string.Concat(Enumerable.Range(0, 6).Where(i => (marker & (1 << i)) != 0).Select(i => (char)('1' + i)));
        var written = new List<string?> { Cell(keyboard, markDots), Cell(keyboard, "1"), Cell(keyboard, "12") };
        keyboard.Reset(); // like the space bar: ends the number
        written.Add(Cell(keyboard, "1"));
        Assert.Equal([null, "1", "2", "a"], written);

        // The sign with no digits after was a "#" (if Italian Braille uses it for "#").
        Assert.Null(Cell(keyboard, markDots));
        var after = Cell(keyboard, "2345");
        Assert.EndsWith("t", after);
    }

    [Fact]
    public void CellWithoutCharacter_IsWrittenAsCell()
    {
        // A cell that is no character (nor the start of one) stays the Braille cell.
        var keyboard = new BrailleKeyboard(BrailleTranslator.Translate);
        var all = Enumerable.Range(1, 63).Select(b => ((char)(BrailleTranslator.EmptyCell + b)).ToString());
        var without = all.FirstOrDefault(c => !"abcdefghijklmnopqrstuvwxyzàèéìòùç.,;:!?'\"()-/*+=<>@#%&_0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ"
            .Any(x => BrailleTranslator.Translate(x.ToString()).StartsWith(c)));
        if (without is null)
            return;
        int bit = without[0] - BrailleTranslator.EmptyCell;
        var points = string.Concat(Enumerable.Range(0, 6).Where(i => (bit & (1 << i)) != 0).Select(i => (char)('1' + i)));
        Assert.Equal(without, Cell(keyboard, points));
    }
}
