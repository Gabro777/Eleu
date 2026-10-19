using System.Drawing.Printing;
using Eleu.Braille;
using Eleu.UI;
using UglyToad.PdfPig;
using static Eleu.Tests.Support;

namespace Eleu.Tests;

/// <summary>How the Braille reaches the printer: the BRF file as it is, or through the Windows driver.</summary>
public class BrailleDeliveryTests
{
    private static ComboBox Combo(Control root, string name) =>
        All(root).OfType<ComboBox>().Single(c => c.AccessibleName == name);

    private static IEnumerable<Control> All(Control root) =>
        root.Controls.Cast<Control>().SelectMany(c => All(c).Prepend(c));

    [Fact]
    public void Dialog_RemembersTheChoiceForEachPrinter_HiddenForTheFile()
    {
        bool viewPlusThroughDriver = false, indexThroughDriver = true, fileShowsDelivery = true;
        IReadOnlyCollection<string> afterwards = [];
        RunSta(() =>
        {
            using var window = new BrailleDialog(["ViewPlus Columbia", "Index Everest"], "ViewPlus Columbia", new PageFormat(40, 29), _ => 1,
                "Stampa braille", withFile: true, delivery: new BrailleDelivery(["ViewPlus Columbia"], ["Braille29"], null));
            window.Show();
            viewPlusThroughDriver = window.ThroughDriverChosen;

            var target = Combo(window, "Invia a");
            target.SelectedItem = "Index Everest";
            indexThroughDriver = window.ThroughDriverChosen;

            target.SelectedItem = BrailleDialog.FileItem;
            fileShowsDelivery = Combo(window, "Tipo di invio").Visible;

            // Through the driver for the Index too: remembered for it.
            target.SelectedItem = "Index Everest";
            var delivery = Combo(window, "Tipo di invio");
            delivery.SelectedIndex = 1;
            afterwards = window.DriverPrinters;
        });
        Assert.True(viewPlusThroughDriver);
        Assert.False(indexThroughDriver);
        Assert.False(fileShowsDelivery);
        Assert.Equal(["ViewPlus Columbia", "Index Everest"], afterwards.Order().Reverse());
    }

    [Fact]
    public void ThroughTheDriver_LettersInLowercase_NoDot7InComputerFonts()
    {
        var cells = BrailleTranslator.Translate("Ciao, 12 case.");
        var text = DriverBraillePrinting.LineText(cells);
        Assert.Equal(BrfFormat.Text([[cells]]).Split("\r\n")[0].ToLowerInvariant(), text);
        Assert.DoesNotContain(text, char.IsUpper);
        Assert.Equal("", DriverBraillePrinting.LineText(new string(BrailleTranslator.EmptyCell, 5)));
    }

    [Theory]
    [InlineData("Braille IT Computer", true)]
    [InlineData("Braille29", false)]
    [InlineData("Braille36", false)]
    public void EightDotFont_TheWindowSaysSo(string font, bool warned)
    {
        bool visible = !warned;
        RunSta(() =>
        {
            using var window = new BrailleDialog(["ViewPlus Columbia"], "ViewPlus Columbia", new PageFormat(40, 29), _ => 1,
                "Stampa braille", withFile: false, delivery: new BrailleDelivery(["ViewPlus Columbia"], ["Braille29", "Braille IT Computer"], font));
            window.Show();
            visible = All(window).OfType<Label>().Single(l => l.Text.StartsWith("Questo font sembra a 8 punti")).Visible;
        });
        Assert.Equal(warned, visible);
        Assert.Equal(warned, DriverBraillePrinting.IsEightDot(font));
    }

    [Fact]
    public void Margins_OnTheGridOfDots()
    {
        Assert.Equal(10, DriverBraillePrinting.WholeDots(0.5f));
        Assert.Equal(11, DriverBraillePrinting.WholeDots(0.51f));
        Assert.Equal(0, DriverBraillePrinting.WholeDots(0));
    }

    [Theory]
    // 11.5 inches = 230 dots; 230 - 10 - 10 = 210, rest 0: one dot more.
    [InlineData(11.5f, 10, 10, 11)]
    // 8.5 inches = 170 dots; 150 rest 0 too.
    [InlineData(8.5f, 10, 10, 11)]
    // 11 inches = 220 dots; 200 rest 0; with a front margin of 11 dots: 199, rest 4, no change.
    [InlineData(11f, 11, 10, 10)]
    // rest 3: two dots more.
    [InlineData(11f, 12, 5, 7)]
    public void BackOfTheSheet_CellsBetweenTheFrontOnes(float width, int front, int desired, int expected) =>
        Assert.Equal(expected, DriverBraillePrinting.BackMarginDots(width, front, desired));

    [Fact]
    public void Capacity_LikeTheOtherBrailleProgramsOnElevenInchPaper()
    {
        // Braille29 at 29 points: a line is about 0.4 inches, a cell 0.25 (the size of a Braille cell).
        var (cells, lines) = DriverBraillePrinting.Capacity(11.5f, 11f, 0.25f, 29f / 72);
        Assert.Equal(42, cells);
        Assert.Equal(25, lines);
        // The 29 lines asked for on the tester's sheet do not fit.
        Assert.True(lines < 29);
    }

    [Fact]
    public void ThroughTheDriver_OneSheetPerBraillePage()
    {
        const string pdfPrinter = "Microsoft Print to PDF";
        if (!PrinterSettings.InstalledPrinters.Cast<string>().Contains(pdfPrinter))
            return;
        var file = Path.Combine(Path.GetTempPath(), $"eleu-test-{Guid.NewGuid():N}.pdf");
        try
        {
            var pages = TestPage.Pages(new PageFormat(20, 8) { DoubleSided = true }, BrailleTranslator.Translate);
            RunSta(() => DriverBraillePrinting.Print(pdfPrinter, pages, "Courier New", doubleSided: true, "Eleu - prova", file));
            using var pdf = PdfDocument.Open(file);
            Assert.Equal(pages.Count, pdf.NumberOfPages);
            // The first line is the ruler in ASCII Braille: the digits 1-9, 0 are the letters A-J.
            var first = pdf.GetPage(1);
            Assert.Contains("abcdefghij", first.Text.Replace(" ", ""), StringComparison.OrdinalIgnoreCase);
            // The first line is not on the very edge of the sheet: half an inch from the top and from the left.
            var top = first.Letters.Max(l => l.BoundingBox.Top);
            var left = first.Letters.Min(l => l.BoundingBox.Left);
            Assert.InRange(first.Height - top, 0.5 * 72 - 1, 1.0 * 72);
            Assert.InRange(left, 0.5 * 72 - 1, 0.8 * 72);
        }
        finally
        {
            File.Delete(file);
        }
    }
}
