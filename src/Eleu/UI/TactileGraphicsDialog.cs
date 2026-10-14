using System.Drawing.Printing;
using Eleu.Braille;

namespace Eleu.UI;

/// <summary>
/// Inserisci > Immagini > Grafica in rilievo: prepares the image to be touched. You choose the stroke
/// (outlines or filled areas), how dark black must be and the size; the preview shows the Braille cells
/// (it can also be read on the Braille display). Then: Braille printing, BRF file, or the drawing to print on
/// microcapsule paper, with the caption in Braille dots.
/// </summary>
public sealed class TactileGraphicsDialog : DialogBase
{
    private static readonly string[] Strokes = ["Solo i contorni (consigliato)", "Zone scure piene"];
    private static readonly (string Name, int Value)[] Thresholds =
    [
        ("Automatica", 0), ("Altissima: più punti", 230), ("Alta", 200), ("Media", 150), ("Bassa", 100), ("Bassissima: meno punti", 50),
    ];

    private readonly Image _image;
    private readonly int _pageLines;
    private readonly Func<string, string?> _translate;
    private readonly ComboBox _stroke, _threshold, _printer;
    private readonly CheckBox _invert;
    private readonly NumericUpDown _cells, _rows;
    private readonly TextBox _caption, _preview;
    private readonly Label _summary;
    private readonly System.Windows.Forms.Timer _pending = new() { Interval = 300 };
    private List<string> _imageCells = [];

    /// <param name="translate">Translates the caption into Braille; null if Braille is not available.</param>
    public TactileGraphicsDialog(Image image, string description, IReadOnlyList<string> printers, string? chosenPrinter,
        int cells, int pageLines, Func<string, string?> translate) : base("Grafica in rilievo", 560)
    {
        _image = image;
        _pageLines = pageLines;
        _translate = translate;
        _stroke = Field("&Tratto:", ComboBox(Strokes, 0));
        _threshold = Field("S&ensibilità:", ComboBox(Thresholds.Select(s => s.Name), 0));
        _invert = WholeLine(new CheckBox { Text = "&Inverti: in rilievo lo sfondo invece della figura", AutoSize = true });
        _cells = Field("&Larghezza massima (celle):", Number(cells, 10, 80, 1, 0));
        _rows = Field("&Altezza massima (righe):", Number(Math.Max(3, pageLines - 3), 3, 250, 1, 0));
        _caption = Field("&Didascalia in cima:", new TextBox { Text = description });
        _preview = Field("Anteprima &Braille:", new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Height = 320,
            Font = new Font("Segoe UI Symbol", 12f),
        });
        _summary = WholeLine(new Label { AutoSize = true, MaximumSize = new Size(760, 0), Margin = new Padding(0, 4, 0, 4) });
        _printer = Field("Stampa&nte Braille:", ComboBox(printers.Prepend("Nessuna"), Math.Max(0, printers.ToList().IndexOf(chosenPrinter ?? "") + 1)));

