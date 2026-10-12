using System.Drawing.Printing;
using System.Runtime.InteropServices;

namespace Eleu.Interfaccia;

/// <summary>
/// File > Stampa: stampante, pagine da stampare, copie. Dice subito quante pagine ha il documento.
/// Più semplice della finestra di stampa di Windows: solo ciò che serve.
/// </summary>
public sealed class FinestraStampa : Modulo
{
    private readonly ComboBox _stampante;
    private readonly NumericUpDown _da, _a, _copie;
    private readonly CheckBox _fascicola;

    public FinestraStampa(IReadOnlyList<string> stampanti, string? predefinita, int pagine) : base("Stampa", 260)
    {
        int indice = predefinita is null ? 0 : Math.Max(0, stampanti.ToList().IndexOf(predefinita));
        _stampante = Campo("&Nome della stampante:", Scelta(stampanti, indice));
        var proprieta = RigaIntera(new Button { Text = "P&roprietà della stampante...", AutoSize = true });
        proprieta.Click += (_, _) => ApriProprieta();

        _da = Campo("&Da pagina:", Numero(1, 1, Math.Max(1, pagine), 1, 0));
        _a = Campo("&A pagina:", Numero(pagine, 1, Math.Max(1, pagine), 1, 0));
        _da.Enabled = _a.Enabled = pagine > 1;
        _copie = Campo("Numero di &copie:", Numero(1, 1, 99, 1, 0));
        _fascicola = RigaIntera(new CheckBox { Text = "&Fascicola copie", AutoSize = true, Checked = true, Enabled = false });
        _copie.ValueChanged += (_, _) => _fascicola.Enabled = _copie.Value > 1;

        RigaIntera(new Label
        {
            Text = pagine == 1 ? "Documento di 1 pagina." : $"Documento di {pagine} pagine.",
            AutoSize = true,
            Margin = new Padding(0, 8, 0, 0),
        });

        AggiungiPulsanti();
        ((Button)AcceptButton!).Text = "&Stampa";
        ActiveControl = _stampante;
    }

    public string Stampante => (string)_stampante.SelectedItem!;

    public int DaPagina => (int)Math.Min(_da.Value, _a.Value);

    public int APagina => (int)Math.Max(_da.Value, _a.Value);

    public short Copie => (short)_copie.Value;

    public bool Fascicola => _fascicola.Checked;

    /// <summary>La finestra delle proprietà del driver (carta, qualità, fronte-retro...).</summary>
    private void ApriProprieta()
    {
        if (!OpenPrinter(Stampante, out var stampante, IntPtr.Zero))
            return;
        try
        {
            PrinterProperties(Handle, stampante);
        }
        finally
        {
            ClosePrinter(stampante);
        }
    }

    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "OpenPrinterW")]
    private static extern bool OpenPrinter(string nome, out IntPtr stampante, IntPtr predefiniti);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool ClosePrinter(IntPtr stampante);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool PrinterProperties(IntPtr finestra, IntPtr stampante);
}
