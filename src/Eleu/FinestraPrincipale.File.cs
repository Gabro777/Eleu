using Eleu.Accessibilita;
using System.Text;
using Eleu.Documento;
using Eleu.Importa;

namespace Eleu;

// Nuovo, apri, salva, file recenti e copia di ripristino.
public sealed partial class FinestraPrincipale
{
    private string? _percorso;
    private FormatoFile _formato = FormatoFile.Docx;
    private Encoding _codificaTesto = FileTesto.Utf8SenzaBom;

    // Percorso proposto per il primo salvataggio quando il documento non va salvato sopra il file da cui viene.
    private string? _percorsoSuggerito;

    private string NomeDocumento => (_percorso ?? _percorsoSuggerito) is string p ? Path.GetFileName(p) : "Senza titolo";

    /// <summary>Il file del documento di questa finestra (null se non è ancora stato salvato).</summary>
    public string? PercorsoDocumento => _percorso;

    /// <summary>Una finestra appena aperta e mai toccata: ci si può aprire un documento senza aprirne un'altra.</summary>
    private bool DocumentoVuoto => _testo.TextLength == 0 && !_testo.Modified && _percorso is null && _percorsoSuggerito is null;

    /// <summary>Un documento nuovo al posto di quello aperto, in questa finestra (prima si chiede se salvare).</summary>
    private void Nuovo()
    {
        if (!ConfermaAbbandonoModifiche())
            return;
        RicordaPosizione();
        CaricaDocumentoNuovo();
        ImpostaDocumentoCorrente(null, FormatoFile.Docx);
        _annunciatore.Annuncia("Nuovo documento");
    }

    /// <summary>Apre un file al posto del documento di questa finestra (prima si chiede se salvare).</summary>
    private void Apri() => ScegliEApri(nuovaFinestra: false);

    /// <summary>File > Apri in un'altra finestra: il documento aperto resta dov'è.</summary>
    private void ApriInAltraFinestra() => ScegliEApri(nuovaFinestra: true);

    private void ScegliEApri(bool nuovaFinestra)
    {
        if (!nuovaFinestra && !ConfermaAbbandonoModifiche())
            return;
        using var dialogo = new OpenFileDialog
        {
            Title = nuovaFinestra ? "Apri in un'altra finestra" : "Apri",
            Filter = FormatiFile.FiltroApri,
            InitialDirectory = CartellaIniziale(),
        };
        if (dialogo.ShowDialog(this) == DialogResult.OK)
            ApriInFinestra(dialogo.FileName, nuovaFinestra);
    }

    /// <summary>
    /// Apre il file qui o in una finestra nuova. Se il file è già aperto in un'altra finestra si va lì,
    /// invece di averlo due volte.
    /// </summary>
    private void ApriInFinestra(string percorso, bool nuovaFinestra = false)
    {
        var giaAperta = ContestoEleu.Finestre.FirstOrDefault(f => f != this && string.Equals(f.PercorsoDocumento, percorso, StringComparison.OrdinalIgnoreCase));
        if (giaAperta is not null)
        {
            giaAperta.Attiva($"{Path.GetFileName(percorso)} era già aperto");
            return;
        }
        if (nuovaFinestra)
            ContestoEleu.Apri(percorso);
        else
            ApriFile(percorso);
    }

