using Eleu.Documents;
using Eleu.UI;

namespace Eleu;

// Document properties (File > Proprietà): title, author, subject, keywords.
public sealed partial class MainWindow
{
    private Metadata _metadata = new();

    /// <summary>The document title ({titolo} in header and footer): the one from the properties, if any.</summary>
    private string DocumentTitle => _metadata.Title.Length > 0 ? _metadata.Title : DocumentName;

    /// <summary>The RTF to save: text with bookmarks, footnotes and images, page and document properties.</summary>
    private string FullRtf() => _metadata.ToRtf(RtfWriter.WithPage(RtfToSave(), _page));

    private void OpenProperties()
    {
        var statistics = Statistics.Compute(_text.Text);
        using var window = new PropertiesDialog(_metadata, _path, statistics.Description());
        if (window.ShowDialog(this) != DialogResult.OK || window.Result == _metadata)
            return;
        _metadata = window.Result;
        _text.Modified = true;
        AnnounceShortly("Proprietà del documento cambiate");
    }
}

/// <summary>
/// File > Proprietà: title, author, subject and keywords, which Word, e-book readers and libraries
/// read; below, to read, the file and when it was created.
/// </summary>
public sealed class PropertiesDialog : DialogBase
{
    private readonly Metadata _initials;
    private readonly TextBox _heading, _author, _subject, _keywords;

    public PropertiesDialog(Metadata metadata, string? path, string statistics) : base("Proprietà del documento", 340)
    {
        _initials = metadata;
        _heading = Field("&Titolo:", new TextBox { Text = metadata.Title });
        _author = Field("&Autore:", new TextBox { Text = metadata.Author });
        _subject = Field("&Oggetto:", new TextBox { Text = metadata.Subject });
        _keywords = Field("&Parole chiave:", new TextBox { Text = metadata.Keywords });
        var info = new List<string>
        {
            path is null ? "Documento non ancora salvato" : $"File: {path}",
            metadata.Created is DateTime created ? $"Creato il {created:d MMMM yyyy}" : "Data di creazione: al primo salvataggio",
            statistics,
        };
        WholeLine(new TextBox
        {
            Text = string.Join(Environment.NewLine, info),
            ReadOnly = true,
            Multiline = true,
            Height = 70,
            AccessibleName = "Informazioni sul documento",
        });
        AddButtons();
        ActiveControl = _heading;
    }

    public Metadata Result => _initials with
    {
        Title = _heading.Text.Trim(),
        Author = _author.Text.Trim(),
        Subject = _subject.Text.Trim(),
        Keywords = _keywords.Text.Trim(),
    };
}
