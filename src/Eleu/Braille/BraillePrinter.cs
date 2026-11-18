using System.ComponentModel;
using System.Drawing.Printing;
using System.Runtime.InteropServices;

namespace Eleu.Braille;

/// <summary>
/// Sends directly to the Braille printer. Braille printers are usually installed in Windows
/// as a "Generic / Text Only" printer: they receive the BRF file as it is (RAW data), without Windows pagination.
/// </summary>
public static class BraillePrinter
{
    /// <summary>The printers installed in Windows; none if Windows cannot list them (the print spooler is stopped).</summary>
    public static IReadOnlyList<string> Installed()
    {
        try
        {
            return PrinterSettings.InstalledPrinters.Cast<string>().ToList();
        }
        catch (Win32Exception)
        {
            return [];
        }
    }

    /// <summary>
    /// The errors printing can end with: the printer, its driver or Windows refusing the job, a sheet or a setting the
    /// driver does not accept, a printer removed meanwhile. They become a message, not an unexpected error.
    /// </summary>
    public static bool IsPrintFailure(Exception e) =>
        e is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or NotSupportedException
            or InvalidPrinterException or ExternalException or DllNotFoundException or EntryPointNotFoundException;

    /// <summary>The message for a failed print: what failed and why, in words, with Windows' own reason when there is one.</summary>
    public static string FailureMessage(string what, Exception e) =>
        $"{what}\n\n{e.Message}\n\n" +
        "Controlla che la stampante sia accesa, collegata e senza fogli inceppati, e che in «Tipo di invio» e «Modello della stampante» ci sia la scelta giusta.\n\n" +
        "Se non si risolve, Aiuto, Resoconto per l'assistenza sul desktop mette sul desktop i dettagli dell'errore, da mandare a chi ti aiuta.";

    /// <summary>The message with Windows' reason for the last error of a printing function.</summary>
    private static IOException Failure(string message)
    {
        int code = Marshal.GetLastWin32Error();
        return new IOException(code == 0 ? message : $"{message} {new Win32Exception(code).Message} (codice {code})", code);
    }

    public static void Print(string printer, byte[] data, string documentName)
    {
        if (!OpenPrinter(printer, out var handle, IntPtr.Zero))
            throw Failure($"Impossibile aprire la stampante {printer}.");
        try
        {
            var documents = new DocInfo1 { DocumentName = documentName, DataType = "RAW" };
            if (StartDocPrinter(handle, 1, ref documents) == 0)
                throw Failure($"La stampante {printer} non accetta il documento.");
            try
            {
                if (!StartPagePrinter(handle))
                    throw Failure($"La stampante {printer} non accetta il documento.");
                if (!WritePrinter(handle, data, data.Length, out int written) || written != data.Length)
                    throw Failure($"Invio alla stampante {printer} non riuscito.");
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
        public string DocumentName;
        public string? OutputFile;
        public string DataType;
    }

    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "OpenPrinterW")]
    private static extern bool OpenPrinter(string name, out IntPtr handle, IntPtr defaults);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool ClosePrinter(IntPtr handle);

    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "StartDocPrinterW")]
    private static extern int StartDocPrinter(IntPtr handle, int level, ref DocInfo1 documents);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool EndDocPrinter(IntPtr handle);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool StartPagePrinter(IntPtr handle);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool EndPagePrinter(IntPtr handle);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool WritePrinter(IntPtr handle, byte[] data, int length, out int written);
}
