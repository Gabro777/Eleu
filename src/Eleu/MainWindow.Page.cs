using System.Drawing.Printing;
using Eleu.Documents;
using Eleu.Editor;
using Eleu.UI;

namespace Eleu;

// Page and printing: page setup, page breaks, page navigation, printing and preview.
public sealed partial class MainWindow
{
    private PageLayout _page = new();

    private void PageSetup()
    {
        using var window = new PageDialog(_page);
        if (window.ShowDialog(this) != DialogResult.OK)
            return;
        if (window.Default)
        {
            _settings.DefaultPage = window.Result;
            _settings.Save();
        }
        if (window.Result == _page)
            return;
        _page = window.Result;
        _text.Modified = true;
        UpdateTitle();
    }

    /// <summary>Ctrl+Enter: what follows the cursor starts on a new page.</summary>
    private void InsertPageBreak()
    {
        int position = _text.SelectionStart;
        if (position == 0)
        {
            // An empty page at the start is not needed and would confuse page navigation.
            _announcer.Announce("All'inizio del documento non serve un'interruzione di pagina");
            return;
        }
        _text.Select(position, 0); // inserted before the selection, without deleting it
        bool paragraphStart = position == 0 || _text.Text[position - 1] == '\n';
        _text.SelectedText = paragraphStart ? Pagination.PageBreak.ToString() : "\n" + Pagination.PageBreak;
        _announcer.Announce("Interruzione di pagina");
    }

    private (int Page, int Total, List<int> Starts) CurrentPage()
    {
        var starts = Pagination.PageStarts(_text, _page);
        return (Pagination.PageOf(starts, _text.SelectionStart), starts.Count, starts);
    }

    private void GoToPage(int page, List<int> starts)
    {
        page = Math.Clamp(page, 1, starts.Count);
        int position = starts[page - 1];
        // The page break at the start is not read: go to the first real character.
        if (position < _text.TextLength && _text.Text[position] == Pagination.PageBreak)
            position++;
        _text.Select(position, 0);
        _text.ScrollToCaret();
        _announcer.Announce($"Pagina {page} di {starts.Count}. {DescribeLine(_text.Text, position)}");
    }

    private void NextPage(int step)
    {
        var (page, total, starts) = CurrentPage();
        if (page + step < 1 || page + step > total)
        {
            _announcer.Announce(step > 0 ? $"Sei già all'ultima pagina, {total}" : "Sei già alla prima pagina");
            return;
        }
        GoToPage(page + step, starts);
    }

    private void Print()
    {
        if (PrinterSettings.InstalledPrinters.Count == 0)
        {
            MessageBox.Show(this, "Non ci sono stampanti installate.", AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        var (_, total, _) = CurrentPage();
        var printers = PrinterSettings.InstalledPrinters.Cast<string>().ToList();
        using var window = new PrintOptionsDialog(printers, new PrinterSettings().PrinterName, total);
        if (window.ShowDialog(this) != DialogResult.OK)
            return;

        using var documents = new DocumentPrinter(_text, _page, DocumentTitle);
        documents.PrinterSettings.PrinterName = window.Printer;
        documents.PrinterSettings.PrintRange = PrintRange.SomePages;
        documents.PrinterSettings.FromPage = window.FromPage;
        documents.PrinterSettings.ToPage = window.ToPage;
        documents.PrinterSettings.Copies = window.Copies;
        documents.PrinterSettings.Collate = window.Collate;
        try
        {
            documents.Print();
            int pages = window.ToPage - window.FromPage + 1;
            AnnounceShortly($"Stampa inviata: {(pages == 1 ? "1 pagina" : $"{pages} pagine")}");
        }
        catch (Exception e) when (e is InvalidPrinterException or System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(this, $"Impossibile stampare.\n\n{e.Message}", AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void PrintPreview()
    {
        if (PrinterSettings.InstalledPrinters.Count == 0)
        {
            MessageBox.Show(this, "Per l'anteprima di stampa serve una stampante installata.", AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        using var documents = new DocumentPrinter(_text, _page, DocumentTitle);
        using var preview = new PrintPreviewDialog { Document = documents, WindowState = FormWindowState.Maximized, UseAntiAlias = true };
        preview.ShowDialog(this);
    }
}
