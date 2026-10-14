using Eleu.Accessibility;
using System.Text;
using Eleu.Documents;
using Eleu.Import;

namespace Eleu;

// New, open, save, recent files and recovery copy.
public sealed partial class MainWindow
{
    private string? _path;
    private FileFormat _format = FileFormat.Docx;
    private Encoding _textEncoding = TextFile.Utf8WithoutBom;

    // Path proposed for the first save when the document must not be saved over the file it comes from.
    private string? _suggestedPath;

    private string DocumentName => (_path ?? _suggestedPath) is string p ? Path.GetFileName(p) : "Senza titolo";

    /// <summary>The file of this window's document (null if it has not been saved yet).</summary>
    public string? DocumentPath => _path;

    /// <summary>A window just opened and never touched: a document can be opened in it without opening another one.</summary>
    private bool DocumentEmpty => _text.TextLength == 0 && !_text.Modified && _path is null && _suggestedPath is null;

    /// <summary>A new document in place of the open one, in this window (asking first whether to save).</summary>
    private void New()
    {
        if (!ConfirmDiscardChanges())
            return;
        RememberPosition();
        LoadNewDocument();
        SetCurrentDocument(null, FileFormat.Docx);
        _announcer.Announce("Nuovo documento");
    }

    /// <summary>Opens a file in place of this window's document (asking first whether to save).</summary>
    private void Open() => ChooseAndOpen(newWindow: false);

    /// <summary>File > Apri in un'altra finestra: the open document stays where it is.</summary>
    private void OpenInNewWindow() => ChooseAndOpen(newWindow: true);

