using System.Drawing.Printing;
using Eleu.Documento;
using Eleu.Editor;
using Eleu.Interfaccia;

namespace Eleu;

// Pagina e stampa: imposta pagina, interruzioni di pagina, navigazione per pagine, stampa e anteprima.
public sealed partial class FinestraPrincipale
{
    private ImpostazioniPagina _pagina = new();

    private void ImpostaPagina()
    {
        using var finestra = new FinestraPagina(_pagina);
        if (finestra.ShowDialog(this) != DialogResult.OK)
            return;
        if (finestra.Predefinito)
        {
            _impostazioni.PaginaPredefinita = finestra.Risultato;
            _impostazioni.Salva();
        }
        if (finestra.Risultato == _pagina)
            return;
        _pagina = finestra.Risultato;
        _testo.Modified = true;
        AggiornaTitolo();
    }

    /// <summary>Ctrl+Invio: ciò che segue il cursore inizia su una pagina nuova.</summary>
    private void InserisciInterruzionePagina()
    {
        int posizione = _testo.SelectionStart;
        if (posizione == 0)
        {
            // Una pagina vuota in testa non serve e confonderebbe la navigazione per pagine.
            _annunciatore.Annuncia("All'inizio del documento non serve un'interruzione di pagina");
            return;
        }
        _testo.Select(posizione, 0); // si inserisce prima della selezione, senza cancellarla
        bool inizioParagrafo = posizione == 0 || _testo.Text[posizione - 1] == '\n';
        _testo.SelectedText = inizioParagrafo ? Paginatore.SaltoPagina.ToString() : "\n" + Paginatore.SaltoPagina;
        _annunciatore.Annuncia("Interruzione di pagina");
    }

    private (int Pagina, int Totale, List<int> Inizi) PaginaCorrente()
    {
        var inizi = Paginatore.InizioPagine(_testo, _pagina);
        return (Paginatore.PaginaDi(inizi, _testo.SelectionStart), inizi.Count, inizi);
    }

    private void VaiAPagina(int pagina, List<int> inizi)
    {
        pagina = Math.Clamp(pagina, 1, inizi.Count);
        int posizione = inizi[pagina - 1];
        // L'interruzione di pagina all'inizio non si legge: si va al primo carattere vero.
        if (posizione < _testo.TextLength && _testo.Text[posizione] == Paginatore.SaltoPagina)
            posizione++;
        _testo.Select(posizione, 0);
        _testo.ScrollToCaret();
        _annunciatore.Annuncia($"Pagina {pagina} di {inizi.Count}. {DescriviRiga(_testo.Text, posizione)}");
    }

    private void PaginaSuccessiva(int passo)
    {
        var (pagina, totale, inizi) = PaginaCorrente();
        if (pagina + passo < 1 || pagina + passo > totale)
        {
            _annunciatore.Annuncia(passo > 0 ? $"Sei già all'ultima pagina, {totale}" : "Sei già alla prima pagina");
            return;
        }
        VaiAPagina(pagina + passo, inizi);
    }

    private void Stampa()
    {
        if (PrinterSettings.InstalledPrinters.Count == 0)
        {
            MessageBox.Show(this, "Non ci sono stampanti installate.", NomeApp, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        var (_, totale, _) = PaginaCorrente();
        var stampanti = PrinterSettings.InstalledPrinters.Cast<string>().ToList();
        using var finestra = new FinestraStampa(stampanti, new PrinterSettings().PrinterName, totale);
        if (finestra.ShowDialog(this) != DialogResult.OK)
            return;

        using var documento = new StampaDocumento(_testo, _pagina, TitoloDocumento);
        documento.PrinterSettings.PrinterName = finestra.Stampante;
        documento.PrinterSettings.PrintRange = PrintRange.SomePages;
        documento.PrinterSettings.FromPage = finestra.DaPagina;
        documento.PrinterSettings.ToPage = finestra.APagina;
        documento.PrinterSettings.Copies = finestra.Copie;
        documento.PrinterSettings.Collate = finestra.Fascicola;
        try
        {
            documento.Print();
            int pagine = finestra.APagina - finestra.DaPagina + 1;
            AnnunciaTraPoco($"Stampa inviata: {(pagine == 1 ? "1 pagina" : $"{pagine} pagine")}");
        }
        catch (Exception e) when (e is InvalidPrinterException or System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(this, $"Impossibile stampare.\n\n{e.Message}", NomeApp, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void AnteprimaDiStampa()
    {
        if (PrinterSettings.InstalledPrinters.Count == 0)
        {
            MessageBox.Show(this, "Per l'anteprima di stampa serve una stampante installata.", NomeApp, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        using var documento = new StampaDocumento(_testo, _pagina, TitoloDocumento);
        using var anteprima = new PrintPreviewDialog { Document = documento, WindowState = FormWindowState.Maximized, UseAntiAlias = true };
        anteprima.ShowDialog(this);
    }
}
