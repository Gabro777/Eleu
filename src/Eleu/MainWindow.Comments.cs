using Eleu.Documents;
using Eleu.UI;

namespace Eleu;

// Comments (Inserisci > Commenti): margin notes on a piece of text, like those left by someone reviewing a
// document in Word or LibreOffice. Each comment is a Text Object Model range that follows the text;
// author and text are kept separately. Arriving on commented text Eleu signals it (sound or voice, from the Options).
public sealed partial class MainWindow
{
    private readonly List<(Comment Comment, dynamic Range)> _comments = [];
    private List<(Comment Comment, int Start, int End)>? _pendingComments;

    private void PrepareComments()
    {
        // As for bookmarks and footnotes: if the box recreates its window, the ranges must be recreated.
        _text.HandleDestroyed += (_, _) =>
        {
            _pendingComments = CommentList();
            _comments.Clear();
        };
        _text.HandleCreated += (_, _) =>
        {
            if (_pendingComments is { } pending)
                BeginInvoke(() =>
                {
                    if (Tom is { } tom)
                        _comments.AddRange(pending.Select(c => (c.Comment, (dynamic)tom.Range(c.Start, c.End))));
                });
            _pendingComments = null;
        };
    }

    /// <summary>The comments in position order, with the commented text range.</summary>
    private List<(Comment Comment, int Start, int End)> CommentList() =>
        _comments.Select(c => (c.Comment, Start: Math.Clamp((int)c.Range.Start, 0, _text.TextLength), End: Math.Clamp((int)c.Range.End, 0, _text.TextLength)))
            .OrderBy(c => c.Start).ThenBy(c => c.End).ToList();

    /// <summary>The comment on the text where the cursor is (the narrowest, if nested), with its number.</summary>
    private (int Number, Comment Comment, int Start, int End)? CommentHere()
    {
        int here = _text.SelectionStart;
        var comments = CommentList();
        var found = comments.Select((c, i) => (Number: i + 1, c.Comment, c.Start, c.End))
            .Where(c => here >= c.Start && (here < c.End || here == c.Start))
            .OrderBy(c => c.End - c.Start).ToList();
        return found.Count == 0 ? null : found[0];
    }

    /// <summary>The commented text, shortened, to say what the comment is on.</summary>
    private string CommentedText(int start, int end)
    {
        var text = TextStructure.Readable(_text.Text[start..Math.Max(start, end)]).ReplaceLineEndings(" ").Trim();
        return text.Length == 0 ? "" : text.Length > 60 ? text[..60] + "..." : text;
    }

    private string DescribeComment(int number, Comment comment, int start, int end)
    {
        var onText = CommentedText(start, end);
        var author = comment.Author.Length > 0 ? $" di {comment.Author}" : "";
        var text = comment.Text.ReplaceLineEndings(" ").Trim();
        // After a text that already ends with its punctuation no period is added.
        var gap = text.Length > 0 && ".!?:;…".Contains(text[^1]) ? " " : ". ";
        return $"Commento {number}{author}: {text}{(onText.Length > 0 ? $"{gap}Sul testo: {onText}" : "")}";
    }

    /// <summary>
    /// Ctrl+Alt+M: comments the selection or, with no selection, the word the cursor is in.
    /// </summary>
    private void AddComment()
    {
        int start = _text.SelectionStart, end = start + _text.SelectionLength;
        if (end == start)
            (start, end) = WordAround(_text.Text, Math.Min(start, _text.TextLength));
        using var window = new CommentDialog("Aggiungi commento", "", _settings.CommentAuthor ?? Environment.UserName);
        if (window.ShowDialog(this) != DialogResult.OK || Tom is not { } tom)
            return;
        if (_comments.Count >= CommentMarkers.Max)
        {
            AnnounceShortly($"Un documento può avere al massimo {CommentMarkers.Max} commenti");
            return;
        }
        _settings.CommentAuthor = window.Author;
        _comments.Add((new Comment(window.CommentText, window.Author, DateTime.Now), tom.Range(start, end)));
        _text.Modified = true;
        AnnounceShortly(end > start ? $"Commento aggiunto su: {CommentedText(start, end)}" : "Commento aggiunto");
    }

