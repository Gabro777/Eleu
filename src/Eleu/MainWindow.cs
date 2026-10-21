using Eleu.Accessibility;
using Eleu.Documents;
using Eleu.Editor;
using Eleu.UI;

namespace Eleu;

/// <summary>
/// Main window: a single full-window text box, the menu and the status bar.
/// Every command is in the menu (reachable with Alt) and has a keyboard shortcut.
/// The parts are split by topic in the MainWindow.*.cs files.
/// </summary>
public sealed partial class MainWindow : Form
{
    private const string AppName = "Eleu";

    private readonly DocumentTextBox _text;
    private readonly ToolStripStatusLabel _positionState, _characterState, _paragraphState, _message;
    private readonly MenuStrip _menu;
    private readonly IAnnouncer _announcer;
    private readonly Settings _settings;
    private readonly Recovery _recovery;
    private readonly System.Windows.Forms.Timer _recoveryTimer;
    private readonly string? _filesToOpen;

    /// <param name="guide">The window shows the Eleu Guide instead of a document.</param>
    public MainWindow(string? filesToOpen = null, bool guide = false)
    {
        _filesToOpen = filesToOpen;
        _openGuide = guide;
        _settings = Settings.Shared;
        Eleu.Braille.BrailleTranslator.Current = _settings.BrailleTable;
        _recovery = new Recovery(Recovery.DefaultFolder);

        Width = 1000;
        Height = 700;
        StartPosition = FormStartPosition.CenterScreen;
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? Icon;
        // Maximized, if it was last time (at first, yes): more room for enlarged text.
        if (_settings.Maximized)
            WindowState = FormWindowState.Maximized;

        _text = new DocumentTextBox
        {
            Dock = DockStyle.Fill,
            AcceptsTab = true,
            DetectUrls = false,
            WordWrap = _settings.WordWrap,
            HideSelection = false,
            BorderStyle = BorderStyle.None,
            Font = new Font("Segoe UI", DocumentContent.BaseSize),
            TabIndex = 0,
        };
        AccessibleNameOverride.Set(_text, "Documento");
        _text.HandleCreated += (_, _) =>
        {
            NativeRichEdit.EnableAdvancedTypography(_text);
            NativeRichEdit.SpellCheckAsYouType(_text, _settings.UnderlineErrors);
            SetTextMargins();
        };
        _text.SelectionChanged += (_, _) =>
        {
            UpdateStatusBar();
            AnnounceCellChange();
            AnnounceImage();
            ReportElements();
        };
        _text.TextChanged += (_, _) => UpdateStatusBar();
        _text.ModifiedChanged += (_, _) => UpdateTitle();
        PrepareBookmarks();
        PrepareFootnotes();
        PrepareComments();
        PrepareHeaderRows();
        PrepareImages();
        PrepareBrailleTyping();
        _text.ContextMenuStrip = CreateContextMenu();

        _positionState = new ToolStripStatusLabel { TextAlign = ContentAlignment.MiddleLeft, BorderSides = ToolStripStatusLabelBorderSides.Right };
        _characterState = new ToolStripStatusLabel { TextAlign = ContentAlignment.MiddleLeft, BorderSides = ToolStripStatusLabelBorderSides.Right };
        _paragraphState = new ToolStripStatusLabel { TextAlign = ContentAlignment.MiddleLeft, BorderSides = ToolStripStatusLabelBorderSides.Right };
        _message = new ToolStripStatusLabel { Spring = true, TextAlign = ContentAlignment.MiddleLeft, Font = new Font(Font, FontStyle.Bold) };
        var statusBar = new StatusStrip { AccessibleName = "Barra di stato" };
        statusBar.Items.AddRange([_positionState, _characterState, _paragraphState, _message]);

        _brailleLine = CreateBrailleLine();
        // After resizing, not during it: changing the label text while WinForms
        // lays out the controls interrupts the layout and the strip stays 100 pixels wide.
        _brailleLine.Resize += (_, _) => { if (IsHandleCreated) BeginInvoke(UpdateBrailleLine); };
        _text.Resize += (_, _) => { if (IsHandleCreated) BeginInvoke(UpdateBrailleLine); };

        _bar = CreateBar();
        _ruler = CreateRuler();
        _text.HScroll += (_, _) => UpdateBars();
        _text.Resize += (_, _) => { if (IsHandleCreated) BeginInvoke(UpdateBars); };

        _menu = CreateMenu();
        MainMenuStrip = _menu;
        RegisterCommands();

        // The order of adding matters: the box with Dock.Fill must be added first,
        // and among the controls at the bottom the one added later is lower (the status bar);
        // at the top, on the contrary, it is higher: menu, button bar, ruler.
        Controls.Add(_text);
        Controls.Add(_ruler);
        Controls.Add(_bar);
        Controls.Add(_brailleLine);
        Controls.Add(statusBar);
        Controls.Add(_menu);

        _announcer = new VisibleAnnouncer(new UiaAnnouncer(this), ShowMessage);
        ActiveControl = _text;

        _recoveryTimer = new System.Windows.Forms.Timer { Interval = _settings.RecoveryMinutes * 60_000 };
        _recoveryTimer.Tick += (_, _) => SaveRecoveryCopy();
        _recoveryTimer.Start();

        UpdateTitle();
        UpdateStatusBar();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        LoadNewDocument();
        _text.Modified = false;
        if (_openGuide)
            LoadGuide();
        else if (!OfferRecovery() && _filesToOpen is not null)
            OpenFile(_filesToOpen);
        if (EleuContext.Active)
            CheckAtStartup();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        if (!ConfirmDiscardChanges())
        {
            e.Cancel = true;
            return;
        }
        RememberPosition();
        _recoveryTimer.Stop();
        _reader?.Dispose();
        _testReader?.Dispose();
        _recovery.Delete();
        _recovery.Dispose();
        if (WindowState != FormWindowState.Minimized)
            _settings.Maximized = WindowState == FormWindowState.Maximized;
        _settings.Save();
    }

