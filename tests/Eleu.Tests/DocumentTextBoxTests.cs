using Eleu.Editor;
using static Eleu.Tests.Support;

namespace Eleu.Tests;

public class DocumentTextBoxTests
{
    [Fact]
    public void NormalText_OutsideOfGotFocus()
    {
        string read = "";
        RunSta(() =>
        {
            using var field = new DocumentTextBox();
            field.CreateControl();
            field.Text = "uno\ndue";
            read = field.Text;
        });
        Assert.Equal("uno\ndue", read);
    }

    [Fact]
    public void DuringFocus_TextIsEmpty()
    {
        // This is what the Windows Forms notification reads: empty, the screen reader does not repeat the document.
        string read = "non letto";
        RunSta(() =>
        {
            using var field = new TestTextBox();
            field.CreateControl();
            field.Text = "documento lungo";
            field.SimulateGotFocus();
            read = field.TextDuringFocus;
        });
        Assert.Equal("", read);
    }

    /// <summary>The word being read aloud is brought on screen, but the cursor and the text stay where they are.</summary>
    [Fact]
    public void ReadingWord_ScrollsToTheWord_CursorAndTextUnchanged()
    {
        int topCharacter = 0, cursor = -1, target = 0;
        Rectangle? band = null;
        Size client = default;
        bool modified = true;
        RunSta(() =>
        {
            using var form = new Form { Width = 400, Height = 250 };
            var field = new DocumentTextBox { Dock = DockStyle.Fill };
            form.Controls.Add(field);
            form.Show();
            field.Text = string.Join("\n", Enumerable.Range(1, 200).Select(i => $"riga numero {i}"));
            field.Select(0, 0);
            field.Modified = false;
            target = field.Text.IndexOf("riga numero 150", StringComparison.Ordinal);

            field.ReadingWord = (target, target + 4);
            field.ShowReadingWord();
            Application.DoEvents();

            topCharacter = field.GetCharIndexFromPosition(new Point(5, 5));
            band = (Rectangle?)typeof(DocumentTextBox).GetMethod("WordRectangle", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .Invoke(field, [field.ReadingWord]);
            client = field.ClientSize;
            cursor = field.SelectionStart;
            modified = field.Modified;
            field.ReadingWord = null;
        });
        Assert.InRange(topCharacter, target - 200, target);
        Assert.Equal(0, cursor);
        Assert.False(modified);
        Assert.NotNull(band);
        Assert.True(band.Value.Width > 10 && band.Value.Height > 5, $"{band}");
        Assert.True(new Rectangle(Point.Empty, client).Contains(band.Value), $"{band} fuori da {client}");
    }

    private sealed class TestTextBox : DocumentTextBox
    {
        public string TextDuringFocus { get; private set; } = "non letto";

        public TestTextBox() => GotFocus += (_, _) => TextDuringFocus = Text;

        public void SimulateGotFocus() => OnGotFocus(EventArgs.Empty);
    }
}
