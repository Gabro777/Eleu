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
    public void Paper_ChosenInTheWindow_InMillimetresLikeBrailleBlaster()
    {
        // The tester's BrailleBlaster: 292.1 × 304.8 mm, margins 25.4 mm on the left and 12.7 mm on top.
        SizeF first = default, other = default;
        float left = 0, top = 0;
        bool sizeFieldsHidden = false, sizeFieldsShown = false;
        RunSta(() =>
        {
            using var window = new BrailleDialog(["ViewPlus Columbia"], "ViewPlus Columbia", new PageFormat(40, 27), _ => 1,
                "Stampa braille", withFile: false,
                delivery: new BrailleDelivery(["ViewPlus Columbia"], ["Braille29"], null, new SizeF(11.5f, 12)) { LeftMargin = 1f, TopMargin = 0.5f });
            window.Show();
            first = window.DriverPaper;
            (left, top) = (window.DriverLeftMargin, window.DriverTopMargin);
            var width = All(window).OfType<NumericUpDown>().Single(n => n.AccessibleName == "Larghezza del foglio, in millimetri");
            sizeFieldsHidden = !width.Visible;
            Combo(window, "Foglio").SelectedItem = "Altra misura";
            sizeFieldsShown = width.Visible;
            width.Value = 304.8m;
            other = window.DriverPaper;
            Assert.Equal(25.4m, All(window).OfType<NumericUpDown>().Single(n => n.AccessibleName == "Margine sinistro, in millimetri").Value);
            Assert.Equal(12.7m, All(window).OfType<NumericUpDown>().Single(n => n.AccessibleName == "Margine superiore, in millimetri").Value);
        });
        Assert.Equal(new SizeF(11.5f, 12), first);
        Assert.Equal((1f, 0.5f), (left, top), new InchesComparer());
        Assert.True(sizeFieldsHidden);
        Assert.True(sizeFieldsShown);
        Assert.Equal(12f, other.Width, 0.01f);
        Assert.Equal(12f, other.Height, 0.01f);
    }

    private sealed class InchesComparer : IEqualityComparer<(float, float)>
    {
        public bool Equals((float, float) a, (float, float) b) => Math.Abs(a.Item1 - b.Item1) < 0.01 && Math.Abs(a.Item2 - b.Item2) < 0.01;
        public int GetHashCode((float, float) obj) => 0;
    }

    [Fact]
    public void PaperFor_TheDriversOwnForm_EvenAMadeOne()
    {
        // A form made in the driver for the tractor (custom kind): used as it is, not replaced by a new size.
        var made = new PaperSize("11.5 x 12 in", 1150, 1200);
        Assert.Same(made, DriverBraillePrinting.PaperFor([new PaperSize("Letter", 850, 1100), made], new SizeF(11.5f, 12)));
        var own = DriverBraillePrinting.PaperFor([made], new SizeF(12, 12));
        Assert.Equal("Eleu Braille", own.PaperName);
        // Asked as a user-defined sheet (256), not with code 0, which the ViewPlus driver does not use for the sheet.
        Assert.Equal(DriverBraillePrinting.UserDefinedPaper, own.RawKind);
        Assert.Equal(PaperKind.Custom, own.Kind);
    }

    [Fact]
    public void Capacity_AfterTheLeftAndTopMargins_LikeBrailleBlaster()
    {
        // The tester's sheet in BrailleBlaster: cells of 6.2 mm, lines of 10 mm. 40 cells and 27 lines fit, the
        // rest is the right (18.7 mm) and bottom (22.1 mm) margin BrailleBlaster shows.
        var (cells, lines) = DriverBraillePrinting.Capacity(11.5f, 12f, 6.2f / 25.4f, 10f / 25.4f, left: 1f, top: 0.5f);
        Assert.Equal(43, cells);
        Assert.Equal(29, lines);
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
    // The tester's sheet in BrailleBlaster: 11.5 inches, margin 1 inch (20 dots): 190, rest 0, one dot more.
    [InlineData(11.5f, 20, 20, 21)]
    // rest 3: two dots more.
    [InlineData(11f, 12, 5, 7)]
    public void BackOfTheSheet_CellsBetweenTheFrontOnes(float width, int front, int desired, int expected) =>
        Assert.Equal(expected, DriverBraillePrinting.BackMarginDots(width, front, desired));

    [Fact]
    public void Runs_EachGroupOfCellsWithItsColumn()
    {
        Assert.Equal([(0, "ab"), (5, "c"), (9, "de")], DriverBraillePrinting.Runs("ab   c   de"));
        Assert.Equal([(3, "x")], DriverBraillePrinting.Runs("   x"));
        Assert.Empty(DriverBraillePrinting.Runs("    "));
    }

    [Fact]
    public void CellWidth_NotTheSpaceOfTheFont()
    {
        // In a font whose space is narrower than a full cell, writing the empty cells as spaces would move what follows
        // to the left: the cell width is the one of a full cell.
        using var font = new Font("Times New Roman", 29f, GraphicsUnit.Point);
        using var bitmap = new Bitmap(1, 1);
        using var g = Graphics.FromImage(bitmap);
        g.PageUnit = GraphicsUnit.Inch;
        float tenSpaces = g.MeasureString("=" + new string(' ', 10) + "=", font, PointF.Empty, StringFormat.GenericTypographic).Width
            - g.MeasureString("==", font, PointF.Empty, StringFormat.GenericTypographic).Width;
        Assert.True(tenSpaces < 10 * DriverBraillePrinting.CellWidth(font) * 0.9, "lo spazio di questo font non è più stretto: il test non dimostra niente");
    }

    [Fact]
    public void ThroughTheDriver_EachGroupOfCellsInItsColumn()
    {
        // The second cell after 10 empty ones: 11 cells further, measured on the cells, not on the font's space. (Only a
        // fixed-width font: with the others the PDF printer of Windows writes no readable text to measure.)
        const string pdfPrinter = "Microsoft Print to PDF", font = "Courier New";
        if (!PrinterSettings.InstalledPrinters.Cast<string>().Contains(pdfPrinter))
            return;
        var file = Path.Combine(Path.GetTempPath(), $"eleu-test-{Guid.NewGuid():N}.pdf");
        try
        {
            const char a = (char)0x2801, b = (char)0x2803; // BRF "A" and "B"
            var line = a + new string(BrailleTranslator.EmptyCell, 10) + b;
            RunSta(() => DriverBraillePrinting.Print(pdfPrinter, [[line]], font, new SizeF(8.27f, 11.69f), doubleSided: false, "Eleu - prova", file));
            float cell;
            using (var courier = new Font(font, DriverBraillePrinting.FontSize(font), GraphicsUnit.Point))
                cell = DriverBraillePrinting.CellWidth(courier) * 72;
            using var pdf = PdfDocument.Open(file);
            var letters = pdf.GetPage(1).Letters.Where(l => l.Value.Trim().Length > 0).OrderBy(l => l.StartBaseLine.X).ToList();
            Assert.Equal(2, letters.Count);
            Assert.InRange(letters[1].StartBaseLine.X - letters[0].StartBaseLine.X, 11 * cell - 2, 11 * cell + 2);
        }
        finally
        {
            File.Delete(file);
        }
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
            // The report of what the driver really gave, for the user to send.
            var report = File.ReadAllText(PrintReport.FilePath);
            Assert.Contains("foglio ricevuto dal driver", report);
            Assert.Contains("Pagina 2 (retro)", report);
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
