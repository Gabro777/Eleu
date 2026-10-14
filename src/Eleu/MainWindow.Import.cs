using Eleu.Accessibility;
using Eleu.Proofing;
using Eleu.Documents;
using Eleu.Import;
using Eleu.UI;

namespace Eleu;

// Opening of PDFs and images (with OCR), scanning, and assisted correction of likely errors.
public sealed partial class MainWindow
{
    private SpellChecker? _speller;
    private bool _spellerLookedUp;
    private LikelyError? _lastError;

    private SpellChecker? Speller
    {
        get
        {
            if (!_spellerLookedUp)
            {
                _speller = SpellChecker.Create("it-IT");
                _spellerLookedUp = true;
            }
            return _speller;
        }
    }

    private void ImportFile(string path)
    {
        var name = Path.GetFileName(path);
        var (result, error, cancelled) = RunWithProgress($"Apertura di {name}",
            (dialog, cancel) => Importer.ImportAsync(path, OcrProgress(dialog), cancel));
        if (cancelled)
        {
            AnnounceShortly("Apertura annullata");
            return;
        }
        if (error is not null || result is null)
        {
            MessageBox.Show(this, $"Impossibile aprire {name}.\n\n{error?.Message}", AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        // It becomes a new document: the PDF or image is never overwritten.
        var proposed = Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path) + ".docx");
        if (ShowImported(result, name, proposed))
            _settings.AddRecent(path);
    }

