using Eleu.Accessibility;
using Eleu.Documents;
using Eleu.UI;
using Eleu.Speech;

namespace Eleu;

// Reading aloud and audiobook export.
public sealed partial class MainWindow
{
    private VoiceCatalog? _catalog;
    private SpeechReader? _reader;
    private SpeechReader? _testReader;
    private int? _cursorWhilePaused;
    private System.Windows.Forms.Timer? _readingTimer;
    private string _readingText = "";

    private VoiceCatalog Catalog
    {
        get
        {
            _catalog ??= VoiceCatalog.Default();
            _catalog.Pronunciations = _settings.Pronunciations;
            return _catalog;
        }
    }

    private void ManagePronunciations()
    {
        using var window = new PronunciationsDialog(_settings.Pronunciations);
        if (window.ShowDialog(this) != DialogResult.OK)
            return;
        _settings.Pronunciations = window.Pronunciations;
        _settings.Save();
        AnnounceShortly(window.Pronunciations.Count == 1 ? "1 pronuncia" : $"{window.Pronunciations.Count} pronunce");
    }

    private VoiceParameters VoiceParameters => new(_settings.VoiceRate, _settings.VoiceVolume);

    private SpeechReader Reader
    {
        get
        {
            if (_reader is null)
            {
                _reader = new SpeechReader(SynthesizeWithChosenVoice);
                _reader.Finished += (_, _) => BeginInvoke(() =>
                {
                    ClearReadingWord();
                    ConfirmationSound(Cue.EndOfReading);
                });
                _reader.Error += (_, e) => MessageBox.Show(this, $"La sintesi vocale si è interrotta.\n\n{e.Message}", AppName,
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            return _reader;
        }
    }

    private Task<SynthesizedAudio> SynthesizeWithChosenVoice(string text, CancellationToken cancel)
    {
        var voice = Catalog.Choose(_settings.VoiceId) ?? throw new InvalidOperationException("Nessuna voce installata.");
        return Catalog.SynthesizeAsync(text, voice, VoiceParameters, cancel);
    }

    /// <summary>F5: reads from the cursor (or the selection); while reading it pauses and resumes.</summary>
    private void ReadOrPause()
    {
        if (_reader is { IsReading: true })
        {
            if (!_reader.IsPaused)
            {
                _reader.Pause();
                MoveCursorWhileReading();
                _cursorWhilePaused = _text.SelectionStart;
                return;
            }
            // If the cursor was moved during the pause, restart from there; otherwise resume.
            if (_cursorWhilePaused == _text.SelectionStart)
            {
                _reader.Resume();
                return;
            }
        }

        int start = _text.SelectionStart;
        int end = _text.SelectionLength > 0 ? start + _text.SelectionLength : _text.TextLength;
        // At the end of the text (just after typing) there is nothing ahead: read from the beginning instead.
        if (_text.SelectionLength == 0 && Passages.FromText(_text.Text, start, end).Count == 0)
            start = 0;
        StartReading(start, end);
    }

    private void ReadAll() => StartReading(0, _text.TextLength);

    private void StartReading(int start, int end)
    {
        if (Catalog.Choose(_settings.VoiceId) is null)
        {
            ShowNoVoice();
            return;
        }
        var passages = Passages.FromText(_text.Text, start, end);
        if (passages.Count == 0)
        {
            _announcer.Announce("Niente da leggere");
            return;
        }
        _cursorWhilePaused = null;
        Reader.Start(passages);
        FollowReading();
    }

    /// <summary>Stops reading leaving the cursor on the word reached. Returns false if it was not reading.</summary>
    private bool StopReading()
    {
        if (_reader is not { IsReading: true })
            return false;
        MoveCursorWhileReading();
        _reader.Stop();
        ClearReadingWord();
        return true;
    }

    /// <summary>
    /// While reading aloud the word being heard is highlighted and kept on screen (it helps low-vision and
    /// dyslexic readers follow along). The text and the cursor do not move: the screen reader stays quiet.
    /// </summary>
    private void FollowReading()
    {
        _readingText = _text.Text;
        if (_readingTimer is null)
        {
            _readingTimer = new System.Windows.Forms.Timer { Interval = 80 };
            _readingTimer.Tick += (_, _) => UpdateReadingWord();
        }
        _readingTimer.Start();
    }

    private void UpdateReadingWord()
    {
        if (_reader is not { IsReading: true })
        {
            ClearReadingWord();
            return;
        }
        if (_reader.CurrentPosition is not int position || position > _readingText.Length)
            return;
        var (start, end) = WordAround(_readingText, position);
        if (end <= start)
            return;
        bool changed = _text.ReadingWord != (start, end);
        _text.ReadingWord = (start, end);
        if (changed)
            _text.ShowReadingWord();
    }

    private void ClearReadingWord()
    {
        _readingTimer?.Stop();
        _text.ReadingWord = null;
    }

    private void MoveCursorWhileReading()
    {
        if (_reader?.CurrentPosition is int position)
        {
            _text.Select(Math.Min(position, _text.TextLength), 0);
            _text.ScrollToCaret();
        }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape && StopReading())
            return true;
        if (AlternateKey(keyData))
            return true;
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void ChooseVoice()
    {
        var items = Catalog.Voices;
        if (items.Count == 0)
        {
            ShowNoVoice();
            return;
        }

        using var window = new VoiceDialog(items, Catalog.Choose(_settings.VoiceId), VoiceParameters);
        window.TestRequest += (_, _) =>
        {
            if (window.ChosenVoice is not AvailableVoice voice)
                return;
            var parameters = window.Parameters;
            _testReader?.Dispose();
            _testReader = new SpeechReader((text, cancel) => Catalog.SynthesizeAsync(text, voice, parameters, cancel));
            _testReader.Start([new Passage($"Ciao, sono {voice.Name}. Così leggerò i tuoi documenti.", 0)]);
        };

        if (window.ShowDialog(this) == DialogResult.OK && window.ChosenVoice is AvailableVoice choice)
        {
            _settings.VoiceId = choice.Id;
            _settings.VoiceRate = window.Parameters.Rate;
            _settings.VoiceVolume = window.Parameters.Volume;
            _settings.Save();
        }
        _testReader?.Stop();
    }

    private void ExportAudiobook()
    {
        StopReading();
        if (Catalog.Choose(_settings.VoiceId) is not AvailableVoice voice)
        {
            ShowNoVoice();
            return;
        }

        var chapters = Passages.Chapters(ContentWithFootnotes(), Path.GetFileNameWithoutExtension(DocumentName));
        if (chapters.Count == 0)
        {
            _announcer.Announce("Il documento è vuoto");
            return;
        }

        using var dialog = new FolderBrowserDialog
        {
            Description = "Scegli la cartella in cui salvare l'audiolibro",
            UseDescriptionForTitle = true,
            InitialDirectory = InitialFolder(),
            ShowNewFolderButton = true,
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        var folder = dialog.SelectedPath;

        int alreadyPresent = AudiobookExporter.FileNames(chapters).Count(n => File.Exists(Path.Combine(folder, n)));
        if (alreadyPresent > 0 && MessageBox.Show(this,
                $"Nella cartella ci sono già {alreadyPresent} file con lo stesso nome. Vuoi sostituirli?", AppName,
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        IReadOnlyList<string> created = [];
        Exception? error = null;
        bool cancelled = false;

        using (var progressDialog = new ProgressDialog("Creazione dell'audiolibro"))
        {
            var progress = new Progress<ExportProgress>(a =>
            {
                var state = $"Capitolo {a.Chapter} di {a.TotalChapters}: {a.Title}";
                progressDialog.Report(state, a.Chapter - 1, a.TotalChapters);
                _announcer.Announce(state);
            });
            progressDialog.Shown += async (_, _) =>
            {
                try
                {
                    created = await new AudiobookExporter(Catalog).ExportAsync(
                        chapters, folder, voice, VoiceParameters, progress, progressDialog.Cancellation);
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
                    progressDialog.DialogResult = DialogResult.OK;
                }
            };
            progressDialog.ShowDialog(this);
        }

        if (cancelled)
            AnnounceShortly("Creazione dell'audiolibro annullata");
        else if (error is not null)
            MessageBox.Show(this, $"Impossibile creare l'audiolibro.\n\n{error.Message}", AppName,
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        else
            MessageBox.Show(this,
                $"Audiolibro creato: {(created.Count == 1 ? "1 file MP3" : $"{created.Count} file MP3")} nella cartella {folder}.",
                AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    /// <summary>
    /// Voce > Crea un libro DAISY: in a new folder named after the document, the DAISY 2.02 book with synchronized text
    /// and audio, to listen to with a DAISY player going from heading to heading.
    /// </summary>
    private void ExportDaisy()
    {
        StopReading();
        if (Catalog.Choose(_settings.VoiceId) is not AvailableVoice voice)
        {
            ShowNoVoice();
            return;
        }
        var content = ContentWithFootnotes();
        content.Metadata = _metadata.Title.Length > 0 ? _metadata : _metadata with { Title = Path.GetFileNameWithoutExtension(DocumentName) };
        if (DaisyExporter.Sections(content, content.Metadata.Title).Count == 0)
        {
            _announcer.Announce("Il documento è vuoto");
            return;
        }

        using var dialog = new FolderBrowserDialog
        {
            Description = "Scegli dove creare il libro DAISY (verrà fatta una cartella col nome del documento)",
            UseDescriptionForTitle = true,
            InitialDirectory = InitialFolder(),
            ShowNewFolderButton = true,
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        var name = Passages.FileName(1, 1, content.Metadata.Title)["01 - ".Length..^".mp3".Length]; // the title cleaned for a folder name
        var folder = Path.Combine(dialog.SelectedPath, $"{name} (DAISY)");
        if (Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder).Any())
        {
            if (MessageBox.Show(this, $"La cartella {Path.GetFileName(folder)} esiste già e non è vuota. Vuoi sostituire il libro che contiene?",
                    AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;
            foreach (var old in Directory.EnumerateFiles(folder).Where(f => Path.GetExtension(f) is ".smil" or ".mp3" or ".html"))
                File.Delete(old);
        }

        int file = 0;
        Exception? error = null;
        bool cancelled = false;
        using (var progressDialog = new ProgressDialog("Creazione del libro DAISY"))
        {
            var progress = new Progress<ExportProgress>(a =>
            {
                var state = $"Sezione {a.Chapter} di {a.TotalChapters}: {a.Title}";
                progressDialog.Report(state, a.Chapter - 1, a.TotalChapters);
                _announcer.Announce(state);
            });
            progressDialog.Shown += async (_, _) =>
            {
                try
                {
                    file = await new DaisyExporter(Catalog).ExportAsync(content, folder, voice, VoiceParameters, progress, progressDialog.Cancellation);
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
                    progressDialog.DialogResult = DialogResult.OK;
                }
            };
            progressDialog.ShowDialog(this);
        }

        if (cancelled)
            AnnounceShortly("Creazione del libro DAISY annullata");
        else if (error is not null)
            MessageBox.Show(this, $"Impossibile creare il libro DAISY.\n\n{error.Message}", AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
        else
            MessageBox.Show(this, $"Libro DAISY creato nella cartella {folder}: {file} file. Si apre con un lettore DAISY partendo da ncc.html.",
                AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void ShowNoVoice() =>
        MessageBox.Show(this,
            "Non ho trovato voci di sintesi installate.\nPuoi aggiungerle da Impostazioni di Windows, Ora e lingua, Voce.",
            AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
}
