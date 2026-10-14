using Eleu.Documents;
using static Eleu.Tests.Support;

namespace Eleu.Tests;

public class MetadataTests
{
    private static readonly Metadata Sample = new("Ricette della nonna", "Anna Rossi", "Cucina {tradizionale}", "dolci, torte", new DateTime(2026, 3, 1, 10, 30, 0));

    [Fact]
    public void Rtf_WrittenAndReadBack_AlsoAfterTheTextBox()
    {
        string? rtf = null;
        RunSta(() =>
        {
            using var field = new RichTextBox();
            field.CreateControl();
            field.Text = "testo";
            rtf = Sample.ToRtf(field.Rtf!);
        });
        Assert.Equal(Sample, Metadata.FromRtf(rtf!));
        // Putting them back does not duplicate them.
        Assert.Equal(Sample with { Title = "Altro" }, Metadata.FromRtf((Sample with { Title = "Altro" }).ToRtf(rtf!)));
    }

    [Fact]
    public void Docx_WrittenAndReadBack()
    {
        var content = new DocumentContent { Metadata = Sample };
        var p = new Paragraph();
        p.Add(new Segment("ciao"));
        content.Paragraphs.Add(p);
        using var stream = new MemoryStream();
        DocxFormat.Write(content, stream);
        stream.Position = 0;
        var read = DocxFormat.Read(stream).Metadata;
        Assert.Equal(Sample with { Created = null }, read with { Created = null });
        // The time zone can shift the hour: it is enough that the day is right.
        Assert.Equal(Sample.Created!.Value.Date, read.Created!.Value.Date);
    }
}
