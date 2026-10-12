using System.Text.Json;

namespace Eleu;

/// <summary>Preferenze dell'utente, salvate in %AppData%\Eleu\impostazioni.json.</summary>
public sealed class Impostazioni
{
    public const int MassimoFileRecenti = 5;
    public const float ZoomMinimo = 0.5f;
    public const float ZoomMassimo = 5f;

    public static string PercorsoPredefinito { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Eleu", "impostazioni.json");

    public float Zoom { get; set; } = 1f;

    public List<string> FileRecenti { get; set; } = [];

    /// <summary>Voce scelta per la lettura e gli audiolibri; null = scelta automatica (italiana).</summary>
    public string? VoceId { get; set; }

    public int VelocitaVoce { get; set; } = 100;

    public int VolumeVoce { get; set; } = 100;

    /// <summary>Stampante Braille scelta l'ultima volta; null = salvare un file BRF.</summary>
    public string? StampanteBraille { get; set; }

    public int CelleBraille { get; set; } = 32;

    public int RigheBraille { get; set; } = 27;

    // Le altre scelte della pagina Braille (vedi Braille.FormatoPagina): restano per la volta dopo.
    public Braille.NumeroPaginaBraille NumeroBraille { get; set; } = Braille.NumeroPaginaBraille.InAltoADestra;

    public bool TitoloBraille { get; set; }

    public int MargineBraille { get; set; }

    public bool PagineStampaBraille { get; set; }

    public bool SommarioBraille { get; set; }

    public bool InterlineaDoppiaBraille { get; set; }

    public bool FronteRetroBraille { get; set; }

    public bool PiegaBraille { get; set; }

    /// <summary>Striscia in basso con la riga corrente in Braille (utile soprattutto a chi vede).</summary>
    public bool RigaBraille { get; set; }

    public bool ACapoAutomatico { get; set; } = true;

    /// <summary>Barra dei pulsanti e righello sopra il testo, per chi vede e usa il mouse.</summary>
    public bool BarraStrumenti { get; set; } = true;

    public bool Righello { get; set; } = true;

    /// <summary>Finestra a tutto schermo all'avvio.</summary>
    public bool Ingrandita { get; set; } = true;

    /// <summary>Parole che il controllo ortografico non deve segnalare (nomi, termini tecnici...).</summary>
    public List<string> DizionarioPersonale { get; set; } = [];

    /// <summary>Parole sbagliate sottolineate in rosso mentre si scrive, per chi vede.</summary>
    public bool ErroriSottolineati { get; set; } = true;

    /// <summary>Il controllo ortografico salta le parole tutte maiuscole (sigle come ONU, NVDA).</summary>
    public bool IgnoraMaiuscole { get; set; } = true;

    /// <summary>Il controllo ortografico salta le parole con numeri (codici, A4, 3D).</summary>
    public bool IgnoraConNumeri { get; set; } = true;

    // ---------- Opzioni (Strumenti > Opzioni) ----------

    /// <summary>Come Eleu segnala titoli, segnalibri, note, commenti e interruzioni di pagina quando il cursore ci arriva.</summary>
    public ModoSegnale SegnaleElementi { get; set; } = ModoSegnale.Suono;

    /// <summary>Un suono quando il cursore, muovendosi, arriva su una parola sbagliata.</summary>
    public bool SuonoParolaSbagliata { get; set; } = true;

    /// <summary>Un suono alla fine della lettura e dopo il salvataggio.</summary>
    public bool SuoniConferma { get; set; } = true;

    /// <summary>Volume dei segnali sonori, da 0 a 100, rispetto agli altri suoni del computer.</summary>
    public int VolumeSuoni { get; set; } = 60;

    /// <summary>Entrando in una cella di tabella Eleu dice riga, colonna e intestazione.</summary>
    public bool AnnunciaCelle { get; set; } = true;

    /// <summary>Arrivando su un'immagine Eleu ne dice la descrizione.</summary>
    public bool AnnunciaImmagini { get; set; } = true;

    /// <summary>All'avvio, senza un file da aprire, Eleu riapre l'ultimo documento usato.</summary>
    public bool RiapriUltimo { get; set; }

    /// <summary>Ogni quanti minuti Eleu tiene da parte una copia per il recupero dopo un'interruzione.</summary>
    public int MinutiRipristino { get; set; } = 1;

    /// <summary>Una volta al giorno, all'avvio, Eleu controlla su GitHub se c'è una versione nuova.</summary>
    public bool ControllaAggiornamenti { get; set; } = true;

    public DateTime? UltimoControlloAggiornamenti { get; set; }

    /// <summary>La versione che l'utente ha scelto di saltare (il tag, per esempio "v1.2.0").</summary>
    public string? VersioneSaltata { get; set; }

    /// <summary>Il nome con cui si firmano i commenti (null = il nome dell'utente di Windows).</summary>
    public string? AutoreCommenti { get; set; }

    /// <summary>Le scelte dell'ultima Pulizia del testo.</summary>
    public Correzione.OpzioniPulizia Pulizia { get; set; } = new();

    /// <summary>L'elenco delle Sostituzioni multiple, che resta per la prossima volta.</summary>
    public List<Correzione.CoppiaSostituzione> Sostituzioni { get; set; } = [];

    public bool SostituzioniParoleIntere { get; set; } = true;

    /// <summary>Come la voce deve dire certe parole o sigle (Voce > Pronunce).</summary>
    public List<Voce.Pronuncia> Pronunce { get; set; } = [];

    // Punto di partenza dei documenti nuovi ("Predefinito" nelle finestre Carattere, Paragrafo e Imposta pagina).
    public string CarattereBase { get; set; } = "Segoe UI";

    public float DimensioneBase { get; set; } = Documento.ContenutoDocumento.DimensioneBase;

    public Editor.FormatoParagrafo? ParagrafoBase { get; set; }

    public Documento.ImpostazioniPagina? PaginaPredefinita { get; set; }

    public const int MassimoPosizioniRicordate = 50;

    /// <summary>Dove si era in ogni documento usato di recente: riaprendolo si riparte da lì.</summary>
    public List<PosizioneFile> UltimePosizioni { get; set; } = [];

    public void RicordaPosizione(string percorso, int posizione)
    {
        UltimePosizioni.RemoveAll(p => string.Equals(p.Percorso, percorso, StringComparison.OrdinalIgnoreCase));
        UltimePosizioni.Insert(0, new PosizioneFile(percorso, posizione));
        if (UltimePosizioni.Count > MassimoPosizioniRicordate)
            UltimePosizioni.RemoveRange(MassimoPosizioniRicordate, UltimePosizioni.Count - MassimoPosizioniRicordate);
    }

    public int? PosizioneRicordata(string percorso) =>
        UltimePosizioni.FirstOrDefault(p => string.Equals(p.Percorso, percorso, StringComparison.OrdinalIgnoreCase))?.Posizione;

    public void AggiungiRecente(string percorso)
    {
        FileRecenti.RemoveAll(f => string.Equals(f, percorso, StringComparison.OrdinalIgnoreCase));
        FileRecenti.Insert(0, percorso);
        if (FileRecenti.Count > MassimoFileRecenti)
            FileRecenti.RemoveRange(MassimoFileRecenti, FileRecenti.Count - MassimoFileRecenti);
    }

    private static Impostazioni? _condivise;

    /// <summary>Le impostazioni di Eleu, le stesse per tutte le finestre aperte.</summary>
    public static Impostazioni Condivise => _condivise ??= Carica();

    public static Impostazioni Carica(string? percorso = null)
    {
        try
        {
            var json = File.ReadAllText(percorso ?? PercorsoPredefinito);
            var impostazioni = JsonSerializer.Deserialize<Impostazioni>(json) ?? new Impostazioni();
            impostazioni.Zoom = Math.Clamp(impostazioni.Zoom, ZoomMinimo, ZoomMassimo);
            impostazioni.VolumeSuoni = Math.Clamp(impostazioni.VolumeSuoni, 0, 100);
            impostazioni.MinutiRipristino = Math.Clamp(impostazioni.MinutiRipristino, 1, 30);
            return impostazioni;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return new Impostazioni();
        }
    }

    public void Salva(string? percorso = null)
    {
        percorso ??= PercorsoPredefinito;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(percorso)!);
            File.WriteAllText(percorso, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Le preferenze non sono indispensabili: se non si possono salvare si va avanti.
        }
    }
}

public sealed record PosizioneFile(string Percorso, int Posizione);

/// <summary>Come segnalare un elemento del documento: con un suono, a voce, in entrambi i modi o per niente.</summary>
public enum ModoSegnale
{
    Nessuno,
    Suono,
    Voce,
    VoceESuono,
}