    private void ChooseAndOpen(bool newWindow)
    {
        if (!newWindow && !ConfirmDiscardChanges())
            return;
        using var dialog = new OpenFileDialog
        {
            Title = newWindow ? "Apri in un'altra finestra" : "Apri",
            Filter = FileFormats.OpenFilter,
            InitialDirectory = InitialFolder(),
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            OpenInWindow(dialog.FileName, newWindow);
    }

    /// <summary>
    /// Opens the file here or in a new window. If the file is already open in another window it goes there,
    /// instead of having it twice.
    /// </summary>
    private void OpenInWindow(string path, bool newWindow = false)
    {
        var alreadyOpen = EleuContext.Windows.FirstOrDefault(f => f != this && string.Equals(f.DocumentPath, path, StringComparison.OrdinalIgnoreCase));
        if (alreadyOpen is not null)
        {
            alreadyOpen.ActivateAndAnnounce($"{Path.GetFileName(path)} era già aperto");
            return;
        }
        if (newWindow)
            EleuContext.Open(path);
        else
            OpenFile(path);
    }

    private void OpenFile(string path)
    {
        RememberPosition();
        if (Importer.IsImportable(path))
        {
            ImportFile(path);
            return;
        }

        var format = FileFormats.FromPath(path);
        var encoding = TextFile.Utf8WithoutBom;
        List<string> notPreserved = [];
        try
        {
            switch (format)
            {
                case FileFormat.Docx:
                    using (var stream = File.OpenRead(path))
                    {
                        var content = DocxFormat.Read(stream);
                        notPreserved = content.NotPreserved;
                        LoadContent(content);
                    }
                    break;
                case FileFormat.Odt:
                    using (var stream = File.OpenRead(path))
                    {
                        var content = OdtFormat.Read(stream);
                        notPreserved = content.NotPreserved;
                        LoadContent(content);
                    }
                    break;
                case FileFormat.Rtf:
                    var rtf = File.ReadAllText(path, Encoding.Latin1);
                    LoadRtf(rtf);
                    _page = RtfReader.ReadPage(rtf);
                    _metadata = Metadata.FromRtf(rtf);
                    AfterLoad();
                    break;
                case FileFormat.Html or FileFormat.Epub:
                    // From a web page or a book Eleu takes the text and structure, not the look:
                    // saving over the original would lose it, so the first save asks for a new name.
                    DocumentContent read;
                    if (format == FileFormat.Epub)
                    {
                        using var book = File.OpenRead(path);
                        read = EpubFormat.Read(book);
                    }
                    else
                    {
                        var pageFolder = Path.GetDirectoryName(path)!;
                        read = HtmlFormat.Read(TextFile.Read(File.ReadAllBytes(path)).Text, src =>
                        {
                            // The images next to the page (not those on the internet).
                            if (src.Contains("://"))
                                return null;
                            var file = Path.GetFullPath(Path.Combine(pageFolder, src.Split('#', '?')[0].Replace('/', Path.DirectorySeparatorChar)));
                            return File.Exists(file) ? File.ReadAllBytes(file) : null;
                        });
                    }
                    notPreserved = ["l'aspetto originale (stili e impaginazione)"];
                    LoadContent(read);
                    break;
                default:
                    var (text, readEncoding) = TextFile.Read(File.ReadAllBytes(path));
                    encoding = readEncoding;
                    LoadContent(DocumentContent.FromPlainText(text));
                    break;
            }
        }
        catch (Exception e) // At the user boundary: any unreadable file becomes a message, not a crash.
        {
            MessageBox.Show(this, $"Impossibile aprire {Path.GetFileName(path)}.\n\n{e.Message}", AppName,
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        _textEncoding = encoding;
        if (notPreserved.Count == 0)
        {
            SetCurrentDocument(path, format);
            AnnounceShortly($"Aperto {DocumentName}{ResumeWhereYouLeft(path)}");
            return;
        }

        // Saving over the original would lose what Eleu does not keep: the first save will ask for a new name.
        var copy = Path.Combine(Path.GetDirectoryName(path)!, $"{Path.GetFileNameWithoutExtension(path)} (Eleu){(format == FileFormat.Odt ? ".odt" : ".docx")}");
        SetCurrentDocument(null, format, copy);
        _settings.AddRecent(path);
        MessageBox.Show(this,
            $"{Path.GetFileName(path)} contiene {string.Join(", ", notPreserved)}, che Eleu non può conservare." + Environment.NewLine + Environment.NewLine +
            "Per non perderli, Eleu non salverà sopra il file originale: quando salvi ti chiederà un nuovo nome.",
            AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private bool Save() => _path is null ? SaveAs() : SaveTo(_path, _format);

    private bool SaveAs()
    {
        using var dialog = new SaveFileDialog
        {
            Title = "Salva con nome",
            Filter = FileFormats.SaveFilter,
            FilterIndex = (int)_format + 1,
            FileName = (_path ?? _suggestedPath) is string p ? Path.GetFileNameWithoutExtension(p) : "Documento",
            InitialDirectory = InitialFolder(),
            AddExtension = true,
            OverwritePrompt = true,
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return false;
        return SaveTo(dialog.FileName, FileFormats.FromPath(dialog.FileName));
    }

    private bool SaveTo(string path, FileFormat format)
    {
        try
        {
            byte[] data = format switch
            {
                // In plain text tables go row by row, images with their description, footnotes at the end.
                FileFormat.Text => TextFile.Write(string.Join(Environment.NewLine,
                    Readable.From(ModelToSave()).Paragraphs.Select(p => p.Text.Replace(Editor.Pagination.PageBreak.ToString(), ""))), _textEncoding),
                FileFormat.Rtf => Encoding.Latin1.GetBytes(FullRtf()),
                FileFormat.Html => new UTF8Encoding(false).GetBytes(HtmlFormat.Write(ModelToSave())),
                FileFormat.Epub => CreateEpub(),
                FileFormat.Odt => CreateOdt(),
                _ => CreateDocx(),
            };
            // A temporary file is written first: if something goes wrong the original stays intact.
            var temporary = path + ".eleu-tmp";
            File.WriteAllBytes(temporary, data);
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"Impossibile salvare {Path.GetFileName(path)}.\n\n{e.Message}", AppName,
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }

        SetCurrentDocument(path, format);
        _recovery.Delete();
        ConfirmationSound(Cue.Saved);
        AnnounceShortly($"Salvato {DocumentName}");
        return true;
    }

    /// <summary>The document as a model, with page and properties: what is written in the formats other than RTF.</summary>
    private DocumentContent ModelToSave()
    {
        var content = CompleteModel();
        content.Page = _page;
        content.Metadata = _metadata with { Created = _metadata.Created ?? DateTime.Now };
        return content;
    }

    private byte[] CreateDocx()
    {
        using var stream = new MemoryStream();
        DocxFormat.Write(ModelToSave(), stream);
        return stream.ToArray();
    }

    private byte[] CreateOdt()
    {
        using var stream = new MemoryStream();
        OdtFormat.Write(ModelToSave(), stream);
        return stream.ToArray();
    }

    private byte[] CreateEpub()
    {
        using var stream = new MemoryStream();
        var content = ModelToSave();
        // A book without a title takes the file name.
        if (content.Metadata.Title.Length == 0)
            content.Metadata = content.Metadata with { Title = Path.GetFileNameWithoutExtension(DocumentName) };
        EpubFormat.Write(content, stream);
        return stream.ToArray();
    }

    /// <summary>Asks whether to save the changes. Returns false if the user cancels.</summary>
    private bool ConfirmDiscardChanges()
    {
        if (!_text.Modified)
            return true;

        var response = MessageBox.Show(this, $"Vuoi salvare le modifiche a {DocumentName}?", AppName,
            MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        switch (response)
        {
            case DialogResult.Yes:
                return Save();
            case DialogResult.No:
                _recovery.Delete();
                return true;
            default:
                return false;
        }
    }

    /// <summary>Empty document with the font, paragraph and page chosen as "Predefinito".</summary>
    private void LoadNewDocument()
    {
        LoadContent(new DocumentContent { Page = _settings.DefaultPage ?? new PageLayout() });
        if (_settings.BaseParagraph is Editor.ParagraphFormat paragraph)
        {
            _text.SelectAll();
            Editor.NativeRichEdit.SetParagraphFormat(_text, paragraph);
            _text.Select(0, 0);
        }
        _text.Modified = false;
    }

    private void LoadContent(DocumentContent content)
    {
        if (content.Paragraphs.Count == 0)
            content.Paragraphs.Add(new Paragraph());
        _page = content.Page;
        _metadata = content.Metadata;
        LoadRtf(RtfWriter.Write(content, _settings.BaseFont, _settings.BaseSize), content);
        AfterLoad();
    }

    private void AfterLoad()
    {
        // The positions of the reading in progress belonged to the previous document.
        _reader?.Stop();
        _text.Select(0, 0);
        ApplyZoom();
    }

    private void SetCurrentDocument(string? path, FileFormat format, string? suggestedPath = null)
    {
        _path = path;
        _suggestedPath = suggestedPath;
        _format = format;
        if (format != FileFormat.Text)
            _textEncoding = TextFile.Utf8WithoutBom;
        if (path is not null)
            _settings.AddRecent(path);
        _text.Modified = false;
        UpdateTitle();
        UpdateStatusBar();
    }

    private string InitialFolder() =>
        (_path ?? _suggestedPath) is string p ? Path.GetDirectoryName(p)! : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

    private void UpdateRecentFiles(ToolStripMenuItem menu)
    {
        menu.DropDownItems.Clear();
        int number = 1;
        foreach (var path in _settings.RecentFiles)
        {
            var file = path;
            menu.DropDownItems.Add(new ToolStripMenuItem($"&{number++} {Path.GetFileName(file)}", null, (_, _) => BeginInvoke(() =>
            {
                if (ConfirmDiscardChanges())
                    OpenInWindow(file);
            }))
            { ToolTipText = file });
        }
        if (menu.DropDownItems.Count == 0)
            menu.DropDownItems.Add(new ToolStripMenuItem("Nessun file recente") { Enabled = false });
    }

    private void SaveRecoveryCopy()
    {
        if (_text.Modified)
            _recovery.Save(FullRtf(), _path);
    }

    /// <summary>If the last session ended badly, offers to recover the text. Returns true if recovered.</summary>
    private bool OfferRecovery()
    {
        using var abandoned = Recovery.Abandoned(Recovery.DefaultFolder);
        if (abandoned is null)
            return false;

        var response = MessageBox.Show(this,
            "Eleu non è stato chiuso correttamente l'ultima volta.\nVuoi recuperare il documento che non era stato salvato?",
            AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (response != DialogResult.Yes)
        {
            abandoned.Delete();
            return false;
        }

        try
        {
            var (rtf, path) = abandoned.Read();
            LoadRtf(rtf);
            _page = RtfReader.ReadPage(rtf);
            _metadata = Metadata.FromRtf(rtf);
            AfterLoad();
            SetCurrentDocument(path, path is null ? FileFormat.Docx : FileFormats.FromPath(path));
            _text.Modified = true;
            UpdateTitle();
            // The copy moves to this window: the old one is no longer needed.
            abandoned.Delete();
            SaveRecoveryCopy();
            AnnounceShortly("Documento recuperato. Ricordati di salvarlo.");
            return true;
        }
        catch (Exception e) when (e is IOException or ArgumentException)
        {
            abandoned.Delete();
            return false;
        }
    }

    /// <summary>Remembers where you are in the open document, to start from there next time.</summary>
    private void RememberPosition()
    {
        if (_path is not null)
            _settings.RememberPosition(_path, _text.SelectionStart);
    }

    /// <summary>Moves the cursor to where it was last time; returns what to add to the announcement.</summary>
    private string ResumeWhereYouLeft(string path)
    {
        if (_settings.RememberedPosition(path) is not int position || position <= 0 || position > _text.TextLength)
            return "";
        _text.Select(position, 0);
        _text.ScrollToCaret();
        return $". Ripreso da dove eri, riga {CurrentPosition().Line}";
    }
}
