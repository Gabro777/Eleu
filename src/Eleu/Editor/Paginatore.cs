using System.ComponentModel;
using System.Drawing.Printing;
using System.Runtime.InteropServices;
using Eleu.Documento;

namespace Eleu.Editor;

/// <summary>
/// Divide il documento in pagine come verrà stampato, con il motore di RichEdit (EM_FORMATRANGE):
/// serve per stampare e per dire "pagina 2 di 5". Le interruzioni di pagina (carattere di salto pagina)
/// iniziano sempre una pagina nuova.
/// </summary>
public static class Paginatore
{
    public const char SaltoPagina = '\f';

    private const int EmFormatRange = 0x0400 + 57;

    /// <summary>Posizione nel testo dove inizia ogni pagina (la prima è sempre 0).</summary>
    public static List<int> InizioPagine(RichTextBox campo, ImpostazioniPagina pagina)
    {
        using var grafica = GraficaDiMisura();
        var hdc = grafica.GetHdc();
        try
        {
            return InizioPagine(campo, pagina, hdc);
        }
        finally
        {
            grafica.ReleaseHdc(hdc);
            LiberaCache(campo);
        }
    }

    public static List<int> InizioPagine(RichTextBox campo, ImpostazioniPagina pagina, IntPtr hdc, float spostamentoX = 0, float spostamentoY = 0)
    {
        var testo = campo.Text;
        var inizi = new List<int> { 0 };
        int lunghezza = campo.TextLength;

        // Tratti tra un'interruzione di pagina e la successiva: ognuno inizia su una pagina nuova.
        var tratti = new List<int> { 0 };
        for (int i = testo.IndexOf(SaltoPagina); i >= 0; i = testo.IndexOf(SaltoPagina, i + 1))
            if (i > 0)
                tratti.Add(i);

        for (int t = 0; t < tratti.Count; t++)
        {
            int inizio = tratti[t];
            int fine = t + 1 < tratti.Count ? tratti[t + 1] : lunghezza;
            if (t > 0)
            {
                inizi.Add(inizio);
                // RichEdit tratta già il salto pagina come fine pagina: si misura da dopo, o conterebbe una pagina vuota.
                inizio = PrimoDopoIlSalto(testo, inizio);
            }
            while (inizio < fine)
            {
                var intervallo = Intervallo(campo, pagina, hdc, inizio, fine, spostamentoX, spostamentoY);
                int prossimo = (int)SendMessage(campo.Handle, EmFormatRange, IntPtr.Zero, ref intervallo);
                if (prossimo <= inizio || prossimo >= fine)
                    break;
                inizi.Add(prossimo);
                inizio = prossimo;
            }
        }
        return inizi;
    }

    /// <summary>La posizione dopo l'eventuale carattere di salto pagina con cui inizia una pagina.</summary>
    public static int PrimoDopoIlSalto(string testo, int inizio) =>
        inizio < testo.Length && testo[inizio] == SaltoPagina ? inizio + 1 : inizio;

    /// <summary>Disegna sull'hdc il testo tra le due posizioni, nell'area dentro i margini.</summary>
    public static void Disegna(RichTextBox campo, ImpostazioniPagina pagina, IntPtr hdc, int inizio, int fine, float spostamentoX, float spostamentoY)
    {
        var intervallo = Intervallo(campo, pagina, hdc, inizio, fine, spostamentoX, spostamentoY);
        SendMessage(campo.Handle, EmFormatRange, 1, ref intervallo);
    }

    public static void LiberaCache(RichTextBox campo) => SendMessage(campo.Handle, EmFormatRange, IntPtr.Zero, IntPtr.Zero);

    /// <summary>Pagina (da 1) che contiene la posizione.</summary>
    public static int PaginaDi(IReadOnlyList<int> inizi, int posizione)
    {
        int pagina = 1;
        for (int i = 1; i < inizi.Count && inizi[i] <= posizione; i++)
            pagina = i + 1;
        return pagina;
    }

    /// <summary>La stampante predefinita, se c'è, così la divisione in pagine è quella vera; altrimenti lo schermo.</summary>
    private static Graphics GraficaDiMisura()
    {
        try
        {
            return new PrinterSettings().CreateMeasurementGraphics();
        }
        catch (Exception e) when (e is InvalidPrinterException or Win32Exception)
        {
            return Graphics.FromHwnd(IntPtr.Zero);
        }
    }

    private static FormatRange Intervallo(RichTextBox campo, ImpostazioniPagina pagina, IntPtr hdc, int inizio, int fine,
        float spostamentoX, float spostamentoY)
    {
        var (larghezza, altezza) = pagina.Effettive;
        // RECT in twip; lo spostamento toglie i margini fisici della stampante, da cui parte l'origine del disegno.
        int X(float punti) => RtfScrittore.Twip(punti - spostamentoX);
        int Y(float punti) => RtfScrittore.Twip(punti - spostamentoY);
        return new FormatRange
        {
            hdc = hdc,
            hdcTarget = hdc,
            rc = new Rett(X(pagina.MargineSinistro), Y(pagina.MargineSopra), X(larghezza - pagina.MargineDestro), Y(altezza - pagina.MargineSotto)),
            rcPage = new Rett(0, 0, RtfScrittore.Twip(larghezza), RtfScrittore.Twip(altezza)),
            cpMin = inizio,
            cpMax = fine,
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rett(int left, int top, int right, int bottom)
    {
        public int Left = left, Top = top, Right = right, Bottom = bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FormatRange
    {
        public IntPtr hdc;
        public IntPtr hdcTarget;
        public Rett rc;
        public Rett rcPage;
        public int cpMin;
        public int cpMax;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, ref FormatRange lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, ref FormatRange lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
}
