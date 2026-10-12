using Eleu.Accessibilita;
using Eleu.Documento;
using Eleu.Editor;
using Eleu.Interfaccia;

namespace Eleu;

/// <summary>
/// Finestra principale: un solo campo di testo a tutto schermo, il menu e la barra di stato.
/// Ogni comando è nel menu (raggiungibile con Alt) e ha una scorciatoia da tastiera.
/// Le parti sono divise per argomento nei file FinestraPrincipale.*.cs.
/// </summary>
public sealed partial class FinestraPrincipale : Form
{
    private const string NomeApp = "Eleu";

    private readonly CampoTesto _testo;
    private readonly ToolStripStatusLabel _statoPosizione, _statoCarattere, _statoParagrafo;
    private readonly MenuStrip _menu;
    private readonly IAnnunciatore _annunciatore;
    private readonly Impostazioni _impostazioni;
    private readonly Ripristino _ripristino;
    private readonly System.Windows.Forms.Timer _timerRipristino;
    private readonly string? _fileDaAprire;

    /// <param name="guida">La finestra mostra la Guida di Eleu invece di un documento.</param>
    public FinestraPrincipale(string? fileDaAprire = null, bool guida = false)
    {
        _fileDaAprire = fileDaAprire;
        _apriGuida = guida;
        _impostazioni = Impostazioni.Condivise;
        _ripristino = new Ripristino(Ripristino.CartellaPredefinita);

        Width = 1000;
        Height = 700;
        StartPosition = FormStartPosition.CenterScreen;
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? Icon;
        // A tutto schermo, se l'ultima volta era così (all'inizio sì): più spazio per il testo ingrandito.
        if (_impostazioni.Ingrandita)
            WindowState = FormWindowState.Maximized;

        _testo = new CampoTesto
        {
            Dock = DockStyle.Fill,
            AcceptsTab = true,
            DetectUrls = false,
            WordWrap = _impostazioni.ACapoAutomatico,
            HideSelection = false,
            BorderStyle = BorderStyle.None,
            Font = new Font("Segoe UI", ContenutoDocumento.DimensioneBase),
            TabIndex = 0,
        };
        NomeAccessibile.Imposta(_testo, "Documento");
        _testo.HandleCreated += (_, _) =>
        {
            RichEditNativo.AbilitaTipografiaAvanzata(_testo);
            RichEditNativo.ControlloMentreScrivi(_testo, _impostazioni.ErroriSottolineati);
            ImpostaMarginiTesto();
        };
        _testo.SelectionChanged += (_, _) =>
        {
            AggiornaBarraDiStato();
            AnnunciaCambioCella();
            AnnunciaImmagine();
            SegnalaElementi();
        };
        _testo.TextChanged += (_, _) => AggiornaBarraDiStato();
        _testo.ModifiedChanged += (_, _) => AggiornaTitolo();
        PreparaSegnalibri();
        PreparaNote();
        PreparaCommenti();
        PreparaIntestazioni();
        PreparaImmagini();
        PreparaScritturaBraille();
        _testo.ContextMenuStrip = CreaMenuContestuale();

        _statoPosizione = new ToolStripStatusLabel { TextAlign = ContentAlignment.MiddleLeft, BorderSides = ToolStripStatusLabelBorderSides.Right };
        _statoCarattere = new ToolStripStatusLabel { TextAlign = ContentAlignment.MiddleLeft, BorderSides = ToolStripStatusLabelBorderSides.Right };
        _statoParagrafo = new ToolStripStatusLabel { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        var barraDiStato = new StatusStrip { AccessibleName = "Barra di stato" };
        barraDiStato.Items.AddRange([_statoPosizione, _statoCarattere, _statoParagrafo]);

        _rigaBraille = CreaRigaBraille();
        // Dopo il ridimensionamento, non durante: cambiare il testo dell'etichetta mentre WinForms
        // dispone i controlli interrompe la disposizione e la striscia resta larga 100 pixel.
        _rigaBraille.Resize += (_, _) => { if (IsHandleCreated) BeginInvoke(AggiornaRigaBraille); };
        _testo.Resize += (_, _) => { if (IsHandleCreated) BeginInvoke(AggiornaRigaBraille); };

        _barra = CreaBarra();
        _righello = CreaRighello();
        _testo.HScroll += (_, _) => AggiornaBarre();
        _testo.Resize += (_, _) => { if (IsHandleCreated) BeginInvoke(AggiornaBarre); };

        _menu = CreaMenu();
        MainMenuStrip = _menu;

        // L'ordine di aggiunta conta: il campo con Dock.Fill va aggiunto per primo,
        // e tra i controlli in basso quello aggiunto dopo sta più in basso (la barra di stato);
        // in alto, al contrario, sta più in alto: menu, barra dei pulsanti, righello.
        Controls.Add(_testo);
        Controls.Add(_righello);
        Controls.Add(_barra);
        Controls.Add(_rigaBraille);
        Controls.Add(barraDiStato);
        Controls.Add(_menu);

        _annunciatore = new AnnunciatoreUia(this);
        ActiveControl = _testo;

        _timerRipristino = new System.Windows.Forms.Timer { Interval = _impostazioni.MinutiRipristino * 60_000 };
        _timerRipristino.Tick += (_, _) => SalvaCopiaDiRipristino();
        _timerRipristino.Start();

        AggiornaTitolo();
        AggiornaBarraDiStato();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        CaricaDocumentoNuovo();
        _testo.Modified = false;
        if (_apriGuida)
            CaricaGuida();
        else if (!ProponiRipristino() && _fileDaAprire is not null)
            ApriFile(_fileDaAprire);
        if (ContestoEleu.Attivo)
            ControllaAllAvvio();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        if (!ConfermaAbbandonoModifiche())
        {
            e.Cancel = true;
            return;
        }
        RicordaPosizione();
        _timerRipristino.Stop();
        _lettore?.Dispose();
        _lettoreProva?.Dispose();
        _ripristino.Elimina();
        _ripristino.Dispose();
        if (WindowState != FormWindowState.Minimized)
            _impostazioni.Ingrandita = WindowState == FormWindowState.Maximized;
        _impostazioni.Salva();
    }

    /// <summary>
    /// I menu seguono l'organizzazione e i tasti a cui sono abituati gli utenti dei programmi di videoscrittura
    /// accessibili: File, Modifica, Visualizza, Inserisci, Formato, Strumenti, Voce, Aiuto. In Inserisci ogni
    /// tipo di elemento ha lo stesso sottomenu (Aggiungi, Elimina, Precedente, Successivo, Elenco).
    /// </summary>
    private MenuStrip CreaMenu()
    {
        var riapri = new ToolStripMenuItem("&Riapri");
        riapri.DropDownItems.Add(new ToolStripMenuItem("Nessun file recente") { Enabled = false });
        riapri.DropDownOpening += (_, _) => AggiornaFileRecenti(riapri);

        var file = CreaVoceMenu("&File",
            Voce("&Nuovo documento", Keys.Control | Keys.N, Nuovo),
            new ToolStripSeparator(),
            Voce("&Apri...", Keys.Control | Keys.O, Apri),
            Voce("Apri in un'altra &finestra...", Keys.None, ApriInAltraFinestra),
            riapri,
            new ToolStripSeparator(),
            Voce("Sa&lva", Keys.Control | Keys.S, () => Salva()),
            Voce("Sal&va con nome...", Keys.F12, () => SalvaConNome()),
            Voce("Salva sele&zione...", Keys.None, SalvaSelezione),
            Voce("Salva c&ome Braille (BRF)...", Keys.None, () => Braille(suFile: true)),
            new ToolStripSeparator(),
            Voce("I&mposta pagina...", Keys.None, ImpostaPagina),
            Voce("An&teprima di stampa...", Keys.None, AnteprimaDiStampa),
            Voce("&Stampa...", Keys.Control | Keys.P, Stampa),
            Voce("Stampa &braille...", Keys.Control | Keys.Alt | Keys.P, () => Braille(suFile: false)),
            new ToolStripSeparator(),
            Voce("&Proprietà...", Keys.None, ApriProprieta),
            new ToolStripSeparator(),
            Voce("&Esci", Keys.Alt | Keys.F4, Close));

        var cerca = CreaVoceMenu("C&erca",
            Voce("&Trova...", Keys.Control | Keys.Shift | Keys.T, () => ApriTrova(false)),
            Voce("Trova s&uccessivo", Keys.F3, () => TrovaEAnnuncia(avanti: true)),
            Voce("Trova &precedente", Keys.Shift | Keys.F3, () => TrovaEAnnuncia(avanti: false)),
            new ToolStripSeparator(),
            Voce("&Sostituisci...", Keys.Control | Keys.Shift | Keys.S, () => ApriTrova(true)),
            Voce("Sostituisci successi&vo", Keys.F4, () => SostituisciCorrente(avanti: true)),
            Voce("Sostituisci prece&dente", Keys.Shift | Keys.F4, () => SostituisciCorrente(avanti: false)),
            new ToolStripSeparator(),
            Voce("V&ai a...", Keys.Control | Keys.Shift | Keys.G, ApriVaiA),
            Voce("Vai a successiv&o", Keys.F2, () => VaiASuccessivo(avanti: true)),
            Voce("Vai a precede&nte", Keys.Shift | Keys.F2, () => VaiASuccessivo(avanti: false)));

        var modifica = CreaVoceMenu("&Modifica",
            Voce("&Annulla", Keys.Control | Keys.Z, () => _testo.Undo()),
            Voce("&Ripeti", Keys.Control | Keys.Y, () => _testo.Redo()),
            Voce("&Taglia", Keys.Control | Keys.X, Taglia),
            Voce("&Copia", Keys.Control | Keys.C, Copia),
            Voce("Copia &braille", Keys.None, CopiaBraille),
            Voce("&Incolla", Keys.Control | Keys.V, Incolla),
            new ToolStripSeparator(),
            cerca,
            CreaVoceMenu("&Seleziona",
                Voce("Seleziona &parola", Keys.None, SelezionaParola),
                Voce("Seleziona r&iga", Keys.None, SelezionaRiga),
                Voce("Seleziona p&aragrafo", Keys.None, SelezionaParagrafo),
                Voce("Seleziona &tutto", Keys.Control | Keys.A, () => _testo.SelectAll()),
                new ToolStripSeparator(),
                Voce("Inizio blocco / seleziona &blocco", Keys.F8, SegnaBlocco),
                Voce("Annulla bl&occo", Keys.Shift | Keys.F8, AnnullaBlocco)));

        var visualizza = CreaVoceMenu("&Visualizza",
            Voce("Barra degli s&trumenti", Keys.None, CambiaBarraStrumenti),
            Voce("&Righello", Keys.None, CambiaRighello),
            Voce("R&iga Braille in fondo alla finestra", Keys.Control | Keys.Shift | Keys.R, CambiaRigaBraille),
            Voce("Anteprima &braille", Keys.None, AnteprimaBraille),
            Voce("A &capo automatico", Keys.None, CambiaACapo),
            CreaVoceMenu("&Zoom",
                Voce("Zoom &avanti", Keys.Control | Keys.Alt | Keys.Oemplus, () => CambiaZoom(+0.25f), "Ctrl+Alt++"),
                Voce("Zoom &indietro", Keys.Control | Keys.Alt | Keys.OemMinus, () => CambiaZoom(-0.25f), "Ctrl+Alt+-"),
                Voce("Zoom &normale", Keys.Control | Keys.Alt | Keys.D0, () => ImpostaZoom(1f), "Ctrl+Alt+0")));

        var inserisci = CreaVoceMenu("&Inserisci",
            CreaVoceMenu("&Segnalibri",
                Voce("&Aggiungi segnalibro...", Keys.Control | Keys.Shift | Keys.F5, AggiungiSegnalibro),
                Voce("&Elimina segnalibro", Keys.None, EliminaSegnalibro),
                new ToolStripSeparator(),
                Voce("Segnalibro &precedente", Keys.None, () => SegnalibroVicino(avanti: false)),
                Voce("Segnalibro &successivo", Keys.None, () => SegnalibroVicino(avanti: true)),
                new ToolStripSeparator(),
                Voce("E&lenco dei segnalibri...", Keys.None, MostraElencoSegnalibri)),
            CreaVoceMenu("&Interruzioni di pagina",
                Voce("&Aggiungi interruzione", Keys.Control | Keys.Enter, InserisciInterruzionePagina, "Ctrl+Invio"),
                Voce("&Elimina interruzioni", Keys.None, EliminaInterruzioni),
                new ToolStripSeparator(),
                Voce("Interruzione &precedente", Keys.None, () => InterruzioneVicina(avanti: false)),
                Voce("Interruzione &successiva", Keys.None, () => InterruzioneVicina(avanti: true)),
                new ToolStripSeparator(),
                Voce("E&lenco delle interruzioni...", Keys.None, MostraElencoInterruzioni)),
            CreaVoceMenu("&Note a piè di pagina",
                Voce("&Aggiungi nota...", Keys.Control | Keys.Alt | Keys.F, AggiungiNota),
                Voce("&Modifica nota...", Keys.None, ModificaNota),
                Voce("&Elimina nota", Keys.None, EliminaNota),
                new ToolStripSeparator(),
                Voce("Nota &precedente", Keys.None, () => NotaVicina(avanti: false)),
                Voce("Nota &successiva", Keys.None, () => NotaVicina(avanti: true)),
                new ToolStripSeparator(),
                Voce("E&lenco delle note...", Keys.None, MostraElencoNote)),
            CreaVoceMenu("&Commenti",
                Voce("&Aggiungi commento...", Keys.Control | Keys.Alt | Keys.M, AggiungiCommento),
                Voce("&Modifica commento...", Keys.None, ModificaCommento),
                Voce("&Elimina commento", Keys.None, EliminaCommento),
                new ToolStripSeparator(),
                Voce("Commento &precedente", Keys.None, () => CommentoVicino(avanti: false)),
                Voce("Commento &successivo", Keys.None, () => CommentoVicino(avanti: true)),
                new ToolStripSeparator(),
                Voce("E&lenco dei commenti...", Keys.None, MostraElencoCommenti)),
            CreaVoceMenu("I&mmagini",
                Voce("&Aggiungi immagine...", Keys.None, AggiungiImmagine),
                Voce("&Modifica immagine...", Keys.None, ModificaImmagine),
                Voce("&Elimina immagine", Keys.None, EliminaImmagine),
                new ToolStripSeparator(),
                Voce("Immagine &precedente", Keys.None, () => ImmagineVicina(avanti: false)),
                Voce("Immagine &successiva", Keys.None, () => ImmagineVicina(avanti: true)),
                new ToolStripSeparator(),
                Voce("Sal&va immagine con nome...", Keys.None, SalvaImmagine),
                Voce("&Grafica in rilievo...", Keys.None, GraficaInRilievo),
                Voce("E&lenco delle immagini...", Keys.None, MostraElencoImmagini)),
            CreaVoceMenu("T&abelle",
                Voce("&Aggiungi tabella...", Keys.None, AggiungiTabella),
                Voce("&Elimina tabella", Keys.None, EliminaTabella),
                new ToolStripSeparator(),
                Voce("Inserisci riga sop&ra", Keys.None, () => InserisciRiga(sotto: false)),
                Voce("Inserisci riga so&tto", Keys.None, () => InserisciRiga(sotto: true)),
                Voce("Inserisci colonna a si&nistra", Keys.None, () => InserisciColonna(aDestra: false)),
                Voce("Inserisci colonna a &destra", Keys.None, () => InserisciColonna(aDestra: true)),
                Voce("Elimina ri&ga", Keys.None, EliminaRiga),
                Voce("Elimina co&lonna", Keys.None, EliminaColonna),
                Voce("I&mposta larghezza colonna...", Keys.None, LarghezzaColonna),
                Voce("Riga di intesta&zione", Keys.None, CambiaIntestazione),
                Voce("Ordina rig&he dalla A alla Z", Keys.None, () => OrdinaRighe(crescente: true)),
                Voce("Ordina righe all'in&verso, dalla Z alla A", Keys.None, () => OrdinaRighe(crescente: false)),
                new ToolStripSeparator(),
                Voce("Cella a s&inistra", Keys.Control | Keys.Alt | Keys.Left, () => CellaVicina(0, -1), "Ctrl+Alt+Freccia sinistra"),
                Voce("&Cella a destra", Keys.Control | Keys.Alt | Keys.Right, () => CellaVicina(0, 1), "Ctrl+Alt+Freccia destra"),
                Voce("Cella &sopra", Keys.Control | Keys.Alt | Keys.Up, () => CellaVicina(-1, 0), "Ctrl+Alt+Freccia su"),
                Voce("Cella sott&o", Keys.Control | Keys.Alt | Keys.Down, () => CellaVicina(1, 0), "Ctrl+Alt+Freccia giù"),
                new ToolStripSeparator(),
                Voce("Tabella &precedente", Keys.None, () => TabellaVicina(avanti: false)),
                Voce("Tabella s&uccessiva", Keys.None, () => TabellaVicina(avanti: true)),
                Voce("Elenco delle ta&belle...", Keys.None, MostraElencoTabelle)),
            CreaVoceMenu("&Titoli",
                Voce("&Aggiungi titolo...", Keys.Control | Keys.Alt | Keys.T, AggiungiTitolo),
                Voce("&Elimina titolo", Keys.None, () => ImpostaTitolo(0)),
                new ToolStripSeparator(),
                Voce("Titolo &precedente", Keys.None, () => TitoloVicino(avanti: false)),
                Voce("Titolo &successivo", Keys.None, () => TitoloVicino(avanti: true)),
                new ToolStripSeparator(),
                Voce("Inserisci so&mmario", Keys.None, InserisciSommario),
                Voce("E&lenco dei titoli...", Keys.None, MostraElencoTitoli)),
            new ToolStripSeparator(),
            Voce("Sim&bolo...", Keys.None, InserisciSimbolo));

        var formato = CreaVoceMenu("F&ormato",
            Voce("&Carattere...", Keys.Control | Keys.Shift | Keys.F, ApriCarattere),
            Voce("&Paragrafo...", Keys.Control | Keys.Shift | Keys.P, ApriParagrafo),
            CreaVoceMenu("&Elenchi",
                Voce("Aggiungi elenco &puntato", Keys.Control | Keys.Shift | Keys.L, () => ImpostaElenco(TipoElenco.Puntato)),
                Voce("Aggiungi elenco &numerato", Keys.Control | Keys.Shift | Keys.N, () => ImpostaElenco(TipoElenco.Numerato)),
                Voce("&Togli l'elenco", Keys.None, () => ImpostaElenco(TipoElenco.Nessuno)),
                new ToolStripSeparator(),
                Voce("Voce dell'elenco p&recedente", Keys.None, () => PuntoElencoVicino(avanti: false)),
                Voce("Voce dell'elenco &successiva", Keys.None, () => PuntoElencoVicino(avanti: true))),
            CreaVoceMenu("&Maiuscole/minuscole",
                Voce("&Minuscolo", Keys.Control | Keys.OemMinus, () => ImpostaMaiuscole(Maiuscole.Minuscolo, "minuscolo"), "Ctrl+-"),
                Voce("M&aiuscolo", Keys.Control | Keys.Oemplus, () => ImpostaMaiuscole(Maiuscole.Maiuscolo, "MAIUSCOLO"), "Ctrl++"),
                Voce("&Iniziale maiuscola", Keys.None, () => ImpostaMaiuscole(Maiuscole.Iniziali, "Iniziali maiuscole")),
                Voce("Al&terna", Keys.Control | Keys.Shift | Keys.A, CambiaMaiuscole)),
            CreaVoceMenu("Formattazione &rapida",
                CreaVoceMenu("&Carattere",
                    Voce("&Grassetto", Keys.Control | Keys.B, () => CambiaEffetto(RichEditNativo.Effetto.Grassetto, "Grassetto")),
                    Voce("&Corsivo", Keys.Control | Keys.I, () => CambiaEffetto(RichEditNativo.Effetto.Corsivo, "Corsivo")),
                    Voce("&Sottolineato", Keys.Control | Keys.U, () => CambiaEffetto(RichEditNativo.Effetto.Sottolineato, "Sottolineato")),
                    Voce("&Barrato", Keys.None, CambiaBarrato),
                    Voce("Rim&picciolisci il carattere",Keys.Control | Keys.Shift | Keys.OemMinus, () => CambiaDimensione(-1), "Maiusc+Ctrl+-"),
                    Voce("I&ngrandisci il carattere",Keys.Control | Keys.Shift | Keys.Oemplus, () => CambiaDimensione(1), "Maiusc+Ctrl++")),
                CreaVoceMenu("&Paragrafo",
                    Voce("Allinea a &sinistra", Keys.Control | Keys.L, () => Allinea(Allineamento.Sinistra, "Allineato a sinistra")),
                    Voce("Allinea al &centro", Keys.Control | Keys.E, () => Allinea(Allineamento.Centro, "Centrato")),
                    Voce("Allinea a &destra", Keys.Control | Keys.R, () => Allinea(Allineamento.Destra, "Allineato a destra")),
                    Voce("&Giustifica", Keys.Control | Keys.J, () => Allinea(Allineamento.Giustificato, "Giustificato")),
                    new ToolStripSeparator(),
                    Voce("Interlinea &1", Keys.Control | Keys.D1, () => ImpostaInterlinea(1f, "singola")),
                    Voce("Interlinea 1,&5", Keys.Control | Keys.D5, () => ImpostaInterlinea(1.5f, "una e mezza")),
                    Voce("Interlinea &2", Keys.Control | Keys.D2, () => ImpostaInterlinea(2f, "doppia")))));

        var strumenti = CreaVoceMenu("&Strumenti",
            CreaVoceMenu("&Correzione",
                Voce("&Errore successivo", Keys.F7, () => VaiAErrore(avanti: true)),
                Voce("Errore &precedente", Keys.Shift | Keys.F7, () => VaiAErrore(avanti: false)),
                Voce("&Correggi con il suggerimento", Keys.Control | Keys.F7, CorreggiConSuggerimento),
                new ToolStripSeparator(),
                Voce("Controllo &ortografico...", Keys.Control | Keys.Shift | Keys.F7, ApriControlloOrtografico),
                Voce("&Dizionario personale...", Keys.None, GestisciDizionario),
                Voce("So&ttolinea errori mentre scrivi", Keys.None, CambiaSottolineatura)),
            CreaVoceMenu("&Informazioni",
                Voce("&Posizione del cursore", Keys.F6, AnnunciaPosizione),
                Voce("&Conteggio parole", Keys.Control | Keys.F6, AnnunciaStatistiche),
                Voce("&Statistiche complete...", Keys.Control | Keys.Shift | Keys.F6, MostraStatisticheComplete),
                Voce("Parole più &usate...", Keys.None, ParolePiuUsate)),
            CreaVoceMenu("Cerca sul &web", [.. ServiziWeb.Select(s => Voce(s.Nome, Keys.None, () => CercaSulWeb(s.Indirizzo)))]),
            new ToolStripSeparator(),
            Voce("&Pulizia del testo...", Keys.None, PuliziaTesto),
            Voce("Sostituzioni &multiple...", Keys.None, ApriSostituzioniMultiple),
            Voce("&Ordina paragrafi", Keys.None, OrdinaParagrafi),
            Voce("Con&fronta con un altro documento...", Keys.None, ConfrontaDocumento),
            Voce("Scrittura &Braille a 6 tasti", Keys.Control | Keys.Shift | Keys.D6, CambiaScritturaBraille, "Ctrl+Maiusc+6"),
            new ToolStripSeparator(),
            Voce("&Dettagli del punto in cui sei",Keys.Alt | Keys.Enter, AnnunciaFormattazione, "Alt+Invio"),
            new ToolStripSeparator(),
            Voce("Op&zioni...", Keys.Control | Keys.Shift | Keys.O, ApriOpzioni));

        var voce = CreaVoceMenu("Vo&ce",
            Voce("Crea un &audiolibro MP3...", Keys.None, EsportaAudiolibro),
            Voce("Crea un libro &DAISY...", Keys.None, EsportaDaisy),
            CreaVoceMenu("&Lettura ad alta voce",
                Voce("&Leggi / pausa", Keys.F11, LeggiOPausa),
                Voce("Leggi &tutto dall'inizio", Keys.Control | Keys.F11, LeggiTutto),
                Voce("&Ferma", Keys.Shift | Keys.F11, () => FermaLettura(), "Maiusc+F11 oppure Esc")),
            new ToolStripSeparator(),
            Voce("&Impostazioni della voce...", Keys.None, ScegliVoce),
            Voce("&Pronunce...", Keys.None, GestisciPronunce));

        var aiuto = CreaVoceMenu("&Aiuto",
            Voce("&Guida di Eleu", Keys.F1, ApriGuida),
            Voce("&Scorciatoie da tastiera", Keys.None, MostraScorciatoie),
            Voce("Controlla &aggiornamenti...", Keys.None, ControllaAggiornamenti),
            Voce("&Informazioni su Eleu", Keys.None, MostraInformazioni));

        var menu = new MenuStrip { AccessibleName = "Menu principale" };
        menu.Items.AddRange([file, modifica, visualizza, inserisci, formato, strumenti, voce, CreaMenuFinestre(), aiuto]);
        return menu;
    }

    /// <summary>
    /// Tasti in più, fuori dai menu: quelli di Word e Blocco note per gli stessi comandi,
    /// dove non tolgono tasti con un altro significato.
    /// </summary>
    private bool TastoAlternativo(Keys tasti)
    {
        Action? azione = tasti switch
        {
            Keys.Control | Keys.F => () => ApriTrova(false),
            Keys.Control | Keys.H => () => ApriTrova(true),
            Keys.Control | Keys.G => ApriVaiA,
            Keys.Control | Keys.D => ApriCarattere,
            _ => null,
        };
        if (azione is null)
            return false;
        BeginInvoke(azione);
        return true;
    }

    private static ToolStripMenuItem CreaVoceMenu(string testo, params ToolStripItem[] voci)
    {
        var menu = new ToolStripMenuItem(testo);
        menu.DropDownItems.AddRange(voci);
        return menu;
    }

    private ToolStripMenuItem Voce(string testo, Keys scorciatoia, Action azione, string? scorciatoiaLeggibile = null)
    {
        // L'azione parte subito dopo, non dentro il clic: se uno screen reader attiva la voce via UI Automation
        // e l'azione apre una finestra modale, la chiamata dello screen reader resterebbe bloccata
        // e Eleu smetterebbe di rispondergli finché la finestra è aperta.
        var voce = new ToolStripMenuItem(testo, null, (_, _) => BeginInvoke(azione));
        if (scorciatoia != Keys.None)
            voce.ShortcutKeys = scorciatoia;
        if (scorciatoiaLeggibile is not null)
            voce.ShortcutKeyDisplayString = scorciatoiaLeggibile;
        return voce;
    }

    private void AggiornaTitolo() => Text = $"{(_testo.Modified ? "*" : "")}{NomeDocumento} - {NomeApp}";

    /// <summary>Barra di stato: posizione, carattere e paragrafo, come nei programmi di videoscrittura accessibili.</summary>
    private void AggiornaBarraDiStato()
    {
        var posizione = PosizioneCorrente();
        _statoPosizione.Text = $"Col {posizione.Colonna}, Riga {posizione.Riga} di {posizione.TotaleRighe}";
        if (_testo.IsHandleCreated)
        {
            var carattere = RichEditNativo.LeggiFormatoCarattere(_testo);
            _statoCarattere.Text = carattere.Carattere is string nome
                ? $"{nome}, {carattere.Dimensione:0.#}"
                : "Caratteri misti";
            var allineamento = RichEditNativo.LeggiAllineamento(_testo) switch
            {
                Allineamento.Centro => "centrato",
                Allineamento.Destra => "a destra",
                Allineamento.Giustificato => "giustificato",
                _ => "a sinistra",
            };
            _statoParagrafo.Text = $"Par {allineamento}, zoom {_impostazioni.Zoom * 100:0}%{(_tastieraBraille is null ? "" : ", scrittura Braille")}";
        }
        AggiornaRigaBraille();
        AggiornaBarre();
    }

    private Posizione PosizioneCorrente() => Posizione.Calcola(_testo.Text, _testo.SelectionStart);

    /// <summary>
    /// Annuncio da usare subito dopo la chiusura di una finestra di dialogo: si aspetta un attimo,
    /// altrimenti lo screen reader lo copre leggendo il documento che riprende il focus.
    /// </summary>
    private void AnnunciaTraPoco(string messaggio)
    {
        var timer = new System.Windows.Forms.Timer { Interval = 400 };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            timer.Dispose();
            _annunciatore.Annuncia(messaggio);
        };
        timer.Start();
    }

