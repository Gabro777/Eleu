using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using Eleu.Documents;
using static Eleu.Tests.Support;

namespace Eleu.Tests;

public class CommentsTests
{
    private static readonly DateTime When = new(2026, 10, 10, 18, 30, 0);

    /// <summary>"Le uova [fresche] sono buone" with a comment on "fresche" and one at a point, at the end.</summary>
    private static DocumentContent WithComments()
    {
        var content = new DocumentContent();
        content.Comments.Add(new Comment("Meglio di giornata.", "Anna Rossi", When));
        content.Comments.Add(new Comment("Manca il punto?\nControlla.", "Luca"));
        var p = new Paragraph();
        p.Add(new Segment($"Le uova {CommentMarkers.Start(0)}fresche{CommentMarkers.End(0)} sono buone{CommentMarkers.Start(1)}{CommentMarkers.End(1)}"));
        content.Paragraphs.Add(p);
        return content;
    }

    [Fact]
    public void Placeholder_DistinctStartAndEnd()
    {
        Assert.Equal(0, CommentMarkers.StartIndex(CommentMarkers.Start(0)));
        Assert.Equal(-1, CommentMarkers.EndIndex(CommentMarkers.Start(0)));
        Assert.Equal(CommentMarkers.Max - 1, CommentMarkers.EndIndex(CommentMarkers.End(CommentMarkers.Max - 1)));
        // No overlap with the images nor beyond the private use area.
        Assert.Equal(-1, Images.Index(CommentMarkers.Start(0)));
        Assert.True(CommentMarkers.End(CommentMarkers.Max - 1) <= '');
        Assert.Equal("AR", new Comment("x", "Anna Rossi").Initials);
    }

    [Fact]
    public void Dttm_RoundTrip() => Assert.Equal(When, CommentMarkers.FromDttm(CommentMarkers.Dttm(When)));

    [Fact]
    public void Docx_CommentsKeptAndValid()
    {
        using var stream = new MemoryStream();
        DocxFormat.Write(WithComments(), stream);
        stream.Position = 0;
        using (var documents = WordprocessingDocument.Open(stream, false))
        {
            Assert.Empty(new OpenXmlValidator().Validate(documents).Select(e => $"{e.Path?.XPath}: {e.Description}"));
            Assert.Equal(2, documents.MainDocumentPart!.WordprocessingCommentsPart!.Comments!.ChildElements.Count);
        }

        stream.Position = 0;
        var reread = DocxFormat.Read(stream);
        Assert.Equal(WithComments().Paragraphs[0].Text, reread.Paragraphs[0].Text);
        Assert.Equal("Meglio di giornata.", reread.Comments[0].Text);
        Assert.Equal("Anna Rossi", reread.Comments[0].Author);
        Assert.Equal(When, reread.Comments[0].Date);
        Assert.Equal("Manca il punto?\nControlla.", reread.Comments[1].Text);
        Assert.Empty(reread.NotPreserved);
        // For Braille and plain text there are no comments.
        Assert.Equal("Le uova fresche sono buone", Readable.From(reread).Paragraphs[0].Text);
    }

    [Fact]
    public void Docx_WithRevisions_WarnsTheyAreAccepted()
    {
        using var stream = new MemoryStream();
        DocxFormat.Write(WithComments(), stream);
        using (var documents = WordprocessingDocument.Open(stream, true))
        {
            var body = documents.MainDocumentPart!.Document!.Body!;
            body.GetFirstChild<DocumentFormat.OpenXml.Wordprocessing.Paragraph>()!.Append(
                new DocumentFormat.OpenXml.Wordprocessing.InsertedRun(new DocumentFormat.OpenXml.Wordprocessing.Run(
                    new DocumentFormat.OpenXml.Wordprocessing.Text(" aggiunto"))) { Id = "9", Author = "Luca" },
                new DocumentFormat.OpenXml.Wordprocessing.DeletedRun(new DocumentFormat.OpenXml.Wordprocessing.Run(
                    new DocumentFormat.OpenXml.Wordprocessing.DeletedText(" tolto"))) { Id = "10", Author = "Luca" });
        }
        stream.Position = 0;
        var read = DocxFormat.Read(stream);
        Assert.EndsWith(" aggiunto", read.Paragraphs[0].Text);
        Assert.DoesNotContain("tolto", read.Paragraphs[0].Text);
        Assert.Contains(read.NotPreserved, e => e.Contains("revisioni"));
    }

    [Fact]
    public void Rtf_RoundTripThroughTheTextBox()
    {
        var originals = WithComments().Comments;
        var text = WithComments().Paragraphs[0].Text;
        string? read = null;
        var reread = new List<Comment>();
        RunSta(() =>
        {
            using var field = new RichTextBox();
            field.CreateControl();
            field.Text = text;
            var rtf = CommentMarkers.FromPlaceholders(field.Rtf!, originals);
            Assert.Contains(@"\annotation", rtf);
            field.Rtf = CommentMarkers.ToPlaceholders(rtf, reread);
            read = field.Text;
        });
        Assert.Equal(text, read);
        Assert.Equal(2, reread.Count);
        Assert.Equal(originals[0], reread[0]);
        Assert.Equal(originals[1] with { Date = null }, reread[1] with { Date = null });
    }

    [Fact]
    public void Rtf_FromWord_WithoutRange_BecomesCommentAtAPoint()
    {
        // As Word writes it: initials, author, reference mark and annotation group.
        var rtf = @"{\rtf1\ansi Ciao{\*\atnid AR}{\*\atnauthor Anna}\chatn {\*\annotation{\*\atnref 7}\pard\plain {\chatn }Bella frase.} mondo.}";
        var comments = new List<Comment>();
        var converted = CommentMarkers.ToPlaceholders(rtf, comments);
        Assert.Equal([new Comment("Bella frase.", "Anna")], comments);
        Assert.Equal("Ciao" + CommentMarkers.Start(0) + CommentMarkers.End(0) + " mondo.", RtfReader.Read(converted).Paragraphs[0].Text);
    }
}
