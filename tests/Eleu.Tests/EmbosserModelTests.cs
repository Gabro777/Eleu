using System.ComponentModel;
using System.Drawing.Printing;
using System.Runtime.InteropServices;
using System.Text;
using Eleu.Braille;
using Eleu.UI;
using static Eleu.Tests.Support;

namespace Eleu.Tests;

/// <summary>
/// The models of printers that get the BRF directly: the Juliet 120 and Romeo 60 with the Index commands, the earlier
/// Enabling Technologies embossers with their own, and printing failures that become messages instead of crashes.
/// </summary>
public class EmbosserModelTests
{
    private const char Esc = (char)0x1B;

    private static IEnumerable<Control> All(Control root) => root.Controls.Cast<Control>().SelectMany(c => All(c).Prepend(c));

    [Theory]
    [InlineData("Juliet 120", EmbosserModel.Index)]
    [InlineData("Enabling Technologies Juliet120", EmbosserModel.Index)]
    [InlineData("Romeo 60", EmbosserModel.Index)]
    [InlineData("Index Everest-D V5", EmbosserModel.Index)]
    [InlineData("Juliet Pro60", EmbosserModel.EnablingTechnologies)]
    [InlineData("Juliet Classic", EmbosserModel.EnablingTechnologies)]
    [InlineData("Romeo Attaché Pro", EmbosserModel.EnablingTechnologies)]
    [InlineData("Romeo Pro50", EmbosserModel.EnablingTechnologies)]
    [InlineData("Trident", EmbosserModel.EnablingTechnologies)]
    [InlineData("Thomas Pro", EmbosserModel.EnablingTechnologies)]
    [InlineData("BookMaker", EmbosserModel.EnablingTechnologies)]
    [InlineData("Marathon", EmbosserModel.EnablingTechnologies)]
    [InlineData("ET Braille", EmbosserModel.EnablingTechnologies)]
    [InlineData("Enabling Technologies", EmbosserModel.EnablingTechnologies)]
    // "Juliet" alone can be a Juliet 120 or an earlier one: the user chooses.
    [InlineData("Juliet", EmbosserModel.Generic)]
    [InlineData("Generic / Text Only", EmbosserModel.Generic)]
    [InlineData("Stampante et ufficio", EmbosserModel.Generic)]
    [InlineData("ViewPlus Columbia", EmbosserModel.Generic)]
    public void Guess_FromTheName(string printer, EmbosserModel model) => Assert.Equal(model, EmbosserModels.Guess(printer));

    [Theory]
    [InlineData(EmbosserModel.Generic)]
    [InlineData(EmbosserModel.Index)]
    [InlineData(EmbosserModel.EnablingTechnologies)]
    public void Key_SavedAndReadBack(EmbosserModel model) => Assert.Equal(model, EmbosserModels.FromKey(EmbosserModels.Key(model)));

    [Fact]
    public void Key_TheOnesSavedBefore()
    {
        Assert.Equal(EmbosserModel.Index, EmbosserModels.FromKey("Index"));
        Assert.Equal(EmbosserModel.Generic, EmbosserModels.FromKey("Generica"));
        Assert.Equal(EmbosserModel.Generic, EmbosserModels.FromKey(null));
        Assert.Equal(EmbosserModel.Generic, EmbosserModels.FromKey("qualcosa di sconosciuto"));
    }

    [Fact]
    public void EnablingTechnologies_Header_TableSixDotsSidesCellsLines()
    {
        // 40 cells = "@" + 40 = "h", 25 lines = "@" + 25 = "Y".
        var both = EnablingTechnologiesBraille.Header(new PageFormat(40, 25) { DoubleSided = true });
        Assert.Equal($"{Esc}A@@{Esc}K@{Esc}W@{Esc}i@{Esc}s@{Esc}LA{Esc}Rh{Esc}QY", both);
        var one = EnablingTechnologiesBraille.Header(new PageFormat(32, 27));
        Assert.Contains($"{Esc}iA", one);
        Assert.Contains($"{Esc}R`", one); // "@" + 32
        Assert.Contains($"{Esc}Q[", one); // "@" + 27
    }

    [Theory]
    [InlineData(0, '@')]
    [InlineData(1, 'A')]
    [InlineData(59, '{')]
    [InlineData(60, '{')]
    public void EnablingTechnologies_Number(int value, char expected) => Assert.Equal(expected, EnablingTechnologiesBraille.Number(value));

    [Fact]
    public void Job_EachModelWithItsCommands()
    {
        List<List<string>> pages = [["⠁⠃"], ["⠉"]];
        var format = new PageFormat(38, 27);
        Assert.Equal(BrfFormat.Copies(pages, 2, false), EmbosserModels.Job(EmbosserModel.Generic, pages, format, 2));

        var index = EmbosserModels.Job(EmbosserModel.Index, pages, format, 2);
        Assert.StartsWith($"{Esc}DBT0,", Encoding.ASCII.GetString(index));
        Assert.Contains(",MC2,", Encoding.ASCII.GetString(index));
        Assert.Equal(IndexBraille.EndOfDocument, index[^1]);

        var earlier = Encoding.ASCII.GetString(EmbosserModels.Job(EmbosserModel.EnablingTechnologies, pages, format, 2));
        Assert.StartsWith($"{Esc}A@@", earlier);
        // The copies one after the other: the text twice.
        Assert.Equal(2, earlier.Split("AB\r\n").Length - 1);
    }

