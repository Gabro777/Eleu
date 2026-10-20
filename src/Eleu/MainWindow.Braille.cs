using Eleu.Braille;
using Eleu.Documents;
using Eleu.Editor;
using Eleu.UI;

namespace Eleu;

// Braille: current line in Braille, printing or BRF file, and preview.
public sealed partial class MainWindow
{
    private const float BrailleLineFontSize = 20f;
    private static readonly char[] LineBreakAndRowEnd = ['\n', '\r'];

    private readonly Label _brailleLine;
    private string? _lastTranslatedLine;

    private PageFormat BrailleFormat => new(_settings.BrailleCells, _settings.BrailleLines)
    {
        Number = _settings.BrailleNumber,
        LeftMargin = _settings.BrailleMargin,
        PrintPages = _settings.BraillePrintPages,
        TableOfContents = _settings.BrailleTableOfContents,
        DoubleSpacing = _settings.BrailleDoubleSpacing,
        DoubleSided = _settings.BrailleDoubleSided,
        FoldInHalf = _settings.BrailleFold,
    };

    /// <summary>
    /// Strip at the bottom with the screen line where the cursor is, in Braille cells.
    /// It is a label: it does not take the focus and is not announced on every change,
    /// so screen reader users do not hear the line repeated at every key press.
    /// </summary>
    private Label CreateBrailleLine() => new()
    {
        Dock = DockStyle.Bottom,
        AutoSize = false,
        AutoEllipsis = true,
        UseMnemonic = false,
        TextAlign = ContentAlignment.MiddleLeft,
        Padding = new Padding(8, 0, 8, 0),
        Height = 48,
        Font = new Font("Segoe UI Symbol", BrailleLineFontSize),
        AccessibleName = "Riga in Braille",
        Visible = _settings.BrailleLine,
    };

    private void ToggleBrailleLine()
    {
        _settings.BrailleLine = !_settings.BrailleLine;
        _brailleLine.Visible = _settings.BrailleLine;
        _lastTranslatedLine = null;
        BeginInvoke(UpdateBrailleLine); // once the controls have been laid out
        _announcer.Announce(_settings.BrailleLine ? "Riga in Braille attivata" : "Riga in Braille disattivata");
    }

    private void UpdateBrailleLine()
    {
        if (_brailleLine is not { Visible: true })
            return;

        // The line as it appears on screen (wrapping with the window width).
        int row = _text.GetLineFromCharIndex(_text.SelectionStart);
        int start = _text.GetFirstCharIndexFromLine(row);
        int next = _text.GetFirstCharIndexFromLine(row + 1);
        int end = next < 0 ? _text.TextLength : next;
        var text = start < 0 ? "" : TextStructure.Readable(_text.Text[start..Math.Max(start, end)]).TrimEnd(LineBreakAndRowEnd);
        int cursorInLine = Math.Clamp(_text.SelectionStart - Math.Max(start, 0), 0, text.Length);

        // Recomputed only if the line, the cursor or the available space change.
        var key = $"{cursorInLine}|{_brailleLine.ClientSize.Width}|{text}";
        if (key == _lastTranslatedLine)
            return;
        _lastTranslatedLine = key;
        try
        {
            var cells = BrailleTranslator.Translate(text);
            int visibleCells = VisibleCells();
            if (cells.Length > visibleCells)
            {
                // A Braille cell is wider than a letter: if the line does not fit, the piece
                // around the cursor is shown, as a Braille display does.
                int cellsAtCursor = BrailleTranslator.Translate(text[..cursorInLine]).Length;
                int startAt = Math.Clamp(cellsAtCursor - visibleCells / 3, 0, cells.Length - visibleCells);
                cells = cells.Substring(startAt, visibleCells);
            }
            _brailleLine.Text = cells;
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or InvalidOperationException)
        {
            _brailleLine.Text = "Braille non disponibile";
        }
    }

    private int VisibleCells()
    {
        var sample = new string('⠿', 20);
        int cellWidth = Math.Max(1, TextRenderer.MeasureText(sample, _brailleLine.Font, Size.Empty, TextFormatFlags.NoPadding).Width / 20);
        return Math.Max(1, (_brailleLine.ClientSize.Width - _brailleLine.Padding.Horizontal) / cellWidth - 1);
    }