    /// <summary>
    /// The menus follow the organization and keys that users of accessible word processors are used to:
    /// File, Modifica, Visualizza, Inserisci, Formato, Strumenti, Voce, Aiuto. In Inserisci every
    /// kind of element has the same submenu (Aggiungi, Elimina, Precedente, Successivo, Elenco).
    /// </summary>
    private MenuStrip CreateMenu()
    {
        var reopen = new ToolStripMenuItem("&Riapri");
        reopen.DropDownItems.Add(new ToolStripMenuItem("Nessun file recente") { Enabled = false });
        reopen.DropDownOpening += (_, _) => UpdateRecentFiles(reopen);

        var file = CreateMenuItem("&File",
            MenuItem("&Nuovo documento", Keys.Control | Keys.N, New),
            new ToolStripSeparator(),
            MenuItem("&Apri...", Keys.Control | Keys.O, Open),
            MenuItem("Apri in un'altra &finestra...", Keys.None, OpenInNewWindow),
            MenuItem("Ac&quisisci da scanner...", Keys.Control | Keys.Alt | Keys.S, ScanDocument, "Ctrl+Alt+S"),
            MenuItem("Foto &dal telefono...", Keys.None, PhonePhotos),
            reopen,
            new ToolStripSeparator(),
            MenuItem("Sa&lva", Keys.Control | Keys.S, () => Save()),
            MenuItem("Sal&va con nome...", Keys.F12, () => SaveAs()),
            MenuItem("Salva sele&zione...", Keys.None, SaveSelection),
            MenuItem("Salva c&ome Braille (BRF)...", Keys.None, () => Braille(toFile: true)),
            new ToolStripSeparator(),
            MenuItem("I&mposta pagina...", Keys.None, PageSetup),
            MenuItem("An&teprima di stampa...", Keys.None, PrintPreview),
            MenuItem("&Stampa...", Keys.Control | Keys.P, Print),
            MenuItem("Stampa &braille...", Keys.Control | Keys.Alt | Keys.P, () => Braille(toFile: false)),
            new ToolStripSeparator(),
            MenuItem("&Proprietà...", Keys.None, OpenProperties),
            new ToolStripSeparator(),
            MenuItem("&Esci", Keys.Alt | Keys.F4, Close));

        var find = CreateMenuItem("C&erca",
            MenuItem("&Trova...", Keys.Control | Keys.Shift | Keys.T, () => OpenFind(false)),
            MenuItem("Trova s&uccessivo", Keys.F3, () => FindAndAnnounce(forward: true)),
            MenuItem("Trova &precedente", Keys.Shift | Keys.F3, () => FindAndAnnounce(forward: false)),
            new ToolStripSeparator(),
            MenuItem("&Sostituisci...", Keys.Control | Keys.Shift | Keys.S, () => OpenFind(true)),
            MenuItem("Sostituisci successi&vo", Keys.F4, () => ReplaceCurrent(forward: true)),
            MenuItem("Sostituisci prece&dente", Keys.Shift | Keys.F4, () => ReplaceCurrent(forward: false)),
            new ToolStripSeparator(),
            MenuItem("V&ai a...", Keys.Control | Keys.Shift | Keys.G, OpenGoTo),
            MenuItem("Vai a successiv&o", Keys.F2, () => GoToNext(forward: true)),
            MenuItem("Vai a precede&nte", Keys.Shift | Keys.F2, () => GoToNext(forward: false)));

        var edit = CreateMenuItem("&Modifica",
            MenuItem("&Annulla", Keys.Control | Keys.Z, () => _text.Undo()),
            MenuItem("&Ripeti", Keys.Control | Keys.Y, () => _text.Redo()),
            MenuItem("&Taglia", Keys.Control | Keys.X, Cut),
            MenuItem("&Copia", Keys.Control | Keys.C, Copy),
            MenuItem("Copia &braille", Keys.None, CopyBraille),
            MenuItem("&Incolla", Keys.Control | Keys.V, Paste),
            new ToolStripSeparator(),
            find,
            CreateMenuItem("&Seleziona",
                MenuItem("Seleziona &parola", Keys.None, SelectWord),
                MenuItem("Seleziona r&iga", Keys.None, SelectLine),
                MenuItem("Seleziona p&aragrafo", Keys.None, SelectParagraph),
                MenuItem("Seleziona &tutto", Keys.Control | Keys.A, () => _text.SelectAll()),
                new ToolStripSeparator(),
                MenuItem("Inizio blocco / seleziona &blocco", Keys.F8, MarkBlock),
                MenuItem("Annulla bl&occo", Keys.Shift | Keys.F8, CancelBlock)));

        var display = CreateMenuItem("&Visualizza",
            MenuItem("Barra degli s&trumenti", Keys.None, ToggleToolbar),
            MenuItem("&Righello", Keys.None, ToggleRuler),
            MenuItem("R&iga Braille in fondo alla finestra", Keys.Control | Keys.Shift | Keys.R, ToggleBrailleLine),
            MenuItem("Anteprima &braille", Keys.None, BraillePreview),
            MenuItem("A &capo automatico", Keys.None, ToggleWordWrap),
            CreateMenuItem("&Zoom",
                MenuItem("Zoom &avanti", Keys.Control | Keys.Alt | Keys.Oemplus, () => ChangeZoom(+0.25f), "Ctrl+Alt++"),
                MenuItem("Zoom &indietro", Keys.Control | Keys.Alt | Keys.OemMinus, () => ChangeZoom(-0.25f), "Ctrl+Alt+-"),
                MenuItem("Zoom &normale", Keys.Control | Keys.Alt | Keys.D0, () => SetZoom(1f), "Ctrl+Alt+0")));

        var insert = CreateMenuItem("&Inserisci",
            CreateMenuItem("&Segnalibri",
                MenuItem("&Aggiungi segnalibro...", Keys.Control | Keys.Shift | Keys.F5, AddBookmark),
                MenuItem("&Elimina segnalibro", Keys.None, DeleteBookmark),
                new ToolStripSeparator(),
                MenuItem("Segnalibro &precedente", Keys.Shift | Keys.F5, () => NearbyBookmark(forward: false)),
                MenuItem("Segnalibro &successivo", Keys.F5, () => NearbyBookmark(forward: true)),
                new ToolStripSeparator(),
                MenuItem("E&lenco dei segnalibri...", Keys.None, ShowBookmarkList),
                MenuItem("&Riassunto dai segnalibri", Keys.None, BookmarkSummary)),
            CreateMenuItem("&Interruzioni di pagina",
                MenuItem("&Aggiungi interruzione", Keys.Control | Keys.Enter, InsertPageBreak, "Ctrl+Invio"),
                MenuItem("&Elimina interruzioni", Keys.None, DeletePageBreaks),
                new ToolStripSeparator(),
                MenuItem("Interruzione &precedente", Keys.None, () => NearbyPageBreak(forward: false)),
                MenuItem("Interruzione &successiva", Keys.None, () => NearbyPageBreak(forward: true)),
                new ToolStripSeparator(),
                MenuItem("E&lenco delle interruzioni...", Keys.None, ShowPageBreakList)),
            CreateMenuItem("&Note a piè di pagina",
                MenuItem("&Aggiungi nota...", Keys.Control | Keys.Alt | Keys.F, AddFootnote),
                MenuItem("&Modifica nota...", Keys.None, EditFootnote),
                MenuItem("&Elimina nota", Keys.None, DeleteFootnote),
                new ToolStripSeparator(),
                MenuItem("Nota &precedente", Keys.None, () => NearbyFootnote(forward: false)),
                MenuItem("Nota &successiva", Keys.None, () => NearbyFootnote(forward: true)),
                new ToolStripSeparator(),
                MenuItem("E&lenco delle note...", Keys.None, ShowFootnoteList)),
            CreateMenuItem("&Commenti",
                MenuItem("&Aggiungi commento...", Keys.Control | Keys.Alt | Keys.M, AddComment),
                MenuItem("&Modifica commento...", Keys.None, EditComment),
                MenuItem("&Elimina commento", Keys.None, DeleteComment),
                new ToolStripSeparator(),
                MenuItem("Commento &precedente", Keys.None, () => NearbyComment(forward: false)),
                MenuItem("Commento &successivo", Keys.None, () => NearbyComment(forward: true)),
                new ToolStripSeparator(),
                MenuItem("E&lenco dei commenti...", Keys.None, ShowCommentList)),
            CreateMenuItem("I&mmagini",
                MenuItem("&Aggiungi immagine...", Keys.None, AddImage),
                MenuItem("&Modifica immagine...", Keys.None, EditImage),
                MenuItem("&Elimina immagine", Keys.None, DeleteImage),
                new ToolStripSeparator(),
                MenuItem("Immagine &precedente", Keys.None, () => NearbyImage(forward: false)),
                MenuItem("Immagine &successiva", Keys.None, () => NearbyImage(forward: true)),
                new ToolStripSeparator(),
                MenuItem("Sal&va immagine con nome...", Keys.None, SaveImage),
                MenuItem("&Grafica in rilievo...", Keys.None, TactileGraphics),
                MenuItem("E&lenco delle immagini...", Keys.None, ShowImageList)),
            CreateMenuItem("T&abelle",
                MenuItem("&Aggiungi tabella...", Keys.None, AddTable),
                MenuItem("&Elimina tabella", Keys.None, DeleteTable),
                new ToolStripSeparator(),
                MenuItem("Inserisci riga sop&ra", Keys.None, () => InsertRow(bottom: false)),
                MenuItem("Inserisci riga so&tto", Keys.None, () => InsertRow(bottom: true)),
                MenuItem("Inserisci colonna a si&nistra", Keys.None, () => InsertColumn(onRight: false)),
                MenuItem("Inserisci colonna a &destra", Keys.None, () => InsertColumn(onRight: true)),
                MenuItem("Elimina ri&ga", Keys.None, DeleteRow),
                MenuItem("Elimina co&lonna", Keys.None, DeleteColumn),
                MenuItem("I&mposta larghezza colonna...", Keys.None, ColumnWidth),
                MenuItem("Riga di intesta&zione", Keys.None, ToggleHeaderRow),
                MenuItem("Ordina rig&he dalla A alla Z", Keys.None, () => SortLines(ascending: true)),
                MenuItem("Ordina righe all'in&verso, dalla Z alla A", Keys.None, () => SortLines(ascending: false)),
                MenuItem("Gra&fico in rilievo dalla tabella...", Keys.None, TableChart),
                new ToolStripSeparator(),
                MenuItem("Cella a s&inistra", Keys.Control | Keys.Alt | Keys.Left, () => NearbyCell(0, -1), "Ctrl+Alt+Freccia sinistra"),
                MenuItem("&Cella a destra", Keys.Control | Keys.Alt | Keys.Right, () => NearbyCell(0, 1), "Ctrl+Alt+Freccia destra"),
                MenuItem("Cella &sopra", Keys.Control | Keys.Alt | Keys.Up, () => NearbyCell(-1, 0), "Ctrl+Alt+Freccia su"),
                MenuItem("Cella sott&o", Keys.Control | Keys.Alt | Keys.Down, () => NearbyCell(1, 0), "Ctrl+Alt+Freccia giù"),
                new ToolStripSeparator(),
                MenuItem("Tabella &precedente", Keys.None, () => NearbyTable(forward: false)),
                MenuItem("Tabella s&uccessiva", Keys.None, () => NearbyTable(forward: true)),
                MenuItem("Elenco delle ta&belle...", Keys.None, ShowTableList)),
            CreateMenuItem("&Titoli",
                MenuItem("&Aggiungi titolo...", Keys.Control | Keys.Alt | Keys.T, AddHeading),
                MenuItem("&Elimina titolo", Keys.None, () => SetHeading(0)),
                new ToolStripSeparator(),
                MenuItem("Titolo &precedente", Keys.None, () => NearbyHeading(forward: false)),
                MenuItem("Titolo &successivo", Keys.None, () => NearbyHeading(forward: true)),
                new ToolStripSeparator(),
                MenuItem("Inserisci so&mmario", Keys.None, InsertTableOfContents),
                MenuItem("E&lenco dei titoli...", Keys.None, ShowHeadingList)),
            new ToolStripSeparator(),
            MenuItem("Sim&bolo...", Keys.None, InsertSymbol));

        var format = CreateMenuItem("F&ormato",
            MenuItem("&Carattere...", Keys.Control | Keys.Shift | Keys.F, OpenCharacter),
            MenuItem("&Paragrafo...", Keys.Control | Keys.Shift | Keys.P, OpenParagraph),
            CreateMenuItem("&Elenchi",
                MenuItem("Aggiungi elenco &puntato", Keys.Control | Keys.Shift | Keys.L, () => SetList(ListKind.Bulleted)),
                MenuItem("Aggiungi elenco &numerato", Keys.Control | Keys.Shift | Keys.N, () => SetList(ListKind.Numbered)),
                MenuItem("&Togli l'elenco", Keys.None, () => SetList(ListKind.None)),
                new ToolStripSeparator(),
                MenuItem("Voce dell'elenco p&recedente", Keys.None, () => NearbyListItem(forward: false)),
                MenuItem("Voce dell'elenco &successiva", Keys.None, () => NearbyListItem(forward: true))),
            CreateMenuItem("&Maiuscole/minuscole",
                MenuItem("&Minuscolo", Keys.Control | Keys.OemMinus, () => SetCase(Uppercase.Lower, "minuscolo"), "Ctrl+-"),
                MenuItem("M&aiuscolo", Keys.Control | Keys.Oemplus, () => SetCase(Uppercase.Upper, "MAIUSCOLO"), "Ctrl++"),
                MenuItem("&Iniziale maiuscola", Keys.None, () => SetCase(Uppercase.Initials, "Iniziali maiuscole")),
                MenuItem("Al&terna", Keys.Control | Keys.Shift | Keys.A, ToggleCase)),
            CreateMenuItem("Formattazione &rapida",
                CreateMenuItem("&Carattere",
                    MenuItem("&Grassetto", Keys.Control | Keys.B, () => ToggleEffect(NativeRichEdit.Effect.Bold, "Grassetto")),
                    MenuItem("&Corsivo", Keys.Control | Keys.I, () => ToggleEffect(NativeRichEdit.Effect.Italic, "Corsivo")),
                    MenuItem("&Sottolineato", Keys.Control | Keys.U, () => ToggleEffect(NativeRichEdit.Effect.Underline, "Sottolineato")),
                    MenuItem("&Barrato", Keys.None, ToggleStrikethrough),
                    MenuItem("Rim&picciolisci il carattere",Keys.Control | Keys.Shift | Keys.OemMinus, () => ChangeSize(-1), "Maiusc+Ctrl+-"),
                    MenuItem("I&ngrandisci il carattere",Keys.Control | Keys.Shift | Keys.Oemplus, () => ChangeSize(1), "Maiusc+Ctrl++")),
                CreateMenuItem("&Paragrafo",
                    MenuItem("Allinea a &sinistra", Keys.Control | Keys.L, () => Align(Alignment.Left, "Allineato a sinistra")),
                    MenuItem("Allinea al &centro", Keys.Control | Keys.E, () => Align(Alignment.Center, "Centrato")),
                    MenuItem("Allinea a &destra", Keys.Control | Keys.R, () => Align(Alignment.Right, "Allineato a destra")),
                    MenuItem("&Giustifica", Keys.Control | Keys.J, () => Align(Alignment.Justified, "Giustificato")),
                    new ToolStripSeparator(),
                    MenuItem("Interlinea &1", Keys.Control | Keys.D1, () => SetLineSpacing(1f, "singola")),
                    MenuItem("Interlinea 1,&5", Keys.Control | Keys.D5, () => SetLineSpacing(1.5f, "una e mezza")),
                    MenuItem("Interlinea &2", Keys.Control | Keys.D2, () => SetLineSpacing(2f, "doppia")))));

        var tools = CreateMenuItem("&Strumenti",
            CreateMenuItem("&Correzione",
                MenuItem("&Errore successivo", Keys.F7, () => GoToError(forward: true)),
                MenuItem("Errore &precedente", Keys.Shift | Keys.F7, () => GoToError(forward: false)),
                MenuItem("&Correggi con il suggerimento", Keys.Control | Keys.F7, CorrectWithSuggestion),
                new ToolStripSeparator(),
                MenuItem("Controllo &ortografico...", Keys.Control | Keys.Shift | Keys.F7, OpenSpellCheck),
                MenuItem("&Dizionario personale...", Keys.None, ManageDictionary),
                MenuItem("So&ttolinea errori mentre scrivi", Keys.None, ToggleErrorUnderline)),
            CreateMenuItem("&Informazioni",
                MenuItem("&Posizione del cursore", Keys.F6, AnnouncePosition),
                MenuItem("&Conteggio parole", Keys.Control | Keys.F6, AnnounceStatistics),
                MenuItem("&Statistiche complete...", Keys.Control | Keys.Shift | Keys.F6, ShowFullStatistics),
                MenuItem("Parole più &usate...", Keys.None, MostUsedWords)),
            CreateMenuItem("Cerca sul &web", [.. WebServices.Select(s => MenuItem(s.Name, Keys.None, () => SearchOnWeb(s.Url)))]),
            new ToolStripSeparator(),
            MenuItem("&Pulizia del testo...", Keys.None, TextCleanup),
            MenuItem("Sostituzioni &multiple...", Keys.None, OpenMultipleReplacements),
            MenuItem("&Ordina paragrafi", Keys.None, SortParagraphs),
            MenuItem("Con&fronta con un altro documento...", Keys.None, CompareDocument),
            MenuItem("Scrittura &Braille a 6 tasti", Keys.Control | Keys.Shift | Keys.D6, ToggleBrailleTyping, "Ctrl+Maiusc+6"),
            MenuItem("Contro&llo per il Braille...", Keys.None, CheckForBraille),
            MenuItem("Cambia le scorciatoie da &tastiera...", Keys.None, CustomizeShortcuts),
            new ToolStripSeparator(),
            MenuItem("&Dettagli del punto in cui sei",Keys.Alt | Keys.Enter, AnnounceFormatting, "Alt+Invio"),
            new ToolStripSeparator(),
            MenuItem("Op&zioni...", Keys.Control | Keys.Shift | Keys.O, OpenOptions));

        var speech = CreateMenuItem("Vo&ce",
            MenuItem("Crea un &audiolibro MP3...", Keys.None, ExportAudiobook),
            MenuItem("Crea un libro &DAISY...", Keys.None, ExportDaisy),
            CreateMenuItem("&Lettura ad alta voce",
                MenuItem("&Leggi / pausa", Keys.F11, ReadOrPause),
                MenuItem("Leggi &tutto dall'inizio", Keys.Control | Keys.F11, ReadAll),
                MenuItem("&Ferma", Keys.Shift | Keys.F11, () => StopReading(), "Maiusc+F11 oppure Esc")),
            new ToolStripSeparator(),
            MenuItem("&Impostazioni della voce...", Keys.None, ChooseVoice),
            MenuItem("&Pronunce...", Keys.None, ManagePronunciations));

        var help = CreateMenuItem("&Aiuto",
            MenuItem("&Guida di Eleu", Keys.F1, OpenGuide),
            MenuItem("&Scorciatoie da tastiera", Keys.None, ShowShortcuts),
            MenuItem("Controlla &aggiornamenti...", Keys.None, CheckForUpdates),
            MenuItem("&Informazioni su Eleu", Keys.None, ShowAbout));

        var menu = new MenuStrip { AccessibleName = "Menu principale" };
        menu.Items.AddRange([file, edit, display, insert, format, tools, speech, CreateWindowsMenu(), help]);
        return menu;
    }

