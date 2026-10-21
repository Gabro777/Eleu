using Eleu.Braille;
using Eleu.UI;
using static Eleu.Tests.Support;

namespace Eleu.Tests;

/// <summary>The Braille tables that can be chosen: languages and kinds of Braille, all translated by liblouis.</summary>
public class BrailleTableTests
{
    public static TheoryData<string> Tables() => [.. BrailleTranslator.Tables.Select(t => t.File)];

    [Theory]
    [MemberData(nameof(Tables))]
    public void EveryTable_TranslatesIntoSixDotCells(string table)
    {
        Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, "liblouis", "tables", table)), $"manca {table}");
        var cells = BrailleTranslator.Translate("Hello, world: 123 è più.", table);
        Assert.NotEmpty(cells);
        Assert.All(cells, c => Assert.InRange(c, (char)0x2800, (char)0x283F));
        Assert.NotEmpty(BrailleTranslator.BackTranslate(cells, table));
    }

    [Fact]
    public void ContractedEnglish_TheIsOneCell()
    {
        // In contracted English Braille "the" is one cell (dots 2-3-4-6); letter by letter in the uncontracted one.
        Assert.Equal("⠮", BrailleTranslator.Translate("the", "en-ueb-g2.ctb"));
        Assert.Equal(3, BrailleTranslator.Translate("the", "en-ueb-g1.ctb").Length);
    }

    [Fact]
    public void BackTranslate_WithTheSameTable()
    {
        var cells = BrailleTranslator.Translate("the house", "en-ueb-g2.ctb");
        Assert.Equal("the house", BrailleTranslator.BackTranslate(cells, "en-ueb-g2.ctb"));
    }

    [Fact]
    public void Current_UnknownTableBecomesItalian()
    {
        var before = BrailleTranslator.Current;
        try
        {
            BrailleTranslator.Current = "does-not-exist.ctb";
            Assert.Equal(BrailleTranslator.DefaultTable, BrailleTranslator.Current);
            BrailleTranslator.Current = "fr-bfu-comp6.utb";
            Assert.Equal("fr-bfu-comp6.utb", BrailleTranslator.Current);
        }
        finally
        {
            BrailleTranslator.Current = before;
        }
    }

    [Fact]
    public void Dialog_TableChosen_PagesCountedWithIt()
    {
        string? counted = null, chosen = null;
        RunSta(() =>
        {
            using var window = new BrailleDialog([], null, new PageFormat(40, 25), format => { counted = format.Table; return 1; },
                "Salva come Braille (BRF)", withFile: true);
            window.Show();
            var combo = window.Controls.Cast<Control>().SelectMany(Descendants).OfType<ComboBox>().Single(c => c.AccessibleName == "Tabella Braille");
            Assert.Equal("Italiano", combo.SelectedItem);
            combo.SelectedItem = "Inglese UEB, contratto (grado 2)";
            chosen = window.Format.Table;
        });
        Assert.Equal("en-ueb-g2.ctb", chosen);
        Assert.Equal("en-ueb-g2.ctb", counted);
    }

    private static IEnumerable<Control> Descendants(Control c) => c.Controls.Cast<Control>().SelectMany(Descendants).Prepend(c);
}
