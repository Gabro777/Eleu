using Eleu.Documents;
using static Eleu.Tests.Support;

namespace Eleu.Tests;

public class TablesTests
{
    /// <summary>Prima, a 2x2 table with the header row, Dopo.</summary>
    internal static DocumentContent Sample()
    {
        var content = new DocumentContent();
        void Paragraph(string text, CellPosition? cell = null, bool bold = false)
        {
            var p = new Paragraph { Cell = cell };
            p.Add(new Segment(text, Bold: bold));
            content.Paragraphs.Add(p);
        }
        Paragraph("Prima");
        Paragraph("Nome", new CellPosition(0, 0, 0, 100, true), bold: true);
        Paragraph("Età", new CellPosition(0, 0, 1, 50, true), bold: true);
        Paragraph("Anna", new CellPosition(0, 1, 0, 100));
        Paragraph("", new CellPosition(0, 1, 1, 50)); // empty cell
        Paragraph("Dopo");
        return content;
    }

    private static string Cells(DocumentContent content) =>
        string.Join(" | ", content.Paragraphs.Select(p => p.Cell is { } c
            ? $"[{c.Table},{c.Row},{c.Column} {c.Width}{(c.Header ? " int" : "")}]{p.Text}"
            : p.Text));

    [Fact]
    public void Rtf_TableWrittenAndReadBack()
    {
        var reread = RtfReader.Read(RtfWriter.Write(Sample()));
        Assert.Equal(Cells(Sample()), Cells(reread));
        Assert.True(reread.Paragraphs[1].Segments[0].Bold);
    }

    [Fact]
    public void Rtf_ThroughTheTextBox()
    {
        string? rtf = null;
        RunSta(() =>
        {
            using var field = new RichTextBox();
            field.CreateControl();
            field.Rtf = RtfWriter.Write(Sample());
            rtf = field.Rtf;
        });
        // The text box drops the header row (trhdr): Eleu considers the first row as header.
        Assert.Equal(Cells(Sample()).Replace(" int", ""), Cells(RtfReader.Read(rtf!)));
    }

    [Fact]
    public void Docx_TableWrittenAndReadBack()
    {
        using var stream = new MemoryStream();
        DocxFormat.Write(Sample(), stream);
        stream.Position = 0;
        var errors = new DocumentFormat.OpenXml.Validation.OpenXmlValidator()
            .Validate(DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Open(stream, false));
        Assert.Empty(errors);

        stream.Position = 0;
        var reread = DocxFormat.Read(stream);
        Assert.Equal(Cells(Sample()), Cells(reread));
        Assert.Empty(reread.NotPreserved);
    }

    [Fact]
    public void Rtf_AsWordWritesIt_RowRepeatedAfterTheCells()
    {
        // Word puts the row definition before the cells and repeats it in a group before "row".
        // In the test RTFs "|" stands for the backslash.
        static string Row(string a, string b) =>
            "|trowd|irow0|trgaph108|cellx2000|cellx4000|pard|plain|intbl " + a + "|cell " + b +
            "|cell |pard|plain|intbl {|trowd|irow0|trgaph108|cellx2000|cellx4000|row }";
        var rtf = ("{|rtf1 " + Row("Nome", "Anni") + Row("Anna", "40") + "|pard|plain Dopo|par}").Replace('|', (char)92);
        var read = RtfReader.Read(rtf);
        Assert.Equal("[0,0,0 100]Nome | [0,0,1 100]Anni | [0,1,0 100]Anna | [0,1,1 100]40 | Dopo", Cells(read));
    }

    [Fact]
    public void Rtf_TwoSeparateTables()
    {
        var content = Sample();
        var p = new Paragraph { Cell = new CellPosition(1, 0, 0, 80) };
        p.Add(new Segment("Sola"));
        content.Paragraphs.Add(p);
        var reread = RtfReader.Read(RtfWriter.Write(content));
        Assert.Equal(Cells(content), Cells(reread));
    }
}

public class TextStructureTests
{
    // The text box text with "Prima", a 2x2 table and "Dopo", as Editor.DocumentTextBox gives it.
    private static readonly string Text = string.Concat(
        "Prima", "\n",
        TextStructure.RowStart, "\n", "A1", TextStructure.CellEnd, "B1", TextStructure.CellEnd, TextStructure.RowEnd, "\n",
        TextStructure.RowStart, "\n", "A2", TextStructure.CellEnd, "", TextStructure.CellEnd, TextStructure.RowEnd, "\n",
        "Dopo");

    [Fact]
    public void Paragraphs_EachCellIsAParagraphAndTechnicalRowsAreNot()
    {
        var paragraphs = TextStructure.Paragraphs(Text).Select(p => Text[p.Start..p.End]);
        Assert.Equal(["Prima", "A1", "B1", "A2", "", "Dopo"], paragraphs);
    }