    /// <summary>
    /// Extra keys, outside the menus: those of Word and Notepad for the same commands,
    /// where they do not take keys that have another meaning.
    /// </summary>
    private bool AlternateKey(Keys keys)
    {
        Action? action = keys switch
        {
            Keys.Control | Keys.F => () => OpenFind(false),
            Keys.Control | Keys.H => () => OpenFind(true),
            Keys.Control | Keys.G => OpenGoTo,
            Keys.Control | Keys.D => OpenCharacter,
            _ => null,
        };
        if (action is null)
            return false;
        BeginInvoke(() => RunCommand(action));
        return true;
    }

    private static ToolStripMenuItem CreateMenuItem(string text, params ToolStripItem[] items)
    {
        var menu = new ToolStripMenuItem(text);
        menu.DropDownItems.AddRange(items);
        return menu;
    }

    private ToolStripMenuItem MenuItem(string text, Keys shortcut, Action action, string? readableShortcut = null)
    {
        // The action runs right after, not inside the click: if a screen reader activates the item via UI Automation
        // and the action opens a modal window, the screen reader's call would stay blocked
        // and Eleu would stop answering it while the window is open.
        var item = new ToolStripMenuItem(text, null, (_, _) => BeginInvoke(() => RunCommand(action)));
        if (shortcut != Keys.None)
            item.ShortcutKeys = shortcut;
        if (readableShortcut is not null)
            item.ShortcutKeyDisplayString = readableShortcut;
        return item;
    }

