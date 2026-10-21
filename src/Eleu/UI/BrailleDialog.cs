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

    private readonly ComboBox _target, _number, _table;
    private readonly NumericUpDown _cells, _rows, _margin, _indent, _from, _to;
    private readonly CheckBox _heading, _printPages, _tableOfContents, _lineSpacing, _doubleSided, _fold, _inkCopy;
    private readonly Label _pages;
    private readonly Func<PageFormat, int> _countPages;
    private readonly string? _headingInCells;
    private readonly Action<BrailleDialog>? _printTestPage;
    private readonly ComboBox _delivery, _font, _paper;
    private readonly NumericUpDown _paperWidth, _paperHeight;
    private readonly Label _deliveryLabel, _fontLabel, _fontHint, _paperLabel, _paperWidthLabel, _paperHeightLabel, _paperFits;
    private readonly HashSet<string> _driverPrinters;
    private readonly bool _deliveryVisible;

    private const string Direct = "Diretto: il file Braille (BRF) va alla stampante";
    private const string ThroughDriver = "Tramite il driver di Windows (ViewPlus e simili)";
    private const string OtherPaper = "Altra misura";

    public const string EightDotHint = "Questo font sembra a 8 punti (\"computer\"): i punti 7 e 8 escono tra una riga e l'altra e la sua tabella non è quella dei file Braille. Scegli Braille29 o Braille36, senza sigla o con «US».";
    public const string OtherTableHint = "Questo font usa la tabella di un'altra lingua o di un altro programma (per esempio «IT» dopo il numero): lettere e numeri escono giusti, ma la punteggiatura e i simboli no. Scegli Braille29 o Braille36, senza sigla dopo il numero oppure con «US» (la tabella dei file Braille).";

    /// <summary>Name of the chosen printer, or null to save a BRF file.</summary>
    public string? Printer => _target.SelectedItem is string choice && choice != FileItem ? choice : null;

    public PageFormat Format => new((int)_cells.Value, (int)_rows.Value)
    {
        Table = BrailleTranslator.Tables[Math.Max(0, _table.SelectedIndex)].File,
        Number = (BraillePageNumber)Math.Max(0, _number.SelectedIndex),
        Title = _heading.Checked ? _headingInCells : null,
        LeftMargin = (int)_margin.Value,
        ParagraphIndent = (int)_indent.Value,
        PrintPages = _printPages.Checked,
        TableOfContents = _tableOfContents.Checked,
        DoubleSpacing = _lineSpacing.Checked,
        DoubleSided = _doubleSided.Checked,
        FoldInHalf = _fold.Checked,
    };

    /// <summary>The pages to send, from 1.</summary>
    public (int From, int To) Range => ((int)_from.Value, (int)Math.Max(_from.Value, _to.Value));

    /// <summary>Also a PDF copy with the letters under every Braille line, for who does not read Braille. Never checked at first.</summary>
    public bool InkCopy => _inkCopy.Checked;

    /// <param name="headingInCells">The document title already in Braille, for the line on top of every page.</param>
    public BrailleDialog(IReadOnlyList<string> printers, string? chosenPrinter, PageFormat format, Func<PageFormat, int> countPages,
        string heading = "Braille", bool withFile = true, string? headingInCells = null, Action<BrailleDialog>? printTestPage = null,
        BrailleDelivery? delivery = null)
    {
        _countPages = countPages;
        _headingInCells = headingInCells;
        _printTestPage = printTestPage;
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

        // How the printer gets the Braille: the BRF file as it is, or through the Windows driver with its Braille font.
        _driverPrinters = [.. delivery?.DriverPrinters ?? []];
        _delivery = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, AccessibleName = "Tipo di invio" };
        _delivery.Items.AddRange([Direct, ThroughDriver]);
        _deliveryLabel = AddField(grid, "Tipo di in&vio:", _delivery);
        _font = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, AccessibleName = "Font Braille del driver" };
        _font.Items.AddRange([.. delivery?.Fonts ?? []]);
        if (_font.Items.Count > 0)
            _font.SelectedItem = DriverBraillePrinting.ProposedFont(delivery?.Fonts ?? [], delivery?.Font);
        _fontLabel = AddField(grid, "Font Braille del driver:", _font);
        _fontHint = Hint(grid);
        _fontHint.Name = "FontHint";

        // Through the driver the sheet is the one chosen here, sent with the job: the driver's own setting does not count.
        _paper = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, AccessibleName = "Foglio" };
        _paper.Items.AddRange([.. DriverBraillePrinting.Papers.Select(p => p.Name), OtherPaper]);
        var paper = delivery?.Paper ?? new SizeF(11.5f, 11f);
        int known = DriverBraillePrinting.Papers.ToList().FindIndex(p => Math.Abs(p.Width - paper.Width) < 0.01 && Math.Abs(p.Height - paper.Height) < 0.01);
        _paper.SelectedIndex = known >= 0 ? known : _paper.Items.Count - 1;
        _paperLabel = AddField(grid, "Mis&ura del foglio:", _paper);
        _paperWidth = Inches(paper.Width, "Larghezza del foglio, in pollici");
        _paperWidthLabel = AddField(grid, "Larg&hezza (pollici):", _paperWidth);
        _paperHeight = Inches(paper.Height, "Altezza del foglio, in pollici");
        _paperHeightLabel = AddField(grid, "Alte&zza (pollici):", _paperHeight);
        _paperFits = Hint(grid);
        _paperFits.Font = Font;

        _font.SelectedIndexChanged += (_, _) => UpdateDelivery(fromPrinter: false);
        _paper.SelectedIndexChanged += (_, _) => UpdateDelivery(fromPrinter: false);
        _paperWidth.ValueChanged += (_, _) => UpdateDelivery(fromPrinter: false);
        _paperHeight.ValueChanged += (_, _) => UpdateDelivery(fromPrinter: false);
        _target.SelectedIndexChanged += (_, _) => UpdateDelivery(fromPrinter: true);
        _delivery.SelectedIndexChanged += (_, _) => UpdateDelivery(fromPrinter: false);
        _deliveryVisible = delivery is not null;
        UpdateDelivery(fromPrinter: true);

        // The language and kind of Braille: it changes the translation, so the page count too.
        _table = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, AccessibleName = "Tabella Braille" };
        _table.Items.AddRange([.. BrailleTranslator.Tables.Select(t => t.Name)]);
        _table.SelectedIndex = Math.Max(0, BrailleTranslator.Tables.ToList().FindIndex(t => t.File == format.Table));
        AddField(grid, "Tabella &Braille:", _table);

        _cells = Number(format.CellsPerLine, PageFormat.MinCells, PageFormat.MaxCells, "Celle per riga");
        AddField(grid, "&Celle per riga:", _cells);
        _rows = Number(format.LinesPerPage, PageFormat.MinLines, PageFormat.MaxLines, "Righe per pagina");
        AddField(grid, "&Righe per pagina:", _rows);
        _margin = Number(format.LeftMargin, 0, 10, "Margine per la rilegatura, in celle");
        AddField(grid, "&Margine per la rilegatura (celle):", _margin);
        _indent = Number(format.ParagraphIndent, 0, PageFormat.MaxParagraphIndent, "Rientro all'inizio dei paragrafi, in celle");
        // No access key: all the letters of the label are already taken in this window.
        AddField(grid, "Rientro all'inizio dei paragrafi (celle):", _indent);

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
        _inkCopy = CheckBox(grid, "Anche una copia in n&ero (PDF), con le lettere sotto ogni riga, per chi non legge il Braille", false);

        _from = Number(1, 1, 9999, "Da pagina");
        AddField(grid, "&Da pagina:", _from);
        _to = Number(9999, 1, 9999, "A pagina");
        AddField(grid, "&A pagina:", _to);

        _pages = new Label { AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
        grid.Controls.Add(_pages);
        grid.SetColumnSpan(_pages, 2);
        foreach (var n in new[] { _cells, _rows, _margin, _indent })
            n.ValueChanged += (_, _) => UpdatePages();
        _number.SelectedIndexChanged += (_, _) => UpdatePages();
        _table.SelectedIndexChanged += (_, _) => UpdatePages();
        foreach (var c in new[] { _heading, _printPages, _tableOfContents, _lineSpacing, _doubleSided, _fold })
            c.CheckedChanged += (_, _) => UpdatePages();
        UpdatePages(firstTime: true);

        var buttons = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 12, 0, 0) };
        var ok = new Button { Text = "OK", AutoSize = true, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Annulla", AutoSize = true, DialogResult = DialogResult.Cancel };
        buttons.Controls.AddRange([ok, cancel]);
        if (printTestPage is not null)
        {
            var test = new Button { Text = "Pa&gina di prova", AutoSize = true };
            test.Click += (_, _) => PrintTestPage();
            buttons.Controls.Add(test);
        }
        grid.Controls.Add(buttons);
        grid.SetColumnSpan(buttons, 2);

        Controls.Add(grid);
        AcceptButton = ok;
        CancelButton = cancel;
        ActiveControl = _target;
    }

    /// <summary>Pagina di prova: sends the test page with the format chosen here to the chosen printer.</summary>
    private void PrintTestPage()
    {
        if (Printer is not string printer)
        {
            MessageBox.Show(this, "La pagina di prova va alla stampante Braille: sceglila in «Invia a».", Text,
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            _target.Focus();
            return;
        }
        _printTestPage?.Invoke(this);
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

    private static NumericUpDown Inches(float @value, string name) =>
        new() { Minimum = 3, Maximum = 20, DecimalPlaces = 2, Increment = 0.25m, Value = Math.Clamp((decimal)@value, 3, 20), AccessibleName = name };

    /// <summary>A line of text across the window, under the field it is about.</summary>
    private Label Hint(TableLayoutPanel grid)
    {
        var hint = new Label { AutoSize = true, MaximumSize = new Size(620, 0), Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, 0, 0, 6) };
        grid.Controls.Add(hint);
        grid.SetColumnSpan(hint, 2);
        return hint;
    }

    private static CheckBox CheckBox(TableLayoutPanel grid, string text, bool isChecked)
    {
        var checkBox = new CheckBox { Text = text, Checked = isChecked, AutoSize = true };
        grid.Controls.Add(checkBox);
        grid.SetColumnSpan(checkBox, 2);
        return checkBox;
    }

    private static Label AddField(TableLayoutPanel grid, string label, Control field)
    {
        // The label precedes the field: Windows uses it as the name and Alt+letter goes to the field.
        var caption = new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 6) };
        grid.Controls.Add(caption);
        grid.Controls.Add(field);
        return caption;
    }

    /// <summary>The printer goes through the Windows driver (with its Braille font) instead of receiving the BRF file.</summary>
    public bool ThroughDriverChosen => Printer is not null && _delivery.SelectedItem as string == ThroughDriver;

    /// <summary>The driver's Braille font chosen, if any.</summary>
    public string? DriverFont => _font.SelectedItem as string;

    /// <summary>The sheet for printing through the driver, in inches.</summary>
    public SizeF DriverPaper => _paper.SelectedIndex >= 0 && _paper.SelectedIndex < DriverBraillePrinting.Papers.Count
        ? DriverBraillePrinting.Papers[_paper.SelectedIndex].Size
        : new SizeF((float)_paperWidth.Value, (float)_paperHeight.Value);

    /// <summary>
    /// The delivery choice is only for printers. Choosing a printer shows how it was used last time; choosing the
    /// delivery remembers it for that printer. The font only matters through the driver.
    /// </summary>
    private void UpdateDelivery(bool fromPrinter)
    {
        bool printer = _deliveryVisible && Printer is not null;
        if (fromPrinter && Printer is string name)
            _delivery.SelectedItem = _driverPrinters.Contains(name) ? ThroughDriver : Direct;
        else if (!fromPrinter && Printer is string chosen)
        {
            if (_delivery.SelectedItem as string == ThroughDriver)
                _driverPrinters.Add(chosen);
            else
                _driverPrinters.Remove(chosen);
        }
        if (_delivery.SelectedIndex < 0)
            _delivery.SelectedItem = Direct;
        _delivery.Visible = _deliveryLabel.Visible = printer;
        bool driver = printer && _delivery.SelectedItem as string == ThroughDriver;
        _font.Visible = _fontLabel.Visible = _paper.Visible = _paperLabel.Visible = driver;
        _paperWidth.Visible = _paperWidthLabel.Visible = _paperHeight.Visible = _paperHeightLabel.Visible =
            driver && _paper.SelectedItem as string == OtherPaper;

        var font = _font.SelectedItem as string;
        _fontHint.Text = font is null || DriverBraillePrinting.HasBrfTable(font) ? ""
            : DriverBraillePrinting.IsEightDot(font) ? EightDotHint : OtherTableHint;
        _fontHint.Visible = driver && _fontHint.Text.Length > 0;

        _paperFits.Text = "";
        if (driver && font is not null)
        {
            try
            {
                var (cells, lines) = DriverBraillePrinting.Capacity(DriverPaper, font);
                _paperFits.Text = $"Su questo foglio ci stanno al massimo {cells} celle per riga e {lines} righe per pagina.";
            }
            catch (InvalidOperationException e)
            {
                _paperFits.Text = e.Message;
            }
        }
        _paperFits.Visible = _paperFits.Text.Length > 0;
        _paperFits.AccessibleName = _paperFits.Text;
    }

    /// <summary>The printers used through the driver, after the choices made here.</summary>
    public IReadOnlyCollection<string> DriverPrinters => _driverPrinters;
}

/// <summary>
/// For the delivery choice: the printers used through the driver, the Braille fonts installed, the font and the sheet
/// (in inches) chosen last time.
/// </summary>
public sealed record BrailleDelivery(IReadOnlyCollection<string> DriverPrinters, IReadOnlyList<string> Fonts, string? Font, SizeF? Paper = null);
