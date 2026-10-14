using Eleu.Braille;

namespace Eleu.Tests;

public class AdvancedBrailleTests
{
    private const char V = BrailleTranslator.EmptyCell;

    // Fake "translation" for the tests: every character becomes a full cell, so cells can be counted.
    private static string Cells(string text) => new(text.Select(c => c == ' ' ? V : '⠿').ToArray());

    private static string Translate(string text) => "#" + text; // recognizable numbers: "#3"

    private static List<BrailleParagraph> Book(int paragraphs, int wordsEach = 10, bool withHeadings = true)
    {
        var list = new List<BrailleParagraph>();
        for (int i = 0; i < paragraphs; i++)
        {
            if (withHeadings && i % 3 == 0)
                list.Add(new BrailleParagraph(Cells($"Capitolo {i}"), IsHeading: true, Level: 1, PrintPage: i / 2 + 1));
            list.Add(new BrailleParagraph(Cells(string.Join(" ", Enumerable.Repeat("parola", wordsEach))), PrintPage: i / 2 + 1));
        }
        return list;
    }

    [Fact]
    public void NumberAtTop_WithCurrentTitle()
    {
        var format = new PageFormat(20, 10) { Number = BraillePageNumber.TopRight, Title = Cells("Ricette") };
        var pages = Paginator.Paginate(Book(6), format, Translate);
        Assert.True(pages.Count > 1);
        for (int i = 0; i < pages.Count; i++)
        {
            Assert.True(pages[i].Count <= 10);
            Assert.Equal(20, pages[i][0].Length);
            Assert.StartsWith(Cells("Ricette"), pages[i][0]);
            Assert.EndsWith($"#{i + 1}", pages[i][0]);
        }
    }

    [Fact]
    public void NumberAtBottom_AlwaysOnTheLastLine()
    {
        var format = new PageFormat(20, 8) { Number = BraillePageNumber.BottomRight };
        var pages = Paginator.Paginate(Book(5), format, Translate);
        for (int i = 0; i < pages.Count; i++)
        {
            Assert.Equal(8, pages[i].Count);
            Assert.EndsWith($"#{i + 1}", pages[i][^1]);
        }
    }

    [Fact]
    public void Margin_ShiftsTextAndFitsInLine()
    {
        var format = new PageFormat(20, 25) { LeftMargin = 3 };
        var pages = Paginator.Paginate(Book(2, withHeadings: false), format);
        Assert.All(pages.SelectMany(p => p).Where(r => r.Length > 0), r =>
        {
            Assert.StartsWith(new string(V, 3), r);
            Assert.True(r.Length <= 20);
        });
    }

    [Fact]
    public void PrintPages_ALineWithTheNumberWhenItChanges()
    {
        var format = new PageFormat(20, 40) { PrintPages = true };
        var rows = Paginator.Paginate(Book(6, 3, withHeadings: false), format, Translate).SelectMany(p => p).ToList();
        var signs = rows.Where(r => r.StartsWith(Paginator.Hyphen)).ToList();
        Assert.Equal(2, signs.Count); // print pages 1, 2, 3: the changes to 2 and to 3 are marked
        Assert.EndsWith("#2", signs[0]);
        Assert.EndsWith("#3", signs[1]);
        Assert.All(signs, s => Assert.Equal(20, s.Length));
    }

    [Fact]
    public void TableOfContents_OnTopWithTheRightPages()
    {
        var format = new PageFormat(24, 8) { TableOfContents = true, Number = BraillePageNumber.TopRight };
        var pages = Paginator.Paginate(Book(12), format, Translate);
        Assert.Contains("#Sommario", pages[0][1]); // line 0: page number; line 1: "Sommario"

        // For every table of contents entry, the given page starts with (or contains) that heading.
        var items = pages.SelectMany(p => p.Skip(1)).Where(r => r.Contains(Paginator.Dot)).ToList();
        Assert.Equal(4, items.Count);
        for (int v = 0; v < items.Count; v++)
        {
            int number = int.Parse(items[v][(items[v].LastIndexOf('#') + 1)..]);
            Assert.Contains(pages[number - 1].Skip(1), r => r.StartsWith(Cells($"Capitolo {v * 3}")) && !r.Contains(Paginator.Dot));
        }
    }
}

public class BrailleDialogTests
{
    [Fact]
    public void Range_FollowsPagesWhenAllWasSelected()
    {
        (int, int) before = default, after = default;
        Support.RunSta(() =>
        {
            // The table of contents adds a page: 5 without, 6 with.
            using var window = new UI.BrailleDialog([], null, new PageFormat(), f => f.TableOfContents ? 6 : 5, withFile: true);
            before = window.Range;
            var checkBox = All(window).OfType<CheckBox>().First(c => c.Text.Contains("mmario"));
            checkBox.Checked = true;
            after = window.Range;
        });
        Assert.Equal((1, 5), before);
        Assert.Equal((1, 6), after);

        static IEnumerable<Control> All(Control root) => root.Controls.Cast<Control>().SelectMany(c => All(c).Prepend(c));
    }
}