    /// <summary>
    /// Runs a command, then refreshes status bar and buttons: a format change with no selection (Bold before typing)
    /// changes neither text nor selection, so without this the Bold button would light up only at the next character.
    /// </summary>
    private void RunCommand(Action action)
    {
        action();
        if (!IsDisposed)
            UpdateStatusBar();
    }

    private void UpdateTitle() => Text = $"{(_text.Modified ? "*" : "")}{DocumentName} - {AppName}";

    /// <summary>Status bar: position, font and paragraph, as in accessible word processors.</summary>
    private void UpdateStatusBar()
    {
        var position = CurrentPosition();
        _positionState.Text = $"Col {position.Column}, Riga {position.Line} di {position.TotalLines}";
        if (_text.IsHandleCreated)
        {
            var character = NativeRichEdit.ReadCharacterFormat(_text);
            _characterState.Text = character.Font is string name
                ? $"{name}, {character.Size:0.#}"
                : "Caratteri misti";
            var alignment = NativeRichEdit.ReadAlignment(_text) switch
            {
                Alignment.Center => "centrato",
                Alignment.Right => "a destra",
                Alignment.Justified => "giustificato",
                _ => "a sinistra",
            };
            _paragraphState.Text = $"Par {alignment}, zoom {_settings.Zoom * 100:0}%{(_brailleKeyboard is null ? "" : ", scrittura Braille")}";
        }
        UpdateBrailleLine();
        UpdateBars();
    }

