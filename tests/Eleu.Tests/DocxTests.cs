using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using DocumentFormat.OpenXml.Wordprocessing;
using Eleu.Documents;
using static Eleu.Tests.Support;
using W= DocumentFormat.OpenXml.Wordprocessing;

namespace Eleu.Tests;

public class DocxTests
{
    [Fact]
    public void WriteAndRead_KeepTheDocument()
    {
        var original = Sample();
        using var stream = new MemoryStream();
        DocxFormat.Write(original, stream);
        stream.Position = 0;
        Assert.Equal(Describe(original), Describe(DocxFormat.Read(stream)));
    }

    [Fact]
    public void DocxWritten_FollowsTheOpenXmlSchema()
    {
        using var stream = new MemoryStream();
        DocxFormat.Write(Sample(), stream);
        stream.Position = 0;
        using var documents = WordprocessingDocument.Open(stream, false);
        var errors = new OpenXmlValidator().Validate(documents).Select(e => $"{e.Path?.XPath}: {e.Description}");
        Assert.Empty(errors);
    }

    [Fact]
    public void HeadingsRecognizedFromStyleName_AlsoWithItalianId()
    {
        // Word in Italian saves the "heading 1" style with id "Titolo1".
        using var stream = CreateDocx(body =>
        {
            body.Append(Paragraph("Introduzione", style: "Titolo1"));
            body.Append(Paragraph("Testo"));
        }, styles: new Styles(new Style(new StyleName { Val = "heading 1" }) { StyleId = "Titolo1", Type = StyleValues.Paragraph }));

        var content = DocxFormat.Read(stream);
        Assert.Equal(1, content.Paragraphs[0].HeadingLevel);
        Assert.Equal(0, content.Paragraphs[1].HeadingLevel);
    }

    [Fact]
    public void NumberedList_StaysNumbered()
    {
        var numbering = new Numbering(
            new AbstractNum(new Level(new NumberingFormat { Val = NumberFormatValues.Decimal }) { LevelIndex = 0 }) { AbstractNumberId = 5 },
            new NumberingInstance(new AbstractNumId { Val = 5 }) { NumberID = 7 });
        using var stream = CreateDocx(body =>
        {
            body.Append(Paragraph("uova", number: 7));
            body.Append(Paragraph("farina", number: 7));
        }, numbering: numbering);

        var content = DocxFormat.Read(stream);
        Assert.Equal(["uova", "farina"], content.Paragraphs.Select(p => p.Text));
        Assert.All(content.Paragraphs, p => Assert.True(p.Numbered));
        Assert.All(content.Paragraphs, p => Assert.False(p.Bulleted));
    }

    [Fact]
    public void ManualLineBreak_BecomesNewParagraph()
    {
        using var stream = CreateDocx(body =>
            body.Append(new W.Paragraph(new Run(new Text("prima riga"), new Break(), new Text("seconda riga")))));
        Assert.Equal(["prima riga", "seconda riga"], DocxFormat.Read(stream).Paragraphs.Select(p => p.Text));
    }

    [Fact]
    public void MergedCellsReportedAsNotPreserved()
    {
        using var stream = CreateDocx(body =>
            body.Append(new Table(new TableRow(new TableCell(
                new TableCellProperties(new GridSpan { Val = 2 }), new W.Paragraph(new Run(new Text("cella"))))))));
        var content = DocxFormat.Read(stream);
        Assert.Equal(["celle unite o tabelle dentro le tabelle"], content.NotPreserved);
        Assert.Equal("cella", content.Paragraphs[0].Text);
        Assert.Equal(new CellPosition(0, 0, 0), content.Paragraphs[0].Cell);
    }

    [Fact]
    public void SimpleDocument_NothingToReport()
    {
        using var stream = new MemoryStream();
        DocxFormat.Write(Sample(), stream);
        stream.Position = 0;
        Assert.Empty(DocxFormat.Read(stream).NotPreserved);
    }

    private static W.Paragraph Paragraph(string text, string? style = null, int? number = null)
    {
        var properties = new ParagraphProperties();
        if (style is not null)
            properties.Append(new ParagraphStyleId { Val = style });
        if (number is int n)
            properties.Append(new NumberingProperties(new NumberingLevelReference { Val = 0 }, new NumberingId { Val = n }));
        return new W.Paragraph(properties, new Run(new Text(text)));
    }

    private static MemoryStream CreateDocx(Action<Body> fill, Styles? styles = null, Numbering? numbering = null)
    {
        var stream = new MemoryStream();
        using (var documents = WordprocessingDocument.Create(stream, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
        {
            var main = documents.AddMainDocumentPart();
            var body = new Body();
            main.Document = new Document(body);
            fill(body);
            if (styles is not null)
                main.AddNewPart<StyleDefinitionsPart>().Styles = styles;
            if (numbering is not null)
                main.AddNewPart<NumberingDefinitionsPart>().Numbering = numbering;
        }
        stream.Position = 0;
        return stream;
    }
}
