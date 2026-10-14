using Eleu.Documents;
using Eleu.Editor;
using static Eleu.Tests.Support;

namespace Eleu.Tests;

public class FormatTests
{
    private static T WithField<T>(string text, Func<RichTextBox, T> action)
    {
        T result = default!;
        RunSta(() =>
        {
            using var field = new RichTextBox();
            field.CreateControl();
            field.Text = text;
            result = action(field);
        });
        return result;
    }

    [Fact]
    public void Character_SetAndReadBack()
    {
        var desired = new CharacterFormat("Times New Roman", 14, true, false, false, true, TextPosition.Superscript,
            new ColorValue(0xC00000), new ColorValue(0xFFFF00), 1.5f);
        var read = WithField("prova", field =>
        {
            field.SelectAll();
            NativeRichEdit.SetCharacterFormat(field, desired);
            return NativeRichEdit.ReadCharacterFormat(field);
        });
        Assert.Equal(desired, read);
    }

    [Fact]
    public void Character_OnlyGivenPropertiesChange()
    {
        var read = WithField("prova", field =>
        {
            field.SelectAll();
            NativeRichEdit.SetCharacterFormat(field, new CharacterFormat(Bold: true, Color: new ColorValue(0x0000FF)));
            NativeRichEdit.SetCharacterFormat(field, new CharacterFormat(Color: new ColorValue(null)));
            return NativeRichEdit.ReadCharacterFormat(field);
        });
        Assert.True(read.Bold);
        Assert.Equal(new ColorValue(null), read.Color);
    }

    [Fact]
    public void Paragraph_SetAndReadBack()
    {
        var desired = new ParagraphFormat(Alignment.Justified, 36, 18, -18, 6, 12, 1.5f, ListKind.None);
        var read = WithField("prova", field =>
        {
            NativeRichEdit.SetParagraphFormat(field, desired);
            return NativeRichEdit.ReadParagraphFormat(field);
        });
        Assert.Equal(desired, read);
    }

    [Fact]
    public void NumberedList_RecognizedWhenSaving()
    {
        var rtf = WithField("uno\ndue", field =>
        {
            field.SelectAll();
            NativeRichEdit.SetParagraphFormat(field, new ParagraphFormat(ListType: ListKind.Numbered, LeftIndent: 0));
            return field.Rtf!;
        });
        var content = RtfReader.Read(rtf);
        Assert.All(content.Paragraphs, p => Assert.True(p.Numbered));
        Assert.All(content.Paragraphs, p => Assert.Equal(0f, p.LeftIndent));
        Assert.Equal(["uno", "due"], content.Paragraphs.Select(p => p.Text));
    }

    [Theory]
    [InlineData(Uppercase.Upper, "CIAO MONDO")]
    [InlineData(Uppercase.Lower, "ciao mondo")]
    [InlineData(Uppercase.Initials, "Ciao Mondo")]
    public void Case_ChangesTextAndKeepsFormatting(Uppercase kind, string expected)
    {
        var (text, bold) = WithField("ciao Mondo", field =>
        {
            field.Select(0, 4);
            NativeRichEdit.SetEffect(field, NativeRichEdit.Effect.Bold, true);
            NativeRichEdit.ToggleCase(field, 0, field.TextLength, kind);
            field.Select(0, 4);
            return (field.Text, NativeRichEdit.EffectState(field, NativeRichEdit.Effect.Bold));
        });
        Assert.Equal(expected, text);
        Assert.True(bold);
    }
}
