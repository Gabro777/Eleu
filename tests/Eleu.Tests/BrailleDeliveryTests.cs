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
    [InlineData("Braille IT Computer", BrailleDialog.EightDotHint)]
    [InlineData("Braille36 IT", BrailleDialog.OtherTableHint)]
    [InlineData("Braille29", "")]
    [InlineData("Braille36", "")]
    public void FontWithoutTheBrfTable_TheWindowSaysSo(string font, string hint)
    {
        string shown = "?";
        bool visible = false;
        RunSta(() =>
        {
            using var window = new BrailleDialog(["ViewPlus Columbia"], "ViewPlus Columbia", new PageFormat(40, 29), _ => 1,
                "Stampa braille", withFile: false,
                delivery: new BrailleDelivery(["ViewPlus Columbia"], ["Braille29", "Braille36", "Braille36 IT", "Braille IT Computer"], null));
            window.Show();
            Combo(window, "Font Braille del driver").SelectedItem = font;
            var label = All(window).OfType<Label>().Single(l => l.Name == "FontHint");
            (shown, visible) = (label.Text, label.Visible);
        });
        Assert.Equal(hint, shown);
        Assert.Equal(hint.Length > 0, visible);
        Assert.Equal(hint.Length == 0, DriverBraillePrinting.HasBrfTable(font));
    }

    [Theory]
    [InlineData("Braille29", true)]
    [InlineData("Braille36 US", true)]
    [InlineData("Braille29 US", true)]
    [InlineData("Braille36 IT", false)]
    [InlineData("Braille US Computer", false)]
    [InlineData("Braille US Computer ASCII", false)]
    [InlineData("Tiger Euro", false)]
    public void BrfTable_TheFontsOfTheViewPlusDriver(string font, bool brf) =>
        Assert.Equal(brf, DriverBraillePrinting.HasBrfTable(font));

    [Fact]
    public void FontList_AllOfThem_TheRightOnesFirst()
    {
        string[] tester = ["Braille IT Computer", "Braille36 IT", "Braille36 US", "Braille US Computer ASCII", "Tiger"];
        Assert.Equal(["Braille36 US", "Braille36 IT", "Braille IT Computer", "Braille US Computer ASCII", "Tiger"], DriverBraillePrinting.UsableFonts(tester));
        Assert.Equal(["Braille29", "Braille36", "Braille IT Computer"], DriverBraillePrinting.UsableFonts(["Braille36", "Braille IT Computer", "Braille29"]));
    }

    [Fact]
    public void ProposedFont_TheTestersList()
    {
        // The fonts on the tester's PC: the US one is chosen, not the Italian one used last time.
        string[] fonts = ["Braille36 IT", "Braille36 US", "Braille IT Computer", "Braille US Computer ASCII", "Tiger"];
        Assert.Equal("Braille36 US", DriverBraillePrinting.ProposedFont(fonts, "Braille36 IT"));
    }

    [Fact]
    public void ProposedFont_NotTheOneWithAnotherTable()
    {
        string[] fonts = ["Braille29", "Braille36", "Braille36 IT"];
        // Chosen by mistake last time: the BRF one instead.
        Assert.Equal("Braille29", DriverBraillePrinting.ProposedFont(fonts, "Braille36 IT"));
        Assert.Equal("Braille36", DriverBraillePrinting.ProposedFont(fonts, "Braille36"));
        Assert.Equal("Braille29", DriverBraillePrinting.ProposedFont(fonts, null));
        // Only variants installed: the one chosen stays (the window warns).
        Assert.Equal("Braille36 IT", DriverBraillePrinting.ProposedFont(["Braille29 IT", "Braille36 IT"], "Braille36 IT"));
        Assert.Null(DriverBraillePrinting.ProposedFont([], "Braille29"));
    }

    [Fact]
    public void Paper_ChosenInTheWindow_RememberedAndOtherSize()
    {
        SizeF first = default, other = default;
        bool sizeFieldsHidden = false, sizeFieldsShown = false;
        RunSta(() =>
        {
            using var window = new BrailleDialog(["ViewPlus Columbia"], "ViewPlus Columbia", new PageFormat(40, 25), _ => 1,
                "Stampa braille", withFile: false, delivery: new BrailleDelivery(["ViewPlus Columbia"], ["Braille29"], null, new SizeF(12, 12)));
            window.Show();
            first = window.DriverPaper;
            var width = All(window).OfType<NumericUpDown>().Single(n => n.AccessibleName == "Larghezza del foglio, in pollici");
            sizeFieldsHidden = !width.Visible;
            Combo(window, "Foglio").SelectedItem = "Altra misura";
            sizeFieldsShown = width.Visible;
            width.Value = 13.5m;
            other = window.DriverPaper;
        });
        Assert.Equal(new SizeF(12, 12), first);
        Assert.True(sizeFieldsHidden);
        Assert.True(sizeFieldsShown);
        Assert.Equal(new SizeF(13.5f, 12), other);
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
            RunSta(() => DriverBraillePrinting.Print(pdfPrinter, pages, "Courier New", new SizeF(8.27f, 11.69f), doubleSided: true, "Eleu - prova", file));
            using var pdf = PdfDocument.Open(file);
            Assert.Equal(pages.Count, pdf.NumberOfPages);
            // The sheet is the one Eleu asks for (A4 here), not the one set in the printer.
            Assert.InRange(pdf.GetPage(1).Width, 590, 600);
            Assert.InRange(pdf.GetPage(1).Height, 837, 847);
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
