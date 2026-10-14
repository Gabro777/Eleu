using Eleu.Documents;
using Eleu.Editor;
using static Eleu.Tests.Support;

namespace Eleu.Tests;

public class RtfTests
{
    [Fact]
    public void WriteAndRead_KeepTheDocument()
    {
        var original = Sample();
        var reread = RtfReader.Read(RtfWriter.Write(original));
        Assert.Equal(Describe(original), Describe(reread));
    }

    [Fact]
    public void PassingThroughTheTextBox_KeepsTheDocument()
    {
        // The real path: DOCX → model → RTF → RichEdit → RichEdit's RTF → model → DOCX.
        var original = Sample();
        string fieldRtf = "";
        RunSta(() =>
        {
            using var field = new RichTextBox();
            field.CreateControl();
            field.Rtf = RtfWriter.Write(original);
            fieldRtf = field.Rtf!;
        });
        Assert.Equal(Describe(original), Describe(RtfReader.Read(fieldRtf)));
    }

    [Fact]
    public void HeadingsSetInTheTextBox_AreRecognized()
    {
        int level = 0;
        string rtf = "";
        RunSta(() =>
        {
            using var field = new RichTextBox();
            field.CreateControl();
            field.Text = "Titolo\nCorpo";
            field.Select(0, 0);
            NativeRichEdit.SetHeadingLevel(field, 2);
            level = NativeRichEdit.HeadingLevel(field);
            rtf = field.Rtf!;
        });
        Assert.Equal(2, level);
        var content = RtfReader.Read(rtf);
        Assert.Equal(2, content.Paragraphs[0].HeadingLevel);
        Assert.Equal(0, content.Paragraphs[1].HeadingLevel);
    }

    [Fact]
    public void JustifiedAlignment_SetAndRead()
    {
        Alignment read = Alignment.Left;
        RunSta(() =>
        {
            using var field = new RichTextBox();
            field.CreateControl();
            field.Text = "abc";
            NativeRichEdit.SetAlignment(field, Alignment.Justified);
            read = NativeRichEdit.ReadAlignment(field);
        });
        Assert.Equal(Alignment.Justified, read);
    }

    [Fact]
    public void EffectOnMixedSelection_IsMixed()
    {
        bool? state = false;
        RunSta(() =>
        {
            using var field = new RichTextBox();
            field.CreateControl();
            field.Text = "uno due";
            field.Select(0, 3);
            NativeRichEdit.SetEffect(field, NativeRichEdit.Effect.Bold, true);
            field.SelectAll();
            state = NativeRichEdit.EffectState(field, NativeRichEdit.Effect.Bold);
        });
        Assert.Null(state);
    }

    [Fact]
    public void RealRichEditRtf_WithBulletList()
    {
        const string rtf = """
            {\rtf1\ansi\deff0\nouicompat{\fonttbl{\f0\fnil Segoe UI;}{\f1\fnil\fcharset2 Symbol;}}
            {\*\generator Riched20 10.0.26100}\viewkind4\uc1
            \pard\b\f0\fs36\lang1040 Capitolo\par
            Sotto\par

            \pard{\pntext\f1\'B7\tab}{\*\pn\pnlvlblt\pnf1\pnindent360{\pntxtb\'B7}}\fi-360\li360 punto\par
            }
            """;
        var content = RtfReader.Read(rtf);
        Assert.Equal(["Capitolo", "Sotto", "punto"], content.Paragraphs.Select(p => p.Text));
        Assert.True(content.Paragraphs[0].Segments[0].Bold);
        Assert.Equal(18f, content.Paragraphs[0].Segments[0].Size);
        Assert.False(content.Paragraphs[1].Bulleted);
        Assert.True(content.Paragraphs[2].Bulleted);
    }

    [Fact]
    public void SpecialAndUnicodeCharacters()
    {
        // The \u sequence followed by the code is split on purpose, so that no tool turns it into the character.
        var rtf = @"{\rtf1\ansi\ansicpg1252\uc1\pard caff\'e8 \" + "u" + @"8364? \{x\} \\ \emdash\par}";
        Assert.Equal("caffè € {x} \\ —", RtfReader.Read(rtf).Paragraphs[0].Text);
    }
}
