using Eleu.Braille;

namespace Eleu.Tests;

/// <summary>Check before Braille: what would come out badly is found, and what is fine is left alone.</summary>
public class BrailleCheckTests
{
    private static List<BrailleIssue> Check(string text, IReadOnlyList<CheckedParagraph>? paragraphs = null,
        IReadOnlyList<CheckedImage>? images = null, IReadOnlyList<CheckedTable>? tables = null) =>
        BrailleCheck.Check(text, paragraphs ?? Paragraphs(text), images ?? [], tables ?? [], 32, BrailleTranslator.Translate);

    /// <summary>The paragraphs of a plain text, one per line, none a heading.</summary>
    private static List<CheckedParagraph> Paragraphs(string text)
    {
        var result = new List<CheckedParagraph>();
        int start = 0;
        foreach (var line in text.Split('\n'))
        {
            result.Add(new CheckedParagraph(start, line, 0, false));
            start += line.Length + 1;
        }
        return result;
    }

    [Fact]
    public void NormalDocument_NoIssues()
    {
        Assert.Empty(Check("Il viaggio\nLa città è bella e ci sono 12 case lungo il fiume.\nPoi una frase breve, città e perché."));
    }

    [Fact]
    public void ImageWithoutDescription_Found_WithDescription_Not()
    {
        var issues = Check("Testo", images: [new(0, ""), new(3, "Un gatto nero")]);
        var issue = Assert.Single(issues);
        Assert.Equal(IssueKind.ImageWithoutDescription, issue.Kind);
        Assert.Equal(0, issue.Position);
        Assert.True(issue.Fixable);
        Assert.False(issue.AutomaticFix);
    }

    [Fact]
    public void EmptyHeading_And_SymbolLines_Found_SymbolsInTableCells_Not()
    {
        const string text = "\n* * * * *\nTesto vero.\n-";
        var issues = Check(text, [new(0, "", 2, false), new(1, "* * * * *", 0, false), new(11, "Testo vero.", 0, false), new(23, "-", 0, true)]);
        Assert.Equal([IssueKind.EmptyHeading, IssueKind.SymbolsOnly], issues.Select(i => i.Kind));
        Assert.Equal([0, 1], issues.Select(i => i.Position));
        Assert.All(issues, i => Assert.True(i.AutomaticFix));
    }

    [Fact]
    public void PageBreakAfterAFewWords_Found_AfterALongText_Not_AtTheEnd_Not()
    {
        var longText = string.Join(" ", Enumerable.Repeat("parola", 20));
        var text = $"Copertina\n\f{longText}\n\fFine\n\f";
        var issue = Assert.Single(Check(text));
        Assert.Equal(IssueKind.NearlyEmptyPage, issue.Kind);
        Assert.Equal(text.IndexOf('\f'), issue.Position);
    }

    [Fact]
    public void TableWhoseFirstRowIsData_Found_WithNamesOrHeader_Not()
    {
        var issues = Check("x", tables:
        [
            new(0, 1, HasHeader: false, ["10", "20,5"]),
            new(0, 2, HasHeader: false, ["Nome", "Età"]),
            new(0, 3, HasHeader: true, ["10", "20"]),
        ]);
        var issue = Assert.Single(issues);
        Assert.Equal(IssueKind.TableWithoutHeader, issue.Kind);
        Assert.Contains("Tabella 1", issue.Description);
    }

    [Fact]
    public void SymbolWithoutBraille_FoundOnce()
    {
        var issues = Check("Ciao 😀 a tutti 😀").Where(i => i.Kind == IssueKind.Untranslatable).ToList();
        var issue = Assert.Single(issues);
        Assert.Equal(5, issue.Position);
    }

    [Fact]
    public void WordLongerThanTheLine_Found()
    {
        var issue = Assert.Single(Check("Visita https://www.esempio.it/una/pagina/molto/lunga/davvero per sapere."));
        Assert.Equal(IssueKind.LongWord, issue.Kind);
        Assert.Equal(7, issue.Position);
        Assert.False(issue.Fixable);
    }
}