    private void EditComment()
    {
        if (CommentHere() is not var (number, comment, start, end))
        {
            _announcer.Announce("Il cursore non è su un testo commentato");
            return;
        }
        using var window = new CommentDialog($"Modifica commento {number}", comment.Text, comment.Author);
        if (window.ShowDialog(this) != DialogResult.OK)
            return;
        int index = _comments.FindIndex(c => ReferenceEquals(c.Comment, comment));
        _comments[index] = (comment with { Text = window.CommentText, Author = window.Author }, _comments[index].Range);
        _text.Modified = true;
        AnnounceShortly($"Commento {number} modificato");
    }

    /// <summary>Removes the comment where the cursor is; the commented text stays.</summary>
    private void DeleteComment()
    {
        if (CommentHere() is not var (number, comment, _, _))
        {
            _announcer.Announce("Il cursore non è su un testo commentato");
            return;
        }
        _comments.RemoveAll(c => ReferenceEquals(c.Comment, comment));
        _text.Modified = true;
        _announcer.Announce($"Commento {number} eliminato");
    }

    private void NearbyComment(bool forward)
    {
        var comments = CommentList();
        int here = _text.SelectionStart;
        int i = forward ? comments.FindIndex(c => c.Start > here) : comments.FindLastIndex(c => c.Start < here);
        if (i < 0)
        {
            _announcer.Announce(comments.Count == 0 ? "Nessun commento nel documento" : forward ? "Nessun commento dopo" : "Nessun commento prima");
            return;
        }
        GoToComment(i + 1, comments[i]);
    }

    private void GoToComment(int number, (Comment Comment, int Start, int End) c)
    {
        _lastReportedComment = c.Start; // the announcement is already there: no extra cue
        _text.Select(c.Start, 0);
        _text.ScrollToCaret();
        _announcer.Announce(DescribeComment(number, c.Comment, c.Start, c.End));
    }

    private void ShowCommentList()
    {
        var comments = CommentList();
        var items = comments.Select((c, i) => new ListItem(
            [c.Comment.Author, c.Comment.Text.ReplaceLineEndings(" "), CommentedText(c.Start, c.End)], i)).ToList();
        using var window = new ListDialog("Commenti", ["Autore", "Commento", "Sul testo"], items);
        window.ShowDialog(this);
        if (window.ToDelete.Count > 0)
        {
            var toRemove = window.ToDelete.Select(i => comments[i].Comment).ToHashSet();
            _comments.RemoveAll(c => toRemove.Contains(c.Comment));
            _text.Modified = true;
            AnnounceShortly(toRemove.Count == 1 ? "Commento eliminato" : $"{toRemove.Count} commenti eliminati");
        }
        else if (window.Target is int index)
        {
            BeginInvoke(() => GoToComment(index + 1, comments[index]));
        }
    }

    /// <summary>For Alt+Enter: if the cursor is on commented text, the comment.</summary>
    private string? CommentDescriptionHere() =>
        CommentHere() is var (number, comment, start, end) ? DescribeComment(number, comment, start, end) + ". " : null;
}

/// <summary>Writes or edits a comment: the text (Enter adds a line, Ctrl+Enter confirms) and the author.</summary>
public sealed class CommentDialog : DialogBase
{
    private readonly TextBox _text, _author;

    public CommentDialog(string heading, string text, string author) : base(heading, 420)
    {
        _text = Field("&Testo del commento:", new TextBox
        {
            Text = text.ReplaceLineEndings(Environment.NewLine),
            Multiline = true,
            AcceptsReturn = true,
            ScrollBars = ScrollBars.Vertical,
            Height = 120,
        });
        _author = Field("&Autore:", new TextBox { Text = author });
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
            if (DialogResult == DialogResult.OK && CommentText.Length == 0)
            {
                e.Cancel = true;
                MessageBox.Show(this, "Scrivi il testo del commento.", heading, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        };
    }

    public string CommentText => _text.Text.ReplaceLineEndings("\n").Trim();

    public string Author => _author.Text.Trim();
}
