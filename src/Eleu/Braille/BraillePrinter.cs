using System.Drawing.Printing;
using System.Runtime.InteropServices;

namespace Eleu.Braille;

/// <summary>
/// Sends directly to the Braille printer. Braille printers are usually installed in Windows
/// as a "Generic / Text Only" printer: they receive the BRF file as it is (RAW data), without Windows pagination.
/// </summary>
public static class BraillePrinter
{
    public static IReadOnlyList<string> Installed() => PrinterSettings.InstalledPrinters.Cast<string>().ToList();

    public static void Print(string printer, byte[] data, string documentName)
    {
        if (!OpenPrinter(printer, out var handle, IntPtr.Zero))
            throw new IOException($"Impossibile aprire la stampante {printer}.", Marshal.GetLastWin32Error());
        try
        {
            var documents = new DocInfo1 { DocumentName = documentName, DataType = "RAW" };
            if (StartDocPrinter(handle, 1, ref documents) == 0)
                throw new IOException($"La stampante {printer} non accetta il documento.", Marshal.GetLastWin32Error());
            try
            {
                StartPagePrinter(handle);
                if (!WritePrinter(handle, data, data.Length, out int written) || written != data.Length)
                    throw new IOException($"Invio alla stampante {printer} non riuscito.", Marshal.GetLastWin32Error());
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
