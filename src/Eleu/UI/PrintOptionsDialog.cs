using System.Drawing.Printing;
using System.Runtime.InteropServices;

namespace Eleu.UI;

/// <summary>
/// File > Stampa: printer, pages to print, copies. It says right away how many pages the document has.
/// Simpler than the Windows print dialog: only what is needed.
/// </summary>
public sealed class PrintOptionsDialog : DialogBase
{
    private readonly ComboBox _printer;
    private readonly NumericUpDown _from, _to, _copies;
    private readonly CheckBox _collate;

    public PrintOptionsDialog(IReadOnlyList<string> printers, string? defaultValue, int pages) : base("Stampa", 260)
    {
        int index = defaultValue is null ? 0 : Math.Max(0, printers.ToList().IndexOf(defaultValue));
        _printer = Field("&Nome della stampante:", ComboBox(printers, index));
        var properties = WholeLine(new Button { Text = "P&roprietà della stampante...", AutoSize = true });
        properties.Click += (_, _) => OpenProperties();

        _from = Field("&Da pagina:", Number(1, 1, Math.Max(1, pages), 1, 0));
        _to = Field("&A pagina:", Number(pages, 1, Math.Max(1, pages), 1, 0));
        _from.Enabled = _to.Enabled = pages > 1;
        _copies = Field("Numero di &copie:", Number(1, 1, 99, 1, 0));
        _collate = WholeLine(new CheckBox { Text = "&Fascicola copie", AutoSize = true, Checked = true, Enabled = false });
        _copies.ValueChanged += (_, _) => _collate.Enabled = _copies.Value > 1;

        WholeLine(new Label
        {
            Text = pages == 1 ? "Documento di 1 pagina." : $"Documento di {pages} pagine.",
            AutoSize = true,
            Margin = new Padding(0, 8, 0, 0),
        });

        AddButtons();
        ((Button)AcceptButton!).Text = "&Stampa";
        ActiveControl = _printer;
    }

    public string Printer => (string)_printer.SelectedItem!;

    public int FromPage => (int)Math.Min(_from.Value, _to.Value);

    public int ToPage => (int)Math.Max(_from.Value, _to.Value);

    public short Copies => (short)_copies.Value;

    public bool Collate => _collate.Checked;

    /// <summary>The driver's properties window (paper, quality, double-sided...).</summary>
    private void OpenProperties()
    {
        if (!OpenPrinter(Printer, out var printer, IntPtr.Zero))
            return;
        try
        {
            PrinterProperties(Handle, printer);
        }
        finally
        {
            ClosePrinter(printer);
        }
    }

    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "OpenPrinterW")]
    private static extern bool OpenPrinter(string name, out IntPtr printer, IntPtr defaults);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool ClosePrinter(IntPtr printer);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool PrinterProperties(IntPtr window, IntPtr printer);
}