    private void Taglia()
    {
        if (_testo.SelectionLength == 0)
        {
            _annunciatore.Annuncia("Nessun testo selezionato");
            return;
        }
        _testo.Cut();
        _annunciatore.Annuncia("Tagliato");
    }

    private void Copia()
    {
        if (_testo.SelectionLength == 0)
        {
            _annunciatore.Annuncia("Nessun testo selezionato");
            return;
        }
        _testo.Copy();
        _annunciatore.Annuncia("Copiato");
    }

    private void Incolla()
    {
        // Una tabella copiata da Word o da Excel arriva come tabella; il resto si incolla come testo semplice,
        // senza immagini o formattazioni strane da altri programmi.
        if (IncollaTabella())
            return;
        if (!Clipboard.ContainsText())
        {
            _annunciatore.Annuncia("Gli appunti non contengono testo");
            return;
        }
        _testo.SelectedText = Clipboard.GetText();
    }

    private void MostraScorciatoie()
    {
        var righe = new List<string>();
        var convertitore = new KeysConverter();
        void Elenca(ToolStripItemCollection voci, string percorso)
        {
            foreach (var voce in voci.OfType<ToolStripMenuItem>())
            {
                var nome = voce.Text!.Replace("&", "").TrimEnd('.');
                if (voce.ShortcutKeys != Keys.None)
                    righe.Add($"{percorso}{nome}: {voce.ShortcutKeyDisplayString ?? convertitore.ConvertToString(voce.ShortcutKeys)}");
                Elenca(voce.DropDownItems, $"{percorso}{nome} > ");
            }
        }
        foreach (ToolStripMenuItem menu in _menu.Items)
        {
            righe.Add(menu.Text!.Replace("&", "").ToUpperInvariant());
            Elenca(menu.DropDownItems, "");
            righe.Add("");
        }
        using var finestra = new FinestraTesto("Scorciatoie da tastiera", string.Join(Environment.NewLine, righe));
        finestra.ShowDialog(this);
    }

    private void MostraInformazioni()
    {
        using var finestra = new FinestraInformazioni(Application.ProductVersion.Split('+')[0]);
        finestra.ShowDialog(this);
    }
}