    private void ApriFile(string percorso)
    {
        RicordaPosizione();
        if (Importatore.Importabile(percorso))
        {
            ImportaFile(percorso);
            return;
        }

        var formato = FormatiFile.DaPercorso(percorso);
        var codifica = FileTesto.Utf8SenzaBom;
        List<string> nonConservati = [];
        try
        {
            switch (formato)
            {
                case FormatoFile.Docx:
                    using (var flusso = File.OpenRead(percorso))
                    {
                        var contenuto = DocxFormato.Leggi(flusso);
                        nonConservati = contenuto.ElementiNonConservati;
                        CaricaContenuto(contenuto);
                    }
                    break;
                case FormatoFile.Odt:
                    using (var flusso = File.OpenRead(percorso))
                    {
                        var contenuto = OdtFormato.Leggi(flusso);
                        nonConservati = contenuto.ElementiNonConservati;
                        CaricaContenuto(contenuto);
                    }
                    break;
                case FormatoFile.Rtf:
                    var rtf = File.ReadAllText(percorso, Encoding.Latin1);
                    CaricaRtf(rtf);
                    _pagina = RtfLettore.LeggiPagina(rtf);
                    _metadati = Metadati.DaRtf(rtf);
                    DopoCaricamento();
                    break;
                case FormatoFile.Html or FormatoFile.Epub:
                    // Di una pagina web o di un libro Eleu prende il testo e la struttura, non l'aspetto:
                    // salvare sopra l'originale lo perderebbe, quindi il primo salvataggio chiede un nome nuovo.
                    ContenutoDocumento letto;
                    if (formato == FormatoFile.Epub)
                    {
                        using var libro = File.OpenRead(percorso);
                        letto = EpubFormato.Leggi(libro);
                    }
                    else
                    {
                        var cartellaPagina = Path.GetDirectoryName(percorso)!;
                        letto = HtmlFormato.Leggi(FileTesto.Leggi(File.ReadAllBytes(percorso)).Testo, src =>
                        {
                            // Le immagini accanto alla pagina (non quelle su internet).
                            if (src.Contains("://"))
                                return null;
                            var file = Path.GetFullPath(Path.Combine(cartellaPagina, src.Split('#', '?')[0].Replace('/', Path.DirectorySeparatorChar)));
                            return File.Exists(file) ? File.ReadAllBytes(file) : null;
                        });
                    }
                    nonConservati = ["l'aspetto originale (stili e impaginazione)"];
                    CaricaContenuto(letto);
                    break;
                default:
                    var (testo, codificaLetta) = FileTesto.Leggi(File.ReadAllBytes(percorso));
                    codifica = codificaLetta;
                    CaricaContenuto(ContenutoDocumento.DaTestoSemplice(testo));
                    break;
            }
        }
        catch (Exception e) // Al confine con l'utente: qualunque file illeggibile diventa un messaggio, non un crash.
        {
            MessageBox.Show(this, $"Impossibile aprire {Path.GetFileName(percorso)}.\n\n{e.Message}", NomeApp,
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        _codificaTesto = codifica;
        if (nonConservati.Count == 0)
        {
            ImpostaDocumentoCorrente(percorso, formato);
            AnnunciaTraPoco($"Aperto {NomeDocumento}{RiprendiDaDoveEri(percorso)}");
            return;
        }

        // Salvare sopra l'originale farebbe perdere ciò che Eleu non conserva: il primo salvataggio chiederà un nome nuovo.
        var copia = Path.Combine(Path.GetDirectoryName(percorso)!, $"{Path.GetFileNameWithoutExtension(percorso)} (Eleu){(formato == FormatoFile.Odt ? ".odt" : ".docx")}");
        ImpostaDocumentoCorrente(null, formato, copia);
        _impostazioni.AggiungiRecente(percorso);
        MessageBox.Show(this,
            $"{Path.GetFileName(percorso)} contiene {string.Join(", ", nonConservati)}, che Eleu non può conservare." + Environment.NewLine + Environment.NewLine +
            "Per non perderli, Eleu non salverà sopra il file originale: quando salvi ti chiederà un nuovo nome.",
            NomeApp, MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private bool Salva() => _percorso is null ? SalvaConNome() : SalvaSu(_percorso, _formato);

    private bool SalvaConNome()
    {
        using var dialogo = new SaveFileDialog
        {
            Title = "Salva con nome",
            Filter = FormatiFile.FiltroSalva,
            FilterIndex = (int)_formato + 1,
            FileName = (_percorso ?? _percorsoSuggerito) is string p ? Path.GetFileNameWithoutExtension(p) : "Documento",
            InitialDirectory = CartellaIniziale(),
            AddExtension = true,
            OverwritePrompt = true,
        };
        if (dialogo.ShowDialog(this) != DialogResult.OK)
            return false;
        return SalvaSu(dialogo.FileName, FormatiFile.DaPercorso(dialogo.FileName));
    }

    private bool SalvaSu(string percorso, FormatoFile formato)
    {
        try
        {
            byte[] dati = formato switch
            {
                // Nel testo semplice le tabelle vanno riga per riga, le immagini con la descrizione, le note in fondo.
                FormatoFile.Testo => FileTesto.Scrivi(string.Join(Environment.NewLine,
                    Leggibile.Da(ModelloDaSalvare()).Paragrafi.Select(p => p.Testo.Replace(Editor.Paginatore.SaltoPagina.ToString(), ""))), _codificaTesto),
                FormatoFile.Rtf => Encoding.Latin1.GetBytes(RtfCompleto()),
                FormatoFile.Html => new UTF8Encoding(false).GetBytes(HtmlFormato.Scrivi(ModelloDaSalvare())),
                FormatoFile.Epub => CreaEpub(),
                FormatoFile.Odt => CreaOdt(),
                _ => CreaDocx(),
            };
            // Si scrive prima un file temporaneo: se qualcosa va storto l'originale resta intatto.
            var temporaneo = percorso + ".eleu-tmp";
            File.WriteAllBytes(temporaneo, dati);
            File.Move(temporaneo, percorso, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"Impossibile salvare {Path.GetFileName(percorso)}.\n\n{e.Message}", NomeApp,
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }

        ImpostaDocumentoCorrente(percorso, formato);
        _ripristino.Elimina();
        SuonoConferma(Segnale.Salvato);
        AnnunciaTraPoco($"Salvato {NomeDocumento}");
        return true;
    }

    /// <summary>Il documento come modello, con pagina e proprietà: quello che si scrive nei formati diversi dall'RTF.</summary>
    private ContenutoDocumento ModelloDaSalvare()
    {
        var contenuto = ModelloCompleto();
        contenuto.Pagina = _pagina;
        contenuto.Metadati = _metadati with { Creato = _metadati.Creato ?? DateTime.Now };
        return contenuto;
    }

    private byte[] CreaDocx()
    {
        using var flusso = new MemoryStream();
        DocxFormato.Scrivi(ModelloDaSalvare(), flusso);
        return flusso.ToArray();
    }

    private byte[] CreaOdt()
    {
        using var flusso = new MemoryStream();
        OdtFormato.Scrivi(ModelloDaSalvare(), flusso);
        return flusso.ToArray();
    }

    private byte[] CreaEpub()
    {
        using var flusso = new MemoryStream();
        var contenuto = ModelloDaSalvare();
        // Un libro senza titolo prende il nome del file.
        if (contenuto.Metadati.Titolo.Length == 0)
            contenuto.Metadati = contenuto.Metadati with { Titolo = Path.GetFileNameWithoutExtension(NomeDocumento) };
        EpubFormato.Scrivi(contenuto, flusso);
        return flusso.ToArray();
    }

    /// <summary>Chiede se salvare le modifiche. Restituisce false se l'utente annulla.</summary>
    private bool ConfermaAbbandonoModifiche()
    {
        if (!_testo.Modified)
            return true;

        var risposta = MessageBox.Show(this, $"Vuoi salvare le modifiche a {NomeDocumento}?", NomeApp,
            MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        switch (risposta)
        {
            case DialogResult.Yes:
                return Salva();
            case DialogResult.No:
                _ripristino.Elimina();
                return true;
            default:
                return false;
        }
    }

    /// <summary>Documento vuoto con il carattere, il paragrafo e la pagina scelti come "Predefinito".</summary>
    private void CaricaDocumentoNuovo()
    {
        CaricaContenuto(new ContenutoDocumento { Pagina = _impostazioni.PaginaPredefinita ?? new ImpostazioniPagina() });
        if (_impostazioni.ParagrafoBase is Editor.FormatoParagrafo paragrafo)
        {
            _testo.SelectAll();
            Editor.RichEditNativo.ImpostaFormatoParagrafo(_testo, paragrafo);
            _testo.Select(0, 0);
        }
        _testo.Modified = false;
    }

    private void CaricaContenuto(ContenutoDocumento contenuto)
    {
        if (contenuto.Paragrafi.Count == 0)
            contenuto.Paragrafi.Add(new Paragrafo());
        _pagina = contenuto.Pagina;
        _metadati = contenuto.Metadati;
        CaricaRtf(RtfScrittore.Scrivi(contenuto, _impostazioni.CarattereBase, _impostazioni.DimensioneBase), contenuto);
        DopoCaricamento();
    }

    private void DopoCaricamento()
    {
        // Le posizioni della lettura in corso valevano per il documento precedente.
        _lettore?.Ferma();
        _testo.Select(0, 0);
        ApplicaZoom();
    }

    private void ImpostaDocumentoCorrente(string? percorso, FormatoFile formato, string? percorsoSuggerito = null)
    {
        _percorso = percorso;
        _percorsoSuggerito = percorsoSuggerito;
        _formato = formato;
        if (formato != FormatoFile.Testo)
            _codificaTesto = FileTesto.Utf8SenzaBom;
        if (percorso is not null)
            _impostazioni.AggiungiRecente(percorso);
        _testo.Modified = false;
        AggiornaTitolo();
        AggiornaBarraDiStato();
    }

    private string CartellaIniziale() =>
        (_percorso ?? _percorsoSuggerito) is string p ? Path.GetDirectoryName(p)! : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

    private void AggiornaFileRecenti(ToolStripMenuItem menu)
    {
        menu.DropDownItems.Clear();
        int numero = 1;
        foreach (var percorso in _impostazioni.FileRecenti)
        {
            var file = percorso;
            menu.DropDownItems.Add(new ToolStripMenuItem($"&{numero++} {Path.GetFileName(file)}", null, (_, _) => BeginInvoke(() =>
            {
                if (ConfermaAbbandonoModifiche())
                    ApriInFinestra(file);
            }))
            { ToolTipText = file });
        }
        if (menu.DropDownItems.Count == 0)
            menu.DropDownItems.Add(new ToolStripMenuItem("Nessun file recente") { Enabled = false });
    }

    private void SalvaCopiaDiRipristino()
    {
        if (_testo.Modified)
            _ripristino.Salva(RtfCompleto(), _percorso);
    }

    /// <summary>Se l'ultima sessione si è chiusa male, propone di recuperare il testo. Restituisce true se recuperato.</summary>
    private bool ProponiRipristino()
    {
        using var abbandonato = Ripristino.Abbandonato(Ripristino.CartellaPredefinita);
        if (abbandonato is null)
            return false;

        var risposta = MessageBox.Show(this,
            "Eleu non è stato chiuso correttamente l'ultima volta.\nVuoi recuperare il documento che non era stato salvato?",
            NomeApp, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (risposta != DialogResult.Yes)
        {
            abbandonato.Elimina();
            return false;
        }

        try
        {
            var (rtf, percorso) = abbandonato.Leggi();
            CaricaRtf(rtf);
            _pagina = RtfLettore.LeggiPagina(rtf);
            _metadati = Metadati.DaRtf(rtf);
            DopoCaricamento();
            ImpostaDocumentoCorrente(percorso, percorso is null ? FormatoFile.Docx : FormatiFile.DaPercorso(percorso));
            _testo.Modified = true;
            AggiornaTitolo();
            // La copia passa a questa finestra: quella vecchia non serve più.
            abbandonato.Elimina();
            SalvaCopiaDiRipristino();
            AnnunciaTraPoco("Documento recuperato. Ricordati di salvarlo.");
            return true;
        }
        catch (Exception e) when (e is IOException or ArgumentException)
        {
            abbandonato.Elimina();
            return false;
        }
    }

    /// <summary>Ricorda dove si è nel documento aperto, per ripartire da lì la prossima volta.</summary>
    private void RicordaPosizione()
    {
        if (_percorso is not null)
            _impostazioni.RicordaPosizione(_percorso, _testo.SelectionStart);
    }

    /// <summary>Porta il cursore dove si era l'ultima volta; restituisce cosa aggiungere all'annuncio.</summary>
    private string RiprendiDaDoveEri(string percorso)
    {
        if (_impostazioni.PosizioneRicordata(percorso) is not int posizione || posizione <= 0 || posizione > _testo.TextLength)
            return "";
        _testo.Select(posizione, 0);
        _testo.ScrollToCaret();
        return $". Ripreso da dove eri, riga {PosizioneCorrente().Riga}";
    }
}
