using System.Drawing.Printing;
using Eleu.Documento;

namespace Eleu.Editor;

/// <summary>
/// Stampa su carta del contenuto del campo di testo, pagina per pagina con il motore di RichEdit,
/// con intestazione, piè di pagina e numeri di pagina. Serve anche per l'anteprima di stampa.
/// </summary>
public sealed class StampaDocumento : PrintDocument
{
    private const float PuntiPerCentesimoDiPollice = 0.72f;

    private readonly RichTextBox _campo;
    private readonly ImpostazioniPagina _pagina;
    private List<int> _inizi = [];
    private int _indice;
    private int _ultima;

    public StampaDocumento(RichTextBox campo, ImpostazioniPagina pagina, string titolo)
    {
        _campo = campo;
        _pagina = pagina;
        DocumentName = titolo;
        var (larghezza, altezza) = pagina.Effettive;
        // Il foglio in centesimi di pollice; i margini li gestisce Eleu.
        DefaultPageSettings.PaperSize = new PaperSize("Eleu", (int)Math.Round(Math.Min(larghezza, altezza) / PuntiPerCentesimoDiPollice),
            (int)Math.Round(Math.Max(larghezza, altezza) / PuntiPerCentesimoDiPollice));
        DefaultPageSettings.Landscape = pagina.Orizzontale;
        DefaultPageSettings.Margins = new Margins(0, 0, 0, 0);
    }

    /// <summary>Numero di pagine che verranno stampate.</summary>
    public int Pagine => _inizi.Count;

    protected override void OnBeginPrint(PrintEventArgs e)
    {
        base.OnBeginPrint(e);
        using var misura = PrinterSettings.CreateMeasurementGraphics(DefaultPageSettings);
        var hdc = misura.GetHdc();
        try
        {
            _inizi = Paginatore.InizioPagine(_campo, _pagina, hdc, MargineFisicoX(), MargineFisicoY());
        }
        finally
        {
            misura.ReleaseHdc(hdc);
        }

        bool alcune = PrinterSettings.PrintRange == PrintRange.SomePages;
        _indice = alcune ? Math.Clamp(PrinterSettings.FromPage, 1, _inizi.Count) - 1 : 0;
        _ultima = alcune ? Math.Clamp(PrinterSettings.ToPage, _indice + 1, _inizi.Count) - 1 : _inizi.Count - 1;
    }

    protected override void OnPrintPage(PrintPageEventArgs e)
    {
        base.OnPrintPage(e);
        int inizio = Paginatore.PrimoDopoIlSalto(_campo.Text, _inizi[_indice]);
        int fine = _indice + 1 < _inizi.Count ? _inizi[_indice + 1] : _campo.TextLength;

        var hdc = e.Graphics!.GetHdc();
        try
        {
            Paginatore.Disegna(_campo, _pagina, hdc, inizio, fine, MargineFisicoX(), MargineFisicoY());
        }
        finally
        {
            e.Graphics.ReleaseHdc(hdc);
        }

        DisegnaBordi(e.Graphics, _indice + 1);
        _indice++;
        e.HasMorePages = _indice <= _ultima;
    }

    protected override void OnEndPrint(PrintEventArgs e)
    {
        Paginatore.LiberaCache(_campo);
        base.OnEndPrint(e);
    }

    /// <summary>Intestazione in alto e piè di pagina con il numero in basso, centrati nei margini.</summary>
    private void DisegnaBordi(Graphics grafica, int numero)
    {
        var (larghezza, altezza) = _pagina.Effettive;
        grafica.PageUnit = GraphicsUnit.Point;
        using var carattere = new Font("Segoe UI", 9f);
        using var centrato = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        float x = _pagina.MargineSinistro - MargineFisicoX();
        float ampiezza = larghezza - _pagina.MargineSinistro - _pagina.MargineDestro;

        // Intestazione e piè di pagina alla loro distanza dal bordo del foglio, con le variabili sostituite.
        var intestazione = _pagina.Componi(_pagina.Intestazione, numero, _inizi.Count, DocumentName, DateTime.Today);
        if (intestazione.Length > 0)
            grafica.DrawString(intestazione, carattere, Brushes.Black,
                new RectangleF(x, _pagina.DistanzaIntestazione - MargineFisicoY(), ampiezza, 20),
                new StringFormat(centrato) { LineAlignment = StringAlignment.Near });

        var piede = _pagina.Componi(_pagina.PiePagina, numero, _inizi.Count, DocumentName, DateTime.Today);
        if (piede.Length > 0)
            grafica.DrawString(piede, carattere, Brushes.Black,
                new RectangleF(x, altezza - _pagina.DistanzaPiede - 20 - MargineFisicoY(), ampiezza, 20),
                new StringFormat(centrato) { LineAlignment = StringAlignment.Far });
    }

    // Margini fisici della stampante (zona non stampabile), in punti: l'origine del disegno parte da lì.
    private float MargineFisicoX() => DefaultPageSettings.HardMarginX * PuntiPerCentesimoDiPollice;

    private float MargineFisicoY() => DefaultPageSettings.HardMarginY * PuntiPerCentesimoDiPollice;
}
