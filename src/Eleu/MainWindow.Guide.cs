using Eleu.Documents;

namespace Eleu;

// Aiuto > Guida di Eleu (F1): the guide is an Eleu document, read with the usual keys.
public sealed partial class MainWindow
{
    private readonly bool _openGuide;

    private static string GuidePath => Path.Combine(AppContext.BaseDirectory, "Guida", "Guida.html");

    /// <summary>Opens the guide here, if the window is empty, otherwise in a new window; if it is already open it goes there.</summary>
    private void OpenGuide()
    {
        var opened = EleuContext.Windows.FirstOrDefault(f => f._guide);
        if (opened is not null)
        {
            opened.ActivateAndAnnounce();
            return;
        }
        if (DocumentEmpty)
            LoadGuide();
        else
            EleuContext.Open(guide: true);
    }

    private bool _guide;

    private void LoadGuide()
    {
        if (!File.Exists(GuidePath))
        {
            _announcer.Announce("La guida non è stata installata insieme a Eleu");
            return;
        }
        var guide = HtmlFormat.Read(File.ReadAllText(GuidePath));
        LoadContent(guide);
        // It has no file: if it is changed and saved, Eleu asks where (in Documents).
        SetCurrentDocument(null, FileFormat.Docx,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Guida di Eleu.docx"));
        _metadata = guide.Metadata;
        _guide = true;
        AnnounceShortly("Guida di Eleu. Si passa da un titolo all'altro con Inserisci, Titoli, Titolo successivo; Ctrl+F4 la chiude.");
    }
}
