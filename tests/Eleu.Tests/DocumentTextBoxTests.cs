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

    private sealed class TestTextBox : DocumentTextBox
    {
        public string TextDuringFocus { get; private set; } = "non letto";

        public TestTextBox() => GotFocus += (_, _) => TextDuringFocus = Text;

        public void SimulateGotFocus() => OnGotFocus(EventArgs.Empty);
    }
}