    private Position CurrentPosition() => Position.Compute(_text.Text, _text.SelectionStart);

    /// <summary>
    /// Announcement to use right after closing a dialog: wait a moment,
    /// otherwise the screen reader covers it while reading the document that takes the focus back.
    /// </summary>
    /// <summary>
    /// What Eleu says to the screen reader is also written for a few seconds at the end of the status bar: without a
    /// screen reader, "Il documento è vuoto" or "Niente da leggere" would otherwise leave nothing to see.
    /// </summary>
    private sealed class VisibleAnnouncer(IAnnouncer screenReader, Action<string> show) : IAnnouncer
    {
        public void Announce(string message)
        {
            screenReader.Announce(message);
            show(message);
        }
    }

    private System.Windows.Forms.Timer? _messageTimer;

    private void ShowMessage(string message)
    {
        if (IsDisposed)
            return;
        if (InvokeRequired)
        {
            BeginInvoke(() => ShowMessage(message));
            return;
        }
        _message.Text = message;
        _message.ToolTipText = message;
        if (_messageTimer is null)
        {
            _messageTimer = new System.Windows.Forms.Timer { Interval = 6000 };
            _messageTimer.Tick += (_, _) =>
            {
                _messageTimer.Stop();
                _message.Text = "";
                _message.ToolTipText = "";
            };
        }
        _messageTimer.Stop();
        _messageTimer.Start();
    }

