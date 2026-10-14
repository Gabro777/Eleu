using Eleu.Accessibility;

namespace Eleu.UI;

/// <summary>
/// Strumenti > Opzioni: Eleu's general preferences in three tabs (Ctrl+Tab moves between them).
/// General: startup and recovery copy. Announcements and sounds: how Eleu signals document elements
/// and misspelled words. Proofing: what to report.
/// </summary>
public sealed class OptionsDialog : DialogBase
{
    private static readonly string[] Modes = ["Nessun segnale", "Suono", "Voce", "Voce e suono"];

    private readonly Settings _settings;
    private readonly CheckBox _reopen, _updates, _errorSound, _confirmations, _cells, _images, _underline, _uppercase, _numbers;
    private readonly NumericUpDown _minutes, _volume;
    private readonly ComboBox _elements;

    public OptionsDialog(Settings settings) : base("Opzioni")
    {
        _settings = settings;

        var general = NewGrid(120);
        _reopen = CheckBox(general, "All'&avvio riapri l'ultimo documento", settings.ReopenLast);
        _minutes = Field(general, "Copia di &recupero ogni (minuti):", Number(settings.RecoveryMinutes, 1, 30, 1, 0));
        _updates = CheckBox(general, "Controlla gli a&ggiornamenti all'avvio (una volta al giorno)", settings.CheckForUpdates);

        var announcements = NewGrid(180);
        _elements = Field(announcements, "&Titoli, segnalibri, note, commenti, interruzioni di pagina:", ComboBox(Modes, (int)settings.ElementCue));
        _errorSound = CheckBox(announcements, "Suono sulle parole &sbagliate", settings.MisspelledWordSound);
        _confirmations = CheckBox(announcements, "Suoni di &conferma (fine lettura, salvataggio)", settings.ConfirmationSounds);
        _volume = Field(announcements, "&Volume dei suoni:", Number(settings.SoundVolume, 0, 100, 10, 0));
        var test = WholeLine(announcements, new Button { Text = "&Prova il suono", AutoSize = true });
        test.Click += (_, _) => Sounds.Play(Cue.Heading, (int)_volume.Value);
        _cells = CheckBox(announcements, "Annuncia le ce&lle delle tabelle", settings.AnnounceCells);
        _images = CheckBox(announcements, "Annuncia le &immagini", settings.AnnounceImages);

        var proofing = NewGrid(120);
        _underline = CheckBox(proofing, "S&ottolinea gli errori mentre scrivi", settings.UnderlineErrors);
        _uppercase = CheckBox(proofing, "Ignora le parole tutte &maiuscole", settings.IgnoreUppercase);
        _numbers = CheckBox(proofing, "Ignora le parole con &numeri", settings.IgnoreWithDigits);

        var tabs = new TabControl { Dock = DockStyle.Fill, Height = 340, Width = 560, AccessibleName = "Schede" };
        tabs.TabPages.Add(Tab("Generale", general));
        tabs.TabPages.Add(Tab("Annunci e suoni", announcements));
        tabs.TabPages.Add(Tab("Correzione", proofing));
        WholeLine(tabs);

        AddButtons();
        ActiveControl = _reopen;
    }

    /// <summary>Writes the choices into the settings (after OK).</summary>
    public void Apply()
    {
        _settings.ReopenLast = _reopen.Checked;
        _settings.CheckForUpdates = _updates.Checked;
        _settings.RecoveryMinutes = (int)_minutes.Value;
        _settings.ElementCue = (CueMode)Math.Max(0, _elements.SelectedIndex);
        _settings.MisspelledWordSound = _errorSound.Checked;
        _settings.ConfirmationSounds = _confirmations.Checked;
        _settings.SoundVolume = (int)_volume.Value;
        _settings.AnnounceCells = _cells.Checked;
        _settings.AnnounceImages = _images.Checked;
        _settings.UnderlineErrors = _underline.Checked;
        _settings.IgnoreUppercase = _uppercase.Checked;
        _settings.IgnoreWithDigits = _numbers.Checked;
    }
}
