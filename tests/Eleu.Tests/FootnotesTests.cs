using Eleu.Documents;
using static Eleu.Tests.Support;

namespace Eleu.Tests;

public class FootnotesTests
{
    // In the test RTFs "|" stands for the backslash.
    private static string Rtf(string text) => text.Replace('|', '\\');

    [Fact]
    public void ToPlaceholders_FootnoteBecomesACharacterAndItsText()
    {
        var texts = new List<string>();
        var rtf = Bookmarks.ToPlaceholders(Rtf(
            "{|rtf1 Uova{|super|chftn}{|footnote|pard|plain{|super|chftn} Meglio se {|b fresche}.|par Di gallina.} e farina.}"), []);
        rtf = FootnoteMarkers.ToPlaceholders(rtf, texts);

        Assert.Equal(["Meglio se fresche.\nDi gallina."], texts);
        Assert.DoesNotContain("footnote", rtf);
        Assert.DoesNotContain("chftn", rtf);
    }

    [Fact]
    public void ThroughTheTextBox_FootnoteComesBackAsRtfFootnote()
    {
        string? read = null, rewritten = null;
        var texts = new List<string>();
        RunSta(() =>
        {
            using var field = new RichTextBox();
            field.CreateControl();
            field.Rtf = FootnoteMarkers.ToPlaceholders(Rtf("{|rtf1 Uova{|super|chftn}{|footnote {|super|chftn} Fresche.} e farina.}"), texts);
            read = field.Text;
            rewritten = FootnoteMarkers.FromPlaceholders(field.Rtf!, texts);
        });

        Assert.Equal("Uova" + FootnoteMarkers.Placeholder(0) + " e farina.", read);
        Assert.Contains(Rtf("{|footnote"), rewritten);
        // Read back, the footnote text is the same.
        var again = new List<string>();
        FootnoteMarkers.ToPlaceholders(rewritten!, again);
        Assert.Equal(["Fresche."], again);
    }

    [Fact]
    public void Docx_FootnotesKept()
    {
        var content = new DocumentContent();
        content.Footnotes.AddRange(["Prima nota.", "Seconda,\nsu due righe."]);
        var p = new Paragraph();
        p.Add(new Segment($"Uova{FootnoteMarkers.Placeholder(0)} e farina{FootnoteMarkers.Placeholder(1)}."));
        content.Paragraphs.Add(p);

        using var stream = new MemoryStream();
        DocxFormat.Write(content, stream);
        stream.Position = 0;
        var errors = new DocumentFormat.OpenXml.Validation.OpenXmlValidator()
            .Validate(DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Open(stream, false));
        Assert.Empty(errors);

        stream.Position = 0;
        var reread = DocxFormat.Read(stream);
        Assert.Equal(["Prima nota.", "Seconda,\nsu due righe."], reread.Footnotes);
        Assert.Equal($"Uova{FootnoteMarkers.Placeholder(0)} e farina{FootnoteMarkers.Placeholder(1)}.", reread.Paragraphs[0].Text);
        Assert.Empty(reread.NotPreserved);
    }
}