    [Fact]
    public void Dialog_Juliet120_IndexCommands_EarlierRemembered_GenericKept()
    {
        EmbosserModel juliet = EmbosserModel.Generic, earlier = EmbosserModel.Generic, kept = EmbosserModel.Index, chosen = EmbosserModel.Generic;
        IReadOnlyDictionary<string, string> models = new Dictionary<string, string>();
        RunSta(() =>
        {
            var remembered = new Dictionary<string, string> { ["Juliet vecchia"] = "Enabling Technologies", ["Juliet ufficio"] = "Generica" };
            using var window = new BrailleDialog(["Juliet 120", "Juliet vecchia", "Juliet ufficio"], "Juliet 120", new PageFormat(38, 27), _ => 1,
                "Stampa braille", withFile: false, delivery: new BrailleDelivery([], [], null) { Models = remembered });
            window.Show();
            juliet = window.ModelChosen;
            var target = All(window).OfType<ComboBox>().Single(c => c.AccessibleName == "Invia a");
            target.SelectedItem = "Juliet vecchia";
            earlier = window.ModelChosen;
            target.SelectedItem = "Juliet ufficio";
            kept = window.ModelChosen;
            // Chosen in the window: remembered for that printer.
            All(window).OfType<ComboBox>().Single(c => c.AccessibleName == "Modello della stampante").SelectedItem = BrailleDialog.IndexModel;
            chosen = window.ModelChosen;
            models = new Dictionary<string, string>(window.Models);
        });
        Assert.Equal(EmbosserModel.Index, juliet);
        Assert.Equal(EmbosserModel.EnablingTechnologies, earlier);
        Assert.Equal(EmbosserModel.Generic, kept);
        Assert.Equal(EmbosserModel.Index, chosen);
        Assert.Equal("Index", models["Juliet ufficio"]);
        Assert.Equal("Index", models["Juliet 120"]);
    }

    public static TheoryData<Exception> PrintFailures => new()
    {
        new IOException("porta"),
        new Win32Exception(5),
        new ExternalException("Errore generico in GDI+."),
        new InvalidPrinterException(new PrinterSettings { PrinterName = "Nessuna" }),
        new ArgumentException("foglio"),
        new InvalidOperationException("font"),
        new UnauthorizedAccessException(),
        new NotSupportedException(),
    };

    [Theory]
    [MemberData(nameof(PrintFailures))]
    public void PrintFailure_AMessageNotACrash(Exception e)
    {
        Assert.True(BraillePrinter.IsPrintFailure(e));
        var message = BraillePrinter.FailureMessage("Impossibile stampare in Braille su Juliet 120.", e);
        Assert.Contains("Juliet 120", message);
        Assert.Contains(e.Message, message);
        Assert.Contains("Resoconto per l'assistenza", message);
    }

    [Fact]
    public void PrintFailure_NotForMistakesInEleu() =>
        // Those go to the handler of unexpected errors, with the report.
        Assert.False(BraillePrinter.IsPrintFailure(new NullReferenceException()));

    [Fact]
    public void Print_ToAPrinterThatIsNotThere_IOExceptionWithTheReason()
    {
        var e = Assert.Throws<IOException>(() => BraillePrinter.Print("Eleu stampante che non esiste", [0x41], "prova"));
        Assert.Contains("Eleu stampante che non esiste", e.Message);
        Assert.True(BraillePrinter.IsPrintFailure(e));
    }

    /// <summary>The page count runs as the window opens, with the choices remembered from last time: it must never fail.</summary>
    [Fact]
    public void Paginate_AnyChoiceOfTheWindow_NeverFails()
    {
        var text = Enumerable.Range(0, 40).Select(i => new BrailleParagraph(
            string.Join(BrailleTranslator.EmptyCell, Enumerable.Repeat(new string((char)0x2801, 1 + i % 70), 3)),
            IsHeading: i % 7 == 0, NewPage: i % 13 == 0, Level: 1 + i % 3, PrintPage: 1 + i / 5)).ToList();
        string Translate(string t) => "⠼" + new string((char)0x2801, t.Length);
        foreach (int cells in new[] { PageFormat.MinCells, 11, 13, 40, PageFormat.MaxCells })
        foreach (int lines in new[] { PageFormat.MinLines, 6, 27, PageFormat.MaxLines })
        foreach (int margin in new[] { 0, 3, 9, 10 })
        foreach (int indent in new[] { 0, PageFormat.MaxParagraphIndent })
        foreach (var number in Enum.GetValues<BraillePageNumber>())
        for (int flags = 0; flags < 32; flags++)
        {
            var format = new PageFormat(cells, lines)
            {
                LeftMargin = margin,
                ParagraphIndent = indent,
                Number = number,
                Title = (flags & 1) != 0 ? new string((char)0x2803, 80) : null,
                TableOfContents = (flags & 2) != 0,
                DoubleSpacing = (flags & 4) != 0,
                DoubleSided = (flags & 8) != 0,
                FoldInHalf = (flags & 16) != 0,
                PrintPages = (flags & 3) == 3,
            };
            var pages = Paginator.Paginate(text, format, Translate);
            Assert.NotEmpty(pages);
        }
    }
}
