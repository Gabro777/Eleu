using Eleu.Proofing;

namespace Eleu.UI;

/// <summary>Strumenti > Pulizia del testo: what to clean up, and whether only in the selection.</summary>
public sealed class CleanupDialog : DialogBase
{
    private readonly CheckBox _doubleSpaces, _edgeSpaces, _blankLines, _hyphenation, _brokenParagraphs, _selectionOnly;

    public CleanupDialog(CleanupOptions options, bool selection) : base("Pulizia del testo", 380)
    {
        CheckBox CheckBox(string text, bool isChecked) => WholeLine(new CheckBox { Text = text, Checked = isChecked, AutoSize = true });
        _doubleSpaces = CheckBox("&Spazi doppi tra le parole", options.DoubleSpaces);
        _edgeSpaces = CheckBox("Spazi a &inizio e fine paragrafo", options.EdgeSpaces);
        _blankLines = CheckBox("&Righe vuote ripetute (ne resta una)", options.BlankLines);
        _hyphenation = CheckBox("Parole &divise dal trattino a fine riga", options.Hyphenation);
        _brokenParagraphs = CheckBox("&Paragrafi spezzati a metà frase (testo copiato da PDF)", options.BrokenParagraphs);
        _selectionOnly = CheckBox("Solo nella s&elezione", selection);
        _selectionOnly.Enabled = selection;
        AddButtons();
        ActiveControl = _doubleSpaces;
    }

    public CleanupOptions Options => new(_doubleSpaces.Checked, _edgeSpaces.Checked, _blankLines.Checked, _hyphenation.Checked, _brokenParagraphs.Checked);

    public bool SelectionOnly => _selectionOnly.Enabled && _selectionOnly.Checked;
}
