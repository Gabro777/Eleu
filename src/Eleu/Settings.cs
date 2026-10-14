using System.Text.Json;
using System.Text.Json.Serialization;

namespace Eleu;

/// <summary>User preferences, saved in %AppData%\Eleu\impostazioni.json.</summary>
public sealed class Settings
{
    public const int MaxRecentFiles = 5;
    public const float MinZoom = 0.5f;
    public const float MaxZoom = 5f;

    public static string DefaultPath { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Eleu", "impostazioni.json");

    public float Zoom { get; set; } = 1f;

    [JsonPropertyName("FileRecenti")]
    public List<string> RecentFiles { get; set; } = [];

    /// <summary>Voice chosen for reading and audiobooks; null = automatic choice (Italian).</summary>
    [JsonPropertyName("VoceId")]
    public string? VoiceId { get; set; }

    [JsonPropertyName("VelocitaVoce")]
    public int VoiceRate { get; set; } = 100;

    [JsonPropertyName("VolumeVoce")]
    public int VoiceVolume { get; set; } = 100;

    /// <summary>Braille printer chosen last time; null = save a BRF file.</summary>
    [JsonPropertyName("StampanteBraille")]
    public string? BraillePrinter { get; set; }

    [JsonPropertyName("CelleBraille")]
    public int BrailleCells { get; set; } = 32;

    [JsonPropertyName("RigheBraille")]
    public int BrailleLines { get; set; } = 27;

    // The other Braille page choices (see Braille.PageFormat): kept for next time.
    [JsonPropertyName("NumeroBraille")]
    public Braille.BraillePageNumber BrailleNumber { get; set; } = Braille.BraillePageNumber.TopRight;

    [JsonPropertyName("TitoloBraille")]
    public bool BrailleHeading { get; set; }

    [JsonPropertyName("MargineBraille")]
    public int BrailleMargin { get; set; }

    [JsonPropertyName("PagineStampaBraille")]
    public bool BraillePrintPages { get; set; }

    [JsonPropertyName("SommarioBraille")]
    public bool BrailleTableOfContents { get; set; }

    [JsonPropertyName("InterlineaDoppiaBraille")]
    public bool BrailleDoubleSpacing { get; set; }

    [JsonPropertyName("FronteRetroBraille")]
    public bool BrailleDoubleSided { get; set; }

    [JsonPropertyName("PiegaBraille")]
    public bool BrailleFold { get; set; }

    /// <summary>Strip at the bottom with the current line in Braille (useful mostly to sighted users).</summary>
    [JsonPropertyName("RigaBraille")]
    public bool BrailleLine { get; set; }

    [JsonPropertyName("ACapoAutomatico")]
    public bool WordWrap { get; set; } = true;

    /// <summary>Button bar and ruler above the text, for sighted mouse users.</summary>
    [JsonPropertyName("BarraStrumenti")]
    public bool Toolbar { get; set; } = true;

    [JsonPropertyName("Righello")]
    public bool Ruler { get; set; } = true;

    /// <summary>Maximized window at startup.</summary>
    [JsonPropertyName("Ingrandita")]
    public bool Maximized { get; set; } = true;

    /// <summary>Words the spell checker must not report (names, technical terms...).</summary>
    [JsonPropertyName("DizionarioPersonale")]
    public List<string> PersonalDictionary { get; set; } = [];

    /// <summary>Misspelled words underlined in red while typing, for sighted users.</summary>
    [JsonPropertyName("ErroriSottolineati")]
    public bool UnderlineErrors { get; set; } = true;

    /// <summary>The spell checker skips all-uppercase words (acronyms like ONU, NVDA).</summary>
    [JsonPropertyName("IgnoraMaiuscole")]
    public bool IgnoreUppercase { get; set; } = true;

    /// <summary>The spell checker skips words with digits (codes, A4, 3D).</summary>
    [JsonPropertyName("IgnoraConNumeri")]
    public bool IgnoreWithDigits { get; set; } = true;

    // ---------- Options (Strumenti > Opzioni) ----------

    /// <summary>How Eleu signals headings, bookmarks, footnotes, comments and page breaks when the cursor reaches them.</summary>
    [JsonPropertyName("SegnaleElementi")]
    public CueMode ElementCue { get; set; } = CueMode.Sound;

    /// <summary>A sound when the cursor, moving, reaches a misspelled word.</summary>
    [JsonPropertyName("SuonoParolaSbagliata")]
    public bool MisspelledWordSound { get; set; } = true;

    /// <summary>A sound at the end of reading and after saving.</summary>
    [JsonPropertyName("SuoniConferma")]
    public bool ConfirmationSounds { get; set; } = true;

    /// <summary>Volume of the sound cues, from 0 to 100, relative to the computer's other sounds.</summary>
    [JsonPropertyName("VolumeSuoni")]
    public int SoundVolume { get; set; } = 60;

    /// <summary>Entering a table cell Eleu says row, column and header.</summary>
    [JsonPropertyName("AnnunciaCelle")]
    public bool AnnounceCells { get; set; } = true;

    /// <summary>Arriving on an image Eleu says its description.</summary>
    [JsonPropertyName("AnnunciaImmagini")]
    public bool AnnounceImages { get; set; } = true;

    /// <summary>At startup, without a file to open, Eleu reopens the last document used.</summary>
    [JsonPropertyName("RiapriUltimo")]
    public bool ReopenLast { get; set; }

    /// <summary>Every how many minutes Eleu keeps a copy aside for recovery after an interruption.</summary>
    [JsonPropertyName("MinutiRipristino")]
    public int RecoveryMinutes { get; set; } = 1;

    /// <summary>Once a day, at startup, Eleu checks on GitHub whether there is a new version.</summary>
    [JsonPropertyName("ControllaAggiornamenti")]
    public bool CheckForUpdates { get; set; } = true;

    [JsonPropertyName("UltimoControlloAggiornamenti")]
    public DateTime? LastUpdateCheck { get; set; }

    /// <summary>The version the user chose to skip (the tag, for example "v1.2.0").</summary>
    [JsonPropertyName("VersioneSaltata")]
    public string? SkippedVersion { get; set; }

    /// <summary>The name comments are signed with (null = the Windows user name).</summary>
    [JsonPropertyName("AutoreCommenti")]
    public string? CommentAuthor { get; set; }

    /// <summary>The choices of the last text cleanup.</summary>
    [JsonPropertyName("Pulizia")]
    public Proofing.CleanupOptions Cleanup { get; set; } = new();

    /// <summary>The list of multiple replacements, kept for next time.</summary>
    [JsonPropertyName("Sostituzioni")]
    public List<Proofing.ReplacementPair> Replacements { get; set; } = [];

    [JsonPropertyName("SostituzioniParoleIntere")]
    public bool WholeWordReplacements { get; set; } = true;

    /// <summary>How the voice must say certain words or acronyms (Voce > Pronunce).</summary>
    [JsonPropertyName("Pronunce")]
    public List<Speech.Pronunciation> Pronunciations { get; set; } = [];

    // Starting point of new documents ("Predefinito" in the Carattere, Paragrafo and Imposta pagina windows).
    [JsonPropertyName("CarattereBase")]
    public string BaseFont { get; set; } = "Segoe UI";

    [JsonPropertyName("DimensioneBase")]
    public float BaseSize { get; set; } = Documents.DocumentContent.BaseSize;

    [JsonPropertyName("ParagrafoBase")]
    public Editor.ParagraphFormat? BaseParagraph { get; set; }

    [JsonPropertyName("PaginaPredefinita")]
    public Documents.PageLayout? DefaultPage { get; set; }

    public const int MaxRememberedPositions = 50;

    /// <summary>Where you were in each recently used document: reopening it starts from there.</summary>
    [JsonPropertyName("UltimePosizioni")]
    public List<FilePosition> LastPositions { get; set; } = [];

    public void RememberPosition(string path, int position)
    {
        LastPositions.RemoveAll(p => string.Equals(p.Path, path, StringComparison.OrdinalIgnoreCase));
        LastPositions.Insert(0, new FilePosition(path, position));
        if (LastPositions.Count > MaxRememberedPositions)
            LastPositions.RemoveRange(MaxRememberedPositions, LastPositions.Count - MaxRememberedPositions);
    }

    public int? RememberedPosition(string path) =>
        LastPositions.FirstOrDefault(p => string.Equals(p.Path, path, StringComparison.OrdinalIgnoreCase))?.Position;

    public void AddRecent(string path)
    {
        RecentFiles.RemoveAll(f => string.Equals(f, path, StringComparison.OrdinalIgnoreCase));
        RecentFiles.Insert(0, path);
        if (RecentFiles.Count > MaxRecentFiles)
            RecentFiles.RemoveRange(MaxRecentFiles, RecentFiles.Count - MaxRecentFiles);
    }

    private static Settings? _shared;

    /// <summary>Eleu's settings, the same for all open windows.</summary>
    public static Settings Shared => _shared ??= Load();

    public static Settings Load(string? path = null)
    {
        try
        {
            var json = File.ReadAllText(path ?? DefaultPath);
            var settings = JsonSerializer.Deserialize<Settings>(json) ?? new Settings();
            settings.Zoom = Math.Clamp(settings.Zoom, MinZoom, MaxZoom);
            settings.SoundVolume = Math.Clamp(settings.SoundVolume, 0, 100);
            settings.RecoveryMinutes = Math.Clamp(settings.RecoveryMinutes, 1, 30);
            return settings;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return new Settings();
        }
    }

    public void Save(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Preferences are not essential: if they cannot be saved, carry on.
        }
    }
}

public sealed record FilePosition([property: JsonPropertyName("Percorso")] string Path, [property: JsonPropertyName("Posizione")] int Position);

/// <summary>How to signal a document element: with a sound, by voice, both or not at all.</summary>
public enum CueMode
{
    None,
    Sound,
    Speech,
    SpeechAndSound,
}