    private void AnnounceShortly(string message)
    {
        var timer = new System.Windows.Forms.Timer { Interval = 400 };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            timer.Dispose();
            _announcer.Announce(message);
        };
        timer.Start();
    }

    private void Cut()
    {
        if (_text.SelectionLength == 0)
        {
            _announcer.Announce("Nessun testo selezionato");
            return;
        }
        _text.Cut();
        _announcer.Announce("Tagliato");
    }

    private void Copy()
    {
        if (_text.SelectionLength == 0)
        {
            _announcer.Announce("Nessun testo selezionato");
            return;
        }
        _text.Copy();
        _announcer.Announce("Copiato");
    }

    private void Paste()
    {
        // A table copied from Word or Excel arrives as a table; the rest is pasted as plain text,
        // without images or odd formatting from other programs.
        if (PasteTable())
            return;
        if (!Clipboard.ContainsText())
        {
            _announcer.Announce("Gli appunti non contengono testo");
            return;
        }
        _text.SelectedText = Clipboard.GetText();
    }

    private void ShowShortcuts()
    {
        var rows = new List<string>();
        var converter = new KeysConverter();
        void List(ToolStripItemCollection items, string path)
        {
            foreach (var item in items.OfType<ToolStripMenuItem>())
            {
                var name = item.Text!.Replace("&", "").TrimEnd('.');
                if (item.ShortcutKeys != Keys.None)
                    rows.Add($"{path}{name}: {item.ShortcutKeyDisplayString ?? converter.ConvertToString(item.ShortcutKeys)}");
                List(item.DropDownItems, $"{path}{name} > ");
            }
        }
        foreach (ToolStripMenuItem menu in _menu.Items)
        {
            rows.Add(menu.Text!.Replace("&", "").ToUpperInvariant());
            List(menu.DropDownItems, "");
            rows.Add("");
        }
        using var window = new TextDialog("Scorciatoie da tastiera", string.Join(Environment.NewLine, rows));
        window.ShowDialog(this);
    }

    private void ShowAbout()
    {
        using var window = new AboutDialog(Application.ProductVersion.Split('+')[0]);
        window.ShowDialog(this);
    }
}