    /// <summary>The cells follow the zoom, for low-vision users.</summary>
    private void FitBrailleLineToZoom()
    {
        var old = _brailleLine.Font;
        _brailleLine.Font = new Font(old.FontFamily, BrailleLineFontSize * _settings.Zoom);
        _brailleLine.Height = (int)(48 * _settings.Zoom);
        old.Dispose();
        _lastTranslatedLine = null;
        if (IsHandleCreated)
            BeginInvoke(UpdateBrailleLine);
    }

    /// <summary>File > Salva come Braille, BRF (toFile) or File > Stampa braille.</summary>
    private void Braille(bool toFile)
    {
        if (!CheckBeforeBraille(toFile ? "&Salva comunque" : "&Stampa comunque"))
            return;
        if (TranslateToBraille() is not List<BrailleParagraph> paragraphs)
            return;

        IReadOnlyList<string> printers = toFile ? [] : BraillePrinter.Installed();
        if (!toFile && printers.Count == 0)
        {
            MessageBox.Show(this, "Non ci sono stampanti installate.", AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        var heading = BrailleTitle();
        using var window = new BrailleDialog(printers, _settings.BraillePrinter, BrailleFormat with { Title = _settings.BrailleHeading ? heading : null },
            format => Paginate(paragraphs, format).Count,
            toFile ? "Salva come Braille (BRF)" : "Stampa braille", withFile: toFile, headingInCells: heading,
            printTestPage: toFile ? null : PrintBrailleTestPage,
            delivery: toFile ? null : new BrailleDelivery(_settings.BrailleDriverPrinters, DriverBraillePrinting.BrailleFonts(), _settings.BrailleDriverFont,
                new SizeF(_settings.BrailleDriverPaperWidth, _settings.BrailleDriverPaperHeight)));
        if (window.ShowDialog(this) != DialogResult.OK)
            return;

        if (FitToDriverPaper(window, window.Format) is not PageFormat chosen)
            return;
        if (!toFile)
        {
            _settings.BraillePrinter = window.Printer;
            _settings.BrailleDriverPrinters = [.. window.DriverPrinters];
            if (window.DriverFont is string font)
                _settings.BrailleDriverFont = font;
            (_settings.BrailleDriverPaperWidth, _settings.BrailleDriverPaperHeight) = (window.DriverPaper.Width, window.DriverPaper.Height);
        }
        _settings.BrailleCells = chosen.CellsPerLine;
        _settings.BrailleLines = chosen.LinesPerPage;
        _settings.BrailleNumber = chosen.Number;
        _settings.BrailleHeading = chosen.Title is not null;
        _settings.BrailleMargin = chosen.LeftMargin;
        _settings.BraillePrintPages = chosen.PrintPages;
        _settings.BrailleTableOfContents = chosen.TableOfContents;
        _settings.BrailleDoubleSpacing = chosen.DoubleSpacing;
        _settings.BrailleDoubleSided = chosen.DoubleSided;
        _settings.BrailleFold = chosen.FoldInHalf;
        _settings.Save();

        var all = Paginate(paragraphs, chosen);
        var (from, to) = window.Range;
        // Fewer lines per page make more pages: the whole document stays the whole document.
        if (chosen != window.Format && from == 1 && to >= Paginate(paragraphs, window.Format).Count)
            to = all.Count;
        var pages = all.Skip(from - 1).Take(to - from + 1).ToList();
        var data = BrfFormat.Byte(pages);
        var howManyPages = pages.Count == 1 ? "1 pagina" : $"{pages.Count} pagine";

        string done;
        if (window.Printer is string printer)
        {
            try
            {
                SendToBraillePrinter(window, printer, pages, chosen, DocumentName);
                done = $"Inviate {howManyPages} Braille a {printer}";
            }
            catch (Exception e) when (e is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                MessageBox.Show(this, $"{e.Message}\n\nControlla che la stampante sia accesa e collegata.", AppName,
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
        }
        else
        {
            using var dialog = new SaveFileDialog
            {
                Title = "Salva il file Braille",
                Filter = "File Braille (*.brf)|*.brf",
                FileName = Path.GetFileNameWithoutExtension(DocumentName),
                InitialDirectory = InitialFolder(),
                AddExtension = true,
                OverwritePrompt = true,
            };
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;
            try
            {
                File.WriteAllBytes(dialog.FileName, data);
                done = $"Salvato {Path.GetFileName(dialog.FileName)}, {howManyPages} Braille";
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                MessageBox.Show(this, $"Impossibile salvare il file Braille.\n\n{e.Message}", AppName,
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
        }

        if (window.InkCopy && SaveInkCopy(pages, chosen, from, all.Count) is string pdf)
            done += $". Copia in nero salvata: {pdf}";
        AnnounceShortly(done);
    }

    /// <summary>The Braille pages with the letters underneath, as a PDF for who does not read Braille; the file name, or null.</summary>
    private string? SaveInkCopy(List<List<string>> pages, PageFormat format, int firstNumber, int total)
    {
        using var dialog = new SaveFileDialog
        {
            Title = "Salva la copia in nero",
            Filter = "PDF (*.pdf)|*.pdf",
            FileName = $"{Path.GetFileNameWithoutExtension(DocumentName)} - Braille in nero",
            InitialDirectory = InitialFolder(),
            AddExtension = true,
            OverwritePrompt = true,
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return null;
        try
        {
            Cursor = Cursors.WaitCursor;
            var pdf = InkCopy.Pdf(pages, format.CellsPerLine, format.LinesPerPage, BrailleTranslator.BackTranslate,
                DocumentTitle, firstNumber, total);
            File.WriteAllBytes(dialog.FileName, pdf);
            return Path.GetFileName(dialog.FileName);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            MessageBox.Show(this, $"Impossibile salvare la copia in nero.\n\n{e.Message}", AppName,
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return null;
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    /// <summary>
    /// The test page (from the Braille window): numbered cells and lines, to see how many really fit on the sheet,
    /// and a line of symbols to check the printer's Braille table.
    /// </summary>
    private void PrintBrailleTestPage(BrailleDialog window)
    {
        if (window.Printer is not string printer)
            return;
        try
        {
            if (FitToDriverPaper(window, window.Format) is not PageFormat format)
                return;
            SendToBraillePrinter(window, printer, TestPage.Pages(format, BrailleTranslator.Translate), format, "Eleu - pagina di prova");
            _announcer.Announce($"Pagina di prova inviata a {printer}");
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or DllNotFoundException or System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(this, $"Impossibile stampare la pagina di prova.\n\n{e.Message}", AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// Through the driver Eleu itself puts the lines on the sheet: when more cells or lines are asked for than fit on the
    /// sheet chosen in the window, the extra ones would be cut. Says so and offers to use as many as fit; null = the user gives up.
    /// </summary>
    private PageFormat? FitToDriverPaper(BrailleDialog window, PageFormat format)
    {
        if (!window.ThroughDriverChosen || window.DriverFont is not string font)
            return format;
        // A font without the table of BRF files can be chosen, but the dots could come out wrong: asked every time.
        if (!DriverBraillePrinting.HasBrfTable(font) && MessageBox.Show(this,
                $"Il font {font} non usa la tabella dei file Braille: alcuni punti potrebbero uscire sbagliati.\n\n" +
                (DriverBraillePrinting.IsEightDot(font) ? BrailleDialog.EightDotHint : BrailleDialog.OtherTableHint) +
                "\n\nVuoi stampare lo stesso con questo font?",
                AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return null;
        var paper = window.DriverPaper;
        int cells, lines;
        try
        {
            (cells, lines) = DriverBraillePrinting.Capacity(paper, font);
        }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException)
        {
            MessageBox.Show(this, e.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return null;
        }
        if (format.CellsPerLine <= cells && format.LinesPerPage <= lines)
            return format;
        var fitting = format with { CellsPerLine = Math.Min(format.CellsPerLine, cells), LinesPerPage = Math.Min(format.LinesPerPage, lines) };
        var answer = MessageBox.Show(this,
            $"Sul foglio scelto ({paper.Width:0.##} per {paper.Height:0.##} pollici), con il font {font}, " +
            $"ci stanno al massimo {cells} celle per riga e {lines} righe per pagina. Hai chiesto {format.CellsPerLine} celle e " +
            $"{format.LinesPerPage} righe: quelle in più verrebbero tagliate.\n\n" +
            $"Vuoi stampare con {fitting.CellsPerLine} celle e {fitting.LinesPerPage} righe?\n\n" +
            "Se il foglio nella stampante è più grande, scegli la sua misura in «Misura del foglio».",
            AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        return answer == DialogResult.Yes ? fitting : null;
    }

    /// <summary>
    /// The pages to the Braille printer, as chosen in the window: the BRF file as it is (the printer's own settings
    /// apply), or through the Windows driver with its Braille font (one page per sheet, double-sided as chosen here).
    /// </summary>
    private static void SendToBraillePrinter(BrailleDialog window, string printer, List<List<string>> pages, PageFormat format, string name)
    {
        if (!window.ThroughDriverChosen)
        {
            BraillePrinter.Print(printer, BrfFormat.Byte(pages), name);
            return;
        }
        if (window.DriverFont is not string font)
            throw new InvalidOperationException(
                "Non trovo il font Braille del driver della stampante. Il driver di ViewPlus lo installa insieme alla stampante: se manca, reinstalla il driver, oppure scegli l'invio diretto.");
        DriverBraillePrinting.Print(printer, pages, font, window.DriverPaper, format.DoubleSided, name);
    }

    /// <summary>The Braille pages, with numbers and table of contents translated into Braille.</summary>
    private static List<List<string>> Paginate(List<BrailleParagraph> paragraphs, PageFormat format) =>
        Paginator.Paginate(paragraphs, format, BrailleTranslator.Translate);

    private string? BrailleTitle()
    {
        try
        {
            return BrailleTranslator.Translate(DocumentTitle);
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }

    private void BraillePreview()
    {
        if (TranslateToBraille() is not List<BrailleParagraph> paragraphs)
            return;

        var pages = Paginate(paragraphs, BrailleFormat with { Title = _settings.BrailleHeading ? BrailleTitle() : null });
        var rows = new List<string>();
        for (int i = 0; i < pages.Count; i++)
        {
            rows.Add($"--- Pagina {i + 1} di {pages.Count} ---");
            rows.AddRange(pages[i]);
        }
        using var window = new TextDialog("Anteprima Braille", string.Join(Environment.NewLine, rows));
        window.ShowDialog(this);
    }

    /// <summary>
    /// The document translated into Braille, one item per paragraph, with the heading level (for the table of contents) and the
    /// print page where each paragraph starts; null if empty or if the translation fails.
    /// </summary>
    private List<BrailleParagraph>? TranslateToBraille()
    {
        var model = CompleteModel();
        // The print page of each paragraph, from the same pagination as paper printing.
        var starts = TextStructure.Paragraphs(_text.Text);
        var printPages = Pagination.PageStarts(_text, _page);
        for (int i = 0; i < model.Paragraphs.Count && i < starts.Count; i++)
            model.Paragraphs[i].PrintPage = Pagination.PageOf(printPages, starts[i].Start);

        var texts = Readable.From(model).Paragraphs.Where(p => p.Text.Trim().Length > 0).ToList();
        if (texts.Count == 0)
        {
            _announcer.Announce("Il documento è vuoto");
            return null;
        }

        try
        {
            return texts
                .Select(p => new BrailleParagraph(
                    BrailleTranslator.Translate((p.Bulleted ? "- " : "") + p.Text.Replace(Pagination.PageBreak.ToString(), "").Trim()),
                    IsHeading: p.HeadingLevel > 0,
                    NewPage: p.PageBreak,
                    Level: p.HeadingLevel,
                    PrintPage: p.PrintPage))
                .ToList();
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or InvalidOperationException)
        {
            MessageBox.Show(this, $"La traduzione in Braille non è disponibile.\n\n{e.Message}", AppName,
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return null;
        }
    }
}
