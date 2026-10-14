using Eleu.Documents;
using Eleu.Editor;
using Eleu.UI;

namespace Eleu;

// Footnotes (Inserisci > Note). In the text the reference is the superscript number, a
// Text Object Model range that follows the text; the footnote text is kept separately and read with Alt+Enter.
public sealed partial class MainWindow
{
    private const int RealTom = -1;

    private readonly List<(string Text, dynamic Range)> _footnotes = [];
    private List<(string Text, int Start, int End)>? _pendingFootnotes;

    private void PrepareFootnotes()
    {
        // As for bookmarks: if the box recreates its window, the ranges must be recreated.
        _text.HandleDestroyed += (_, _) =>
        {
            _pendingFootnotes = FootnoteList();
            _footnotes.Clear();
        };
        _text.HandleCreated += (_, _) =>
        {
            if (_pendingFootnotes is { } pending)
                BeginInvoke(() =>
                {
                    if (Tom is { } tom)
                        _footnotes.AddRange(pending.Select(n => (n.Text, (dynamic)tom.Range(n.Start, n.End))));
                });
            _pendingFootnotes = null;
        };
    }

    /// <summary>
    /// The footnotes in position order. A footnote whose reference was deleted by typing (empty range)
    /// is gone.
    /// </summary>
    private List<(string Text, int Start, int End)> FootnoteList()
    {
        _footnotes.RemoveAll(n => (int)n.Range.Start >= (int)n.Range.End);
        return _footnotes.Select(n => (n.Text, Start: (int)n.Range.Start, End: (int)n.Range.End))
            .OrderBy(n => n.Start).ToList();
    }

    /// <summary>Rewrites the reference numbers (1, 2, 3...) in the order they appear in the text.</summary>
    private void RenumberFootnotes()
    {
        FootnoteList();
        var sorted = _footnotes.OrderBy(n => (int)n.Range.Start).ToList();
        for (int i = 0; i < sorted.Count; i++)
        {
            var reference = sorted[i].Range;
            var number = (i + 1).ToString();
            if ((string)reference.Text != number)
                reference.Text = number;
            reference.Font.Superscript = RealTom;
        }
    }

    /// <summary>The footnote of the reference where the cursor is (inside or right after), with its number.</summary>
    private (int Number, string Text, int Start, int End)? FootnoteHere()
    {
        int here = _text.SelectionStart;
        var footnotes = FootnoteList();
        int i = footnotes.FindIndex(n => here >= n.Start && here <= n.End);
        return i < 0 ? null : (i + 1, footnotes[i].Text, footnotes[i].Start, footnotes[i].End);
    }

    private void AddFootnote()
    {
        using var window = new FootnoteDialog("Aggiungi nota", "");
        if (window.ShowDialog(this) != DialogResult.OK || Tom is not { } tom)
            return;
        int position = _text.SelectionStart + _text.SelectionLength; // after the word or the selection
        var reference = tom.Range(position, position);
        reference.Text = "0";
        reference.Font.Superscript = RealTom;
        _footnotes.Add((window.FootnoteText, reference));
        RenumberFootnotes();

        // Typing continues after the reference, in normal font and not superscript.
        _text.Select((int)reference.End, 0);
        NativeRichEdit.SetCharacterFormat(_text, new CharacterFormat(Position: TextPosition.Normal));
        _text.Modified = true;
        AnnounceShortly($"Nota {FootnoteHere()?.Number} aggiunta");
    }

    private void EditFootnote()
    {
        if (FootnoteHere() is not (int number, string text, int start, _))
        {
            _announcer.Announce("Il cursore non è su una nota");
            return;
        }
        using var window = new FootnoteDialog($"Modifica nota {number}", text);
        if (window.ShowDialog(this) != DialogResult.OK)
            return;
        int index = _footnotes.FindIndex(n => (int)n.Range.Start == start);
        _footnotes[index] = (window.FootnoteText, _footnotes[index].Range);
        _text.Modified = true;
        AnnounceShortly($"Nota {number} modificata");
    }

