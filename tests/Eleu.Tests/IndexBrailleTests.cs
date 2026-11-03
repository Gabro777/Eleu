using System.Text;
using Eleu.Braille;
using Eleu.UI;
using static Eleu.Tests.Support;

namespace Eleu.Tests;

/// <summary>Index embossers with their commands, copies, and the typographic characters Word puts in by itself.</summary>
public class IndexBrailleTests
{
    private static IEnumerable<Control> All(Control root) => root.Controls.Cast<Control>().SelectMany(c => All(c).Prepend(c));

    [Theory]
    [InlineData("l’albero", "l'albero")]
    [InlineData("l‘albero", "l'albero")]
    [InlineData("c´era", "c'era")]
    [InlineData("“ciao”", "\"ciao\"")]
    [InlineData("«ciao»", "\"ciao\"")]
    [InlineData("a – b", "a - b")]
    [InlineData("a—b", "a-b")]
    public void Italian_TypographicCharacters_LikeThePlainOnes(string typographic, string plain) =>
        Assert.Equal(BrailleTranslator.Translate(plain, BrailleTranslator.DefaultTable), BrailleTranslator.Translate(typographic, BrailleTranslator.DefaultTable));

    [Fact]
    public void Italian_CurlyApostrophe_NotAnEmptyCell()
    {
        var cells = BrailleTranslator.Translate("l’albero", BrailleTranslator.DefaultTable);
        Assert.DoesNotContain(BrailleTranslator.EmptyCell, cells);
    }

    [Fact]
    public void Header_TheCommandsOfTheIndexEmbossers()
    {
        var format = new PageFormat(38, 27) { DoubleSided = true };
        Assert.Equal("\u001bDBT0,LS50,TD0,PN0,MC2,DP2,BI0,CH38,TM0,LP27;", IndexBraille.Header(format, 2));
        Assert.Contains(",DP1,", IndexBraille.Header(new PageFormat(32, 25), 1));
        Assert.Contains(",MC1,", IndexBraille.Header(new PageFormat(32, 25), 0));
    }

    [Fact]
    public void Job_CommandsTextAndEndOfDocument()
    {
        var brf = Encoding.ASCII.GetBytes("ABC\r\n\f");
        var job = IndexBraille.Job(brf, new PageFormat(38, 27), 1);
        Assert.Equal(0x1B, job[0]);
        Assert.Equal(IndexBraille.EndOfDocument, job[^1]);
        Assert.Contains("ABC", Encoding.ASCII.GetString(job));
    }

    [Theory]
    [InlineData("Index Basic", true)]
    [InlineData("Index Everest-D V5", true)]
    [InlineData("Everest", true)]
    [InlineData("BrailleBox V5", true)]
    [InlineData("ViewPlus Columbia", false)]
    [InlineData("Microsoft Print to PDF", false)]
    [InlineData("Reindexer", false)]
    public void LooksLikeIndex_FromTheName(string printer, bool index) =>
        Assert.Equal(index, IndexBraille.LooksLikeIndex(printer));

    [Fact]
    public void Copies_OneAfterTheOther_EachOnANewSheet()
    {
        List<List<string>> pages = [["⠁"], ["⠃"], ["⠉"]];
        var once = BrfFormat.Copies(pages, 1, doubleSided: false);
        Assert.Equal(BrfFormat.Byte(pages), once);
        var twice = Encoding.ASCII.GetString(BrfFormat.Copies(pages, 2, doubleSided: false));
        Assert.Equal(6, twice.Count(c => c == '\f'));
        // Both sides with 3 pages: a blank back page, so the second copy starts on the front of a sheet.
        var bothSides = Encoding.ASCII.GetString(BrfFormat.Copies(pages, 2, doubleSided: true));
        Assert.Equal(8, bothSides.Count(c => c == '\f'));
    }

    [Fact]
    public void Dialog_IndexGuessedFromTheName_RememberedForEachPrinter_HiddenThroughTheDriver()
    {
        bool indexGuessed = false, otherGeneric = true, hiddenThroughDriver = false;
        int copies = 0;
        IReadOnlyDictionary<string, string> models = new Dictionary<string, string>();
        RunSta(() =>
        {
            using var window = new BrailleDialog(["Index Basic", "Stampante Braille"], "Index Basic", new PageFormat(38, 27), _ => 1,
                "Stampa braille", withFile: false, delivery: new BrailleDelivery([], ["Braille29"], null));
            window.Show();
            indexGuessed = window.IndexChosen;
            var target = All(window).OfType<ComboBox>().Single(c => c.AccessibleName == "Invia a");
            target.SelectedItem = "Stampante Braille";
            otherGeneric = window.IndexChosen;
            // Index chosen for this one too: remembered.
            All(window).OfType<ComboBox>().Single(c => c.AccessibleName == "Modello della stampante").SelectedItem = BrailleDialog.IndexModel;
            models = new Dictionary<string, string>(window.Models);
            All(window).OfType<ComboBox>().Single(c => c.AccessibleName == "Tipo di invio").SelectedIndex = 1;
            hiddenThroughDriver = !All(window).OfType<ComboBox>().Single(c => c.AccessibleName == "Modello della stampante").Visible && !window.IndexChosen;
            All(window).OfType<NumericUpDown>().Single(n => n.AccessibleName == "Copie").Value = 3;
            copies = window.Copies;
        });
        Assert.True(indexGuessed);
        Assert.False(otherGeneric);
        Assert.Equal("Index", models["Stampante Braille"]);
        Assert.True(hiddenThroughDriver);
        Assert.Equal(3, copies);
    }
}
