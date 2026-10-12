using System.Drawing.Printing;
using System.Runtime.InteropServices;

namespace Eleu.Braille;

/// <summary>
/// Invio diretto alla stampante Braille. Le stampanti Braille si installano in Windows di solito
/// come stampante "Solo testo": ricevono il file BRF così com'è (dati RAW), senza impaginazione di Windows.
/// </summary>
public static class StampanteBraille
{
    public static IReadOnlyList<string> Installate() => PrinterSettings.InstalledPrinters.Cast<string>().ToList();

    public static void Stampa(string stampante, byte[] dati, string nomeDocumento)
    {
        if (!OpenPrinter(stampante, out var handle, IntPtr.Zero))
            throw new IOException($"Impossibile aprire la stampante {stampante}.", Marshal.GetLastWin32Error());
        try
        {
            var documento = new DocInfo1 { NomeDocumento = nomeDocumento, TipoDati = "RAW" };
            if (StartDocPrinter(handle, 1, ref documento) == 0)
                throw new IOException($"La stampante {stampante} non accetta il documento.", Marshal.GetLastWin32Error());
            try
            {
                StartPagePrinter(handle);
                if (!WritePrinter(handle, dati, dati.Length, out int scritti) || scritti != dati.Length)
                    throw new IOException($"Invio alla stampante {stampante} non riuscito.", Marshal.GetLastWin32Error());
                EndPagePrinter(handle);
            }
            finally
            {
                EndDocPrinter(handle);
            }
        }
        finally
        {
            ClosePrinter(handle);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DocInfo1
    {
        public string NomeDocumento;
        public string? FileUscita;
        public string TipoDati;
    }

    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "OpenPrinterW")]
    private static extern bool OpenPrinter(string nome, out IntPtr handle, IntPtr predefiniti);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool ClosePrinter(IntPtr handle);

    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "StartDocPrinterW")]
    private static extern int StartDocPrinter(IntPtr handle, int livello, ref DocInfo1 documento);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool EndDocPrinter(IntPtr handle);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool StartPagePrinter(IntPtr handle);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool EndPagePrinter(IntPtr handle);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool WritePrinter(IntPtr handle, byte[] dati, int lunghezza, out int scritti);
}