        var buttons = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 12, 0, 0) };
        var print = new Button { Text = "Stam&pa Braille", AutoSize = true };
        var saveBrf = new Button { Text = "Salva &file BRF...", AutoSize = true };
        var microcapsule = new Button { Text = "Stampa su carta a &microcapsule...", AutoSize = true };
        var saveDrawing = new Button { Text = "Salva il dise&gno...", AutoSize = true };
        var close = new Button { Text = "&Chiudi", AutoSize = true, DialogResult = DialogResult.Cancel };
        buttons.Controls.AddRange([print, saveBrf, microcapsule, saveDrawing, close]);
        WholeLine(buttons);
        CancelButton = close;

        print.Click += (_, _) => PrintBraille();
        saveBrf.Click += (_, _) => SaveBrf();
        microcapsule.Click += (_, _) => SwellPaperPrint();
        saveDrawing.Click += (_, _) => SaveDrawing();

        _pending.Tick += (_, _) => { _pending.Stop(); Redraw(); };
        _caption.TextChanged += (_, _) => Postpone();
        _stroke.SelectedIndexChanged += (_, _) => Postpone();
        _threshold.SelectedIndexChanged += (_, _) => Postpone();
        _invert.CheckedChanged += (_, _) => Postpone();
        _cells.ValueChanged += (_, _) => Postpone();
        _rows.ValueChanged += (_, _) => Postpone();

        Redraw();
        ActiveControl = _stroke;
    }

    public string? ChosenPrinter => _printer.SelectedIndex > 0 ? (string)_printer.SelectedItem! : null;

    private GraphicsOptions Options => new(
        _stroke.SelectedIndex == 1 ? TactileStroke.Filled : TactileStroke.Outlines,
        Thresholds[Math.Max(0, _threshold.SelectedIndex)].Value,
        _invert.Checked);

    private int Cells => (int)_cells.Value;

    private void Postpone()
    {
        _pending.Stop();
        _pending.Start();
    }

    private void Redraw()
    {
        _imageCells = TactileGraphics.ToCells(_image, Cells, (int)_rows.Value, Options);
        _preview.Text = string.Join(Environment.NewLine, _imageCells);
        int points = _imageCells.Sum(r => r.Sum(c => int.PopCount(c - Braille.BrailleTranslator.EmptyCell)));
        int pages = Pages().Count;
        _summary.Text = $"L'immagine occupa {_imageCells.Count} righe di {Cells} celle, {points} punti in rilievo, " +
                          $"{(pages == 1 ? "1 pagina" : $"{pages} pagine")} Braille.";
        _preview.AccessibleDescription = _summary.Text;
    }

    private string? BrailleCaption() =>
        _caption.Text.Trim() is { Length: > 0 } text ? _translate(text) : null;

    private List<List<string>> Pages() =>
        TactileGraphics.Pages(_imageCells, Cells, _pageLines, BrailleCaption());

    private void PrintBraille()
    {
        if (ChosenPrinter is not string printer)
        {
            MessageBox.Show(this, "Scegli prima la stampante Braille, oppure salva il file BRF.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            _printer.Focus();
            return;
        }
        try
        {
            BraillePrinter.Print(printer, BrfFormat.Byte(Pages()), "Grafica in rilievo");
            MessageBox.Show(this, $"Grafica inviata a {printer}.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (IOException e)
        {
            MessageBox.Show(this, $"{e.Message}\n\nControlla che la stampante sia accesa e collegata.", Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void SaveBrf()
    {
        using var dialog = new SaveFileDialog { Title = "Salva il file Braille", Filter = "File Braille (*.brf)|*.brf", FileName = "Grafica", AddExtension = true, OverwritePrompt = true };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        Write(dialog.FileName, () => File.WriteAllBytes(dialog.FileName, BrfFormat.Byte(Pages())));
    }

    /// <summary>The drawing for an A4 sheet with 1 cm margins: 190 by 277 millimeters.</summary>
    private Bitmap Drawing(float widthMm = 190, float heightMm = 277)
    {
        var caption = BrailleCaption() is string braille
            ? TactileGraphics.WrapWords(braille, (int)(widthMm / TactileGraphics.CellPitch))
            : null;
        return TactileGraphics.ForSwellPaper(_image, widthMm, heightMm, Options, caption);
    }

    private void SaveDrawing()
    {
        using var dialog = new SaveFileDialog { Title = "Salva il disegno da stampare", Filter = "Immagine PNG (*.png)|*.png", FileName = "Grafica in rilievo", AddExtension = true, OverwritePrompt = true };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        Write(dialog.FileName, () =>
        {
            using var drawing = Drawing();
            drawing.Save(dialog.FileName, System.Drawing.Imaging.ImageFormat.Png);
        });
    }

    private void SwellPaperPrint()
    {
        using var documents = new PrintDocument { DocumentName = "Grafica in rilievo" };
        using var dialog = new PrintDialog { Document = documents, UseEXDialog = true, AllowSomePages = false };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        documents.PrintPage += (_, e) =>
        {
            var area = e.MarginBounds; // hundredths of an inch
            using var drawing = Drawing(area.Width * 0.254f, area.Height * 0.254f);
            e.Graphics!.DrawImage(drawing, area);
            e.HasMorePages = false;
        };
        try
        {
            documents.Print();
            MessageBox.Show(this, "Disegno inviato alla stampante. Dopo la stampa, passa il foglio nel fornetto per la carta a microcapsule.",
                Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (InvalidPrinterException e)
        {
            MessageBox.Show(this, e.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void Write(string path, Action write)
    {
        try
        {
            write();
            MessageBox.Show(this, $"Salvato {Path.GetFileName(path)}.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.ExternalException)
        {
            MessageBox.Show(this, $"Impossibile salvare {Path.GetFileName(path)}.\n\n{e.Message}", Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _pending.Dispose();
        base.Dispose(disposing);
    }
}