    private void DeleteFootnote()
    {
        if (FootnoteHere() is not (int number, _, int start, _))
        {
            _announcer.Announce("Il cursore non è su una nota");
            return;
        }
        DeleteFootnotes([start]);
        _announcer.Announce($"Nota {number} eliminata");
    }

    private void DeleteFootnotes(IEnumerable<int> starts)
    {
        var toRemove = starts.ToHashSet();
        foreach (var footnote in _footnotes.Where(n => toRemove.Contains((int)n.Range.Start)).ToList())
        {
            footnote.Range.Text = "";
            _footnotes.Remove(footnote);
        }
        RenumberFootnotes();
        _text.Modified = true;
    }

    private void NearbyFootnote(bool forward)
    {
        var footnotes = FootnoteList();
        int here = _text.SelectionStart;
        int i = forward ? footnotes.FindIndex(n => n.Start > here) : footnotes.FindLastIndex(n => n.End < here);
        if (i < 0)
        {
            _announcer.Announce(footnotes.Count == 0 ? "Nessuna nota nel documento" : forward ? "Nessuna nota dopo" : "Nessuna nota prima");
            return;
        }
        GoToFootnote(i + 1, footnotes[i].Text, footnotes[i].End);
    }

    private void GoToFootnote(int number, string text, int end)
    {
        _text.Select(end, 0);
        _text.ScrollToCaret();
        _announcer.Announce($"Nota {number}: {text}");
    }

    private void ShowFootnoteList()
    {
        var footnotes = FootnoteList();
        var items = footnotes.Select((n, i) => new ListItem([$"Nota {i + 1}", n.Text.ReplaceLineEndings(" ")], n.Start)).ToList();
        using var window = new ListDialog("Note", ["Nota", "Testo"], items);
        window.ShowDialog(this);
        if (window.ToDelete.Count > 0)
        {
            DeleteFootnotes(window.ToDelete);
            AnnounceShortly(window.ToDelete.Count == 1 ? "Nota eliminata" : $"{window.ToDelete.Count} note eliminate");
        }
        else if (window.Target is int start)
        {
            int i = footnotes.FindIndex(n => n.Start == start);
            BeginInvoke(() => GoToFootnote(i + 1, footnotes[i].Text, footnotes[i].End));
        }
    }

    /// <summary>
    /// The document for Braille and the audiobook (see <see cref="Readable"/>): footnotes at the end, images with their
    /// description, tables row by row.
    /// </summary>
    private DocumentContent ContentWithFootnotes() => Readable.From(CompleteModel());

    /// <summary>For Alt+Enter: if the cursor is on a footnote, its text.</summary>
    private string? FootnoteDescriptionHere() => FootnoteHere() is (int number, string text, _, _) ? $"Nota {number}: {text.TrimEnd('.')}. " : null;
}

/// <summary>Writes or edits the text of a footnote; Enter adds a line, Ctrl+Enter or OK confirms.</summary>
public sealed class FootnoteDialog : DialogBase
{
    private readonly TextBox _text;

    public FootnoteDialog(string heading, string text) : base(heading, 420)
    {
        _text = Field("&Testo della nota:", new TextBox
        {
            Text = text.ReplaceLineEndings(Environment.NewLine),
            Multiline = true,
            AcceptsReturn = true,
            ScrollBars = ScrollBars.Vertical,
            Height = 120,
        });
        AddButtons();
        ActiveControl = _text;
        _text.KeyDown += (_, e) =>
        {
            if (e.KeyData == (Keys.Control | Keys.Enter))
            {
                e.SuppressKeyPress = true;
                DialogResult = DialogResult.OK;
            }
        };
        FormClosing += (_, e) =>
        {
            if (DialogResult == DialogResult.OK && FootnoteText.Length == 0)
            {
                e.Cancel = true;
                MessageBox.Show(this, "Scrivi il testo della nota.", heading, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        };
    }

    public string FootnoteText => _text.Text.ReplaceLineEndings("\n").Trim();
}