    [Fact]
    public void CellAt_RowAndColumn()
    {
        int b1 = Text.IndexOf("B1");
        var cell = TextStructure.CellAt(Text, b1 + 1)!;
        Assert.Equal((0, 0, 1), (cell.TableNumber, cell.Row, cell.Column));
        Assert.Equal("B1", Text[cell.Text.Start..cell.Text.End]);

        int a2 = Text.IndexOf("A2");
        Assert.Equal((1, 0), (TextStructure.CellAt(Text, a2)!.Row, TextStructure.CellAt(Text, a2)!.Column));
        Assert.Equal((1, 1), (TextStructure.CellAt(Text, a2 + 3)!.Row, TextStructure.CellAt(Text, a2 + 3)!.Column));
        Assert.Null(TextStructure.CellAt(Text, 2));
        Assert.Null(TextStructure.CellAt(Text, Text.Length - 1));
    }

    [Fact]
    public void Position_CellsAreLines()
    {
        var position = Position.Compute(Text, Text.IndexOf("Dopo") + 2);
        Assert.Equal(new Position(6, 3, 6), position);
        Assert.Equal(3, Position.Compute(Text, Text.IndexOf("B1")).Line);
    }

    [Fact]
    public void Statistics_WithoutTechnicalCharacters()
    {
        var statistics = Statistics.Compute(Text);
        Assert.Equal(5, statistics.Words);
        Assert.Equal("PrimaA1B1A2Dopo".Length, statistics.Characters);
    }
}

public class GridTests
{
    private static Grid Sample()
    {
        var content = new DocumentContent();
        string[][] rows = [["Nome", "Età"], ["Zeno", "9"], ["anna", "10"], ["Bice", ""]];
        for (int r = 0; r < rows.Length; r++)
            for (int c = 0; c < rows[r].Length; c++)
            {
                var p = new Paragraph { Cell = new CellPosition(0, r, c, c == 0 ? 100 : 40) };
                p.Add(new Segment(rows[r][c]));
                content.Paragraphs.Add(p);
            }
        return Grid.From(content.Paragraphs);
    }

    private static string Column(Grid g, int c) => string.Join(",", g.Rows.Select(r => Grid.Text(r[c])));

    [Fact]
    public void Sort_LettersIgnoringCaseAndNumbersAsNumbers()
    {
        var g = Sample();
        g.Sort(0, ascending: true, header: true);
        Assert.Equal("Nome,anna,Bice,Zeno", Column(g, 0));

        g.Sort(1, ascending: true, header: true);
        Assert.Equal("Età,9,10,", Column(g, 1)); // numbers first, empty cells at the end

        g.Sort(1, ascending: false, header: true);
        Assert.Equal("Età,,10,9", Column(g, 1));
    }

    [Fact]
    public void InsertDeleteColumn_WidthsAligned()
    {
        var g = Sample();
        g.InsertColumn(1);
        Assert.Equal([100f, 100f, 40f], g.Widths);
        Assert.Equal(3, g.Columns);
        g.DeleteColumn(0);
        Assert.Equal([100f, 40f], g.Widths);
        var paragraphs = g.Paragraphs();
        Assert.All(paragraphs, p => Assert.NotNull(p.Cell));
        Assert.Equal(40f, paragraphs.First(p => p.Cell!.Column == 1).Cell!.Width);
    }

    [Fact]
    public void Readable_OneLinePerParagraphWithTheColumnName()
    {
        var content = new DocumentContent();
        var before = new Paragraph();
        before.Add(new Segment("Prima"));
        content.Paragraphs.Add(before);
        content.Paragraphs.AddRange(Sample().Paragraphs());

        var texts = Grid.Readable(content).Paragraphs.Select(p => p.Text);
        Assert.Equal(["Prima", "Tabella: Nome; Età", "Nome: Zeno; Età: 9", "Nome: anna; Età: 10", "Nome: Bice", "Fine tabella"], texts);
    }
}

public class TableHeaderRowsTests
{
    [Fact]
    public void HeaderRowsOfModel_CountsTheLeadingRows()
    {
        var c = new Eleu.Documents.DocumentContent();
        void Cell(int t, int r, bool header) => c.Paragraphs.Add(new Eleu.Documents.Paragraph { Cell = new Eleu.Documents.CellPosition(t, r, 0, Header: header) });
        Cell(0, 0, true); Cell(0, 1, true); Cell(0, 2, false);
        Cell(1, 0, false); Cell(1, 1, true); // a header not at the top does not count
        Cell(2, 0, true);
        var counts = MainWindow.HeaderRowsOfModel(c);
        Assert.Equal(2, counts[0]);
        Assert.Equal(0, counts[1]);
        Assert.Equal(1, counts[2]);
    }
}