    /// <summary>
    /// File > Acquisisci da scanner: scans one or more pages (from the glass, asking after each page whether there
    /// is another one, or all of them from the feeder), recognizes the text and opens it as a new document.
    /// </summary>
    private void ScanDocument()
    {
        // Like Open: the scanned text takes the place of this window's document.
        if (!ConfirmDiscardChanges())
            return;
        RememberPosition();
        IScanners scanners = new WindowsScanners();
        var (found, error, cancelled) = RunWithProgress("Ricerca degli scanner", (_, cancel) => scanners.FindAsync(cancel));
        if (cancelled)
            return;
        if (error is not null || found is null || found.Count == 0)
        {
            MessageBox.Show(this,
                "Non ho trovato nessuno scanner.\n\nControlla che sia acceso e collegato al computer e che il suo driver per Windows sia installato."
                + (error is null ? "" : $"\n\n{error.Message}"),
                AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ScannerInfo scanner;
        ScanSource source;
        using (var window = new ScanDialog(found))
        {
            if (window.ShowDialog(this) != DialogResult.OK)
                return;
            scanner = window.Scanner;
            source = window.Source;
        }

        var folder = Path.Combine(Path.GetTempPath(), "Eleu-scansione-" + Guid.NewGuid().ToString("N"));
        try
        {
            var images = ScanPages(scanners, scanner, source, folder);
            if (images.Count == 0)
                return;

            var (result, ocrError, ocrCancelled) = RunWithProgress("Riconoscimento del testo",
                (dialog, cancel) => Importer.RecognizePagesAsync(images, OcrProgress(dialog), cancel));
            if (ocrCancelled)
            {
                AnnounceShortly("Riconoscimento annullato");
                return;
            }
            if (ocrError is not null || result is null)
            {
                MessageBox.Show(this, $"Impossibile riconoscere il testo.\n\n{ocrError?.Message}", AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            var name = $"Scansione del {DateTime.Now:d MMMM yyyy, HH.mm}";
            var proposed = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), name + ".docx");
            ShowImported(result, name, proposed);
        }
        finally
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (IOException)
            {
                // A leftover temporary folder is harmless.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>
    /// File > Foto dal telefono: while the window is open the phone can send photos of pages (QR code or address);
    /// then the text is recognized and opened as a new document, like a scan.
    /// </summary>
    private void PhonePhotos()
    {
        if (!ConfirmDiscardChanges())
            return;
        RememberPosition();
        var folder = Path.Combine(Path.GetTempPath(), "Eleu-telefono-" + Guid.NewGuid().ToString("N"));
        try
        {
            var photos = new List<string>();
            PhoneUpload server;
            try
            {
                server = new PhoneUpload(folder);
            }
            catch (Exception e) when (e is InvalidOperationException or System.Net.Sockets.SocketException)
            {
                MessageBox.Show(this, $"Non posso ricevere foto dal telefono.\n\n{e.Message}", AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            using (server)
            using (var window = new PhoneDialog(server.Url, PhoneUpload.OnPublicNetwork()))
            {
                server.PhotoReceived += file => BeginInvoke(() =>
                {
                    lock (photos)
                        photos.Add(file);
                    window.PhotoArrived();
                    ConfirmationSound(Cue.Saved);
                    _announcer.Announce(window.Photos == 1 ? "Arrivata la prima foto" : $"Arrivata la foto {window.Photos}");
                });
                if (window.ShowDialog(this) != DialogResult.OK)
                    return;
            }
            List<string> images;
            lock (photos)
                images = [.. photos.Order(StringComparer.OrdinalIgnoreCase)];
            if (images.Count == 0)
                return;

            var (result, error, cancelled) = RunWithProgress("Riconoscimento del testo",
                (dialog, cancel) => Importer.RecognizePagesAsync(images, OcrProgress(dialog), cancel));
            if (cancelled)
            {
                AnnounceShortly("Riconoscimento annullato");
                return;
            }
            if (error is not null || result is null)
            {
                MessageBox.Show(this, $"Impossibile riconoscere il testo delle foto.\n\n{error?.Message}", AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            var name = $"Foto del {DateTime.Now:d MMMM yyyy, HH.mm}";
            ShowImported(result, name, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), name + ".docx"));
        }
        finally
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // A leftover temporary folder is harmless.
            }
        }
    }

    /// <summary>The scanned pages, in order; empty if nothing was scanned or the user gave up.</summary>
    private List<string> ScanPages(IScanners scanners, ScannerInfo scanner, ScanSource source, string folder)
    {
        var images = new List<string>();
        while (true)
        {
            var heading = source == ScanSource.Feeder ? "Scansione dei fogli" : $"Scansione della pagina {images.Count + 1}";
            _announcer.Announce(heading);
            var (pages, error, cancelled) = RunWithProgress(heading, (_, cancel) => scanners.ScanAsync(scanner, source, folder, cancel));
            if (cancelled || error is not null || pages is null)
            {
                var what = cancelled ? "Scansione annullata." : $"La scansione non è riuscita.\n\n{error?.Message}";
                if (images.Count == 0)
                {
                    if (cancelled)
                        AnnounceShortly("Scansione annullata");
                    else
                        MessageBox.Show(this, what, AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return images;
                }
                // Pages already scanned are not thrown away without asking.
                var keep = MessageBox.Show(this, $"{what}\n\nVuoi riconoscere il testo delle {Pages(images.Count)} già acquisite?",
                    AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                return keep == DialogResult.Yes ? images : [];
            }
            images.AddRange(pages);

            if (source == ScanSource.Feeder)
            {
                if (images.Count == 0)
                    MessageBox.Show(this, "Nell'alimentatore non c'era nessun foglio.", AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return images;
            }
            var another = MessageBox.Show(this,
                $"Pagina {images.Count} acquisita.\n\nVuoi scansionare un'altra pagina? Metti il foglio seguente sul vetro e premi Sì; premi No per riconoscere il testo.",
                AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (another != DialogResult.Yes)
                return images;
        }

        static string Pages(int n) => n == 1 ? "1 pagina" : $"{n} pagine";
    }

    private Progress<ImportProgress> OcrProgress(ProgressDialog dialog) => new(p =>
    {
        var state = p.TotalPages > 1
            ? $"Riconoscimento del testo, pagina {p.Page} di {p.TotalPages}"
            : "Riconoscimento del testo";
        dialog.Report(state, p.Page - 1, p.TotalPages);
        _announcer.Announce(state);
    });

    /// <summary>
    /// Runs a long job with the progress window (and its Cancel button) open: the result, or the error, or whether it
    /// was cancelled.
    /// </summary>
    private (T? Result, Exception? Error, bool Cancelled) RunWithProgress<T>(string heading, Func<ProgressDialog, CancellationToken, Task<T>> work)
    {
        T? result = default;
        Exception? error = null;
        bool cancelled = false;
        using (var dialog = new ProgressDialog(heading))
        {
            dialog.Shown += async (_, _) =>
            {
                try
                {
                    result = await work(dialog, dialog.Cancellation);
                }
                catch (OperationCanceledException)
                {
                    cancelled = true;
                }
                catch (Exception e) // At the user boundary: the error becomes a message.
                {
                    error = e;
                }
                finally
                {
                    dialog.DialogResult = DialogResult.OK;
                }
            };
            dialog.ShowDialog(this);
        }
        return (result, error, cancelled);
    }

    /// <summary>Recognized text as a new, unsaved document; false if there was no text.</summary>
    private bool ShowImported(ImportedText result, string name, string proposedPath)
    {
        if (result.Paragraphs.Count == 0)
        {
            MessageBox.Show(this, $"Non ho trovato testo in {name}.", AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }

        var content = new DocumentContent();
        foreach (var text in result.Paragraphs)
        {
            var paragraph = new Paragraph();
            paragraph.Add(new Segment(text));
            content.Paragraphs.Add(paragraph);
        }
        LoadContent(content);
        SetCurrentDocument(null, FileFormat.Docx, proposedPath);
        _text.Modified = true;
        UpdateTitle();

        var pages = result.Pages == 1 ? "1 pagina" : $"{result.Pages} pagine";
        var paragraphs = result.Paragraphs.Count == 1 ? "1 paragrafo" : $"{result.Paragraphs.Count} paragrafi";
        AnnounceShortly(result.WithOcr
            ? $"Aperto {name}: {pages}, {paragraphs}. Il testo è stato riconosciuto da un'immagine: con F7 vai agli errori probabili."
            : $"Aperto {name}: {pages}, {paragraphs}.");
        return true;
    }
}
