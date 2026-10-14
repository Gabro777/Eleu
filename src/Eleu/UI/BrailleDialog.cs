using Eleu.Braille;

namespace Eleu.UI;

/// <summary>
/// A single window for Braille: where to send it (a BRF file or a Braille printer), the page format
/// (cells, lines, page number, title on top, margin, print pages, table of contents) and which pages.
/// It says right away how many Braille pages there will be, and says it again at every change.
/// </summary>
public sealed class BrailleDialog : Form
{
    public const string FileItem = "File BRF (da salvare)";

    private static readonly string[] NumberPositions = ["Nessuno", "In alto a destra", "In basso a destra"];

    private readonly ComboBox _target, _number;
    private readonly NumericUpDown _cells, _rows, _margin, _from, _to;
    private readonly CheckBox _heading, _printPages, _tableOfContents, _lineSpacing, _doubleSided, _fold;
    private readonly Label _pages;
    private readonly Func<PageFormat, int> _countPages;
    private readonly string? _headingInCells;

    /// <summary>Name of the chosen printer, or null to save a BRF file.</summary>
    public string? Printer => _target.SelectedItem is string choice && choice != FileItem ? choice : null;

    public PageFormat Format => new((int)_cells.Value, (int)_rows.Value)
    {
        Number = (BraillePageNumber)Math.Max(0, _number.SelectedIndex),
        Title = _heading.Checked ? _headingInCells : null,
        LeftMargin = (int)_margin.Value,
        PrintPages = _printPages.Checked,
        TableOfContents = _tableOfContents.Checked,
        DoubleSpacing = _lineSpacing.Checked,
        DoubleSided = _doubleSided.Checked,
        FoldInHalf = _fold.Checked,
    };

    /// <summary>The pages to send, from 1.</summary>
    public (int From, int To) Range => ((int)_from.Value, (int)Math.Max(_from.Value, _to.Value));

    /// <param name="headingInCells">The document title already in Braille, for the line on top of every page.</param>
    public BrailleDialog(IReadOnlyList<string> printers, string? chosenPrinter, PageFormat format, Func<PageFormat, int> countPages,
        string heading = "Braille", bool withFile = true, string? headingInCells = null)
    {
        _countPages = countPages;
        _headingInCells = headingInCells;
        Text = heading;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Font = new Font("Segoe UI", 11f);
        Padding = new Padding(12);

        var grid = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Fill };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 340));

        _target = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, AccessibleName = "Invia a" };
        if (withFile)
            _target.Items.Add(FileItem);
        _target.Items.AddRange(printers.Cast<object>().ToArray());
        int index = chosenPrinter is null ? 0 : _target.Items.IndexOf(chosenPrinter);
        _target.SelectedIndex = Math.Max(0, index);
        AddField(grid, "&Invia a:", _target);

        _cells = Number(format.CellsPerLine, PageFormat.MinCells, PageFormat.MaxCells, "Celle per riga");
        AddField(grid, "&Celle per riga:", _cells);
        _rows = Number(format.LinesPerPage, PageFormat.MinLines, PageFormat.MaxLines, "Righe per pagina");
        AddField(grid, "&Righe per pagina:", _rows);
        _margin = Number(format.LeftMargin, 0, 10, "Margine per la rilegatura, in celle");
        AddField(grid, "&Margine per la rilegatura (celle):", _margin);

        _number = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, AccessibleName = "Numero di pagina" };
        _number.Items.AddRange(NumberPositions);
        _number.SelectedIndex = (int)format.Number;
        AddField(grid, "&Numero di pagina:", _number);

        _heading = CheckBox(grid, "&Titolo del documento in cima a ogni pagina", format.Title is not null && headingInCells is not null);
        _heading.Enabled = headingInCells is not null;
        _printPages = CheckBox(grid, "Segna dove cambia la pagina a &stampa", format.PrintPages);
        _tableOfContents = CheckBox(grid, "S&ommario all'inizio, con le pagine Braille", format.TableOfContents);
        _lineSpacing = CheckBox(grid, "Inter&linea doppia (una riga vuota tra una riga e l'altra)", format.DoubleSpacing);
        _doubleSided = CheckBox(grid, "Stampa &fronte e retro (sul retro margine e numero passano dalla parte giusta)", format.DoubleSided);
        _fold = CheckBox(grid, "Due righe libere a metà pagina, per &piegare il foglio senza schiacciare i punti", format.FoldInHalf);

        _from = Number(1, 1, 9999, "Da pagina");
        AddField(grid, "&Da pagina:", _from);
        _to = Number(9999, 1, 9999, "A pagina");
        AddField(grid, "&A pagina:", _to);

        _pages = new Label { AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
        grid.Controls.Add(_pages);
        grid.SetColumnSpan(_pages, 2);
        foreach (var n in new[] { _cells, _rows, _margin })
            n.ValueChanged += (_, _) => UpdatePages();
        _number.SelectedIndexChanged += (_, _) => UpdatePages();
        foreach (var c in new[] { _heading, _printPages, _tableOfContents, _lineSpacing, _doubleSided, _fold })
            c.CheckedChanged += (_, _) => UpdatePages();
        UpdatePages(firstTime: true);

        var buttons = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 12, 0, 0) };
        var ok = new Button { Text = "OK", AutoSize = true, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Annulla", AutoSize = true, DialogResult = DialogResult.Cancel };
        buttons.Controls.AddRange([ok, cancel]);
        grid.Controls.Add(buttons);
        grid.SetColumnSpan(buttons, 2);

        Controls.Add(grid);
        AcceptButton = ok;
        CancelButton = cancel;
        ActiveControl = _target;
    }

    private void UpdatePages(bool firstTime = false)
    {
        int pages = _countPages(Format);
        _pages.Text = pages == 1 ? "Il documento occupa 1 pagina Braille." : $"Il documento occupa {pages} pagine Braille.";
        // The range follows the page count: if it went up to the last page, it keeps going up to the last page.
        bool upToTheLast = firstTime || _to.Value >= _to.Maximum;
        _from.Maximum = Math.Max(1, pages);
        _to.Maximum = Math.Max(1, pages);
        if (upToTheLast || _to.Value > pages)
            _to.Value = Math.Max(1, pages);
        _pages.AccessibleName = _pages.Text;
    }

    private static NumericUpDown Number(int @value, int min, int max, string name) =>
        new() { Minimum = min, Maximum = max, Value = Math.Clamp(@value, min, max), AccessibleName = name };

    private static CheckBox CheckBox(TableLayoutPanel grid, string text, bool isChecked)
    {
        var checkBox = new CheckBox { Text = text, Checked = isChecked, AutoSize = true };
        grid.Controls.Add(checkBox);
        grid.SetColumnSpan(checkBox, 2);
        return checkBox;
    }

    private static void AddField(TableLayoutPanel grid, string label, Control field)
    {
        // The label precedes the field: Windows uses it as the name and Alt+letter goes to the field.
        grid.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 6) });
        grid.Controls.Add(field);
    }
}
