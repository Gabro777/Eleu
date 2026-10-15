using Eleu.Documents;

namespace Eleu.Tests;

/// <summary>Summary from bookmarks: the bookmarked paragraphs, once each, in document order, with their formatting.</summary>
public class BookmarkSummaryTests
{
    private static Paragraph P(string text, int heading = 0)
    {
        var p = new Paragraph { HeadingLevel = heading };
        p.Add(new Segment(text, Bold: heading > 0));
        return p;
    }

    [Fact]
    public void BookmarkedParagraphs_InOrder_OnceEach()
    {
        const string text = "La rivoluzione\nPrimo fatto importante.\nUn dettaglio.\nSecondo fatto importante.\n";
        var paragraphs = new List<Paragraph> { P("La rivoluzione", heading: 2), P("Primo fatto importante."), P("Un dettaglio."), P("Secondo fatto importante."), P("") };
        // Two bookmarks in the same paragraph, given out of order.
        int second = text.IndexOf("Secondo", StringComparison.Ordinal), first = text.IndexOf("Primo", StringComparison.Ordinal);
        var summary = MainWindow.SummaryOf(text, paragraphs, [second, first, first + 5, 0], "Storia");

        Assert.Equal(["Riassunto di Storia", "La rivoluzione", "Primo fatto importante.", "Secondo fatto importante."],
            summary.Paragraphs.Select(p => p.Text));
        Assert.Equal(1, summary.Paragraphs[0].HeadingLevel);
        Assert.Equal(2, summary.Paragraphs[1].HeadingLevel); // headings stay headings
    }

    [Fact]
    public void ParagraphInATable_ComesOutOfTheTable()
    {
        var cell = P("Dentro la cella");
        cell.Cell = new CellPosition(0, 0, 0);
        cell.PageBreak = true;
        var summary = MainWindow.SummaryOf("Dentro la cella", [cell], [3], "Prova");
        Assert.Null(summary.Paragraphs[1].Cell);
        Assert.False(summary.Paragraphs[1].PageBreak);
    }
}
