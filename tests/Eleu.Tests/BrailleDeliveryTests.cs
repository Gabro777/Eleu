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
            Assert.Contains("ABCDEFGHIJ", pdf.GetPage(1).Text.Replace(" ", ""));
        }
        finally
        {
            File.Delete(file);
        }
    }
}
