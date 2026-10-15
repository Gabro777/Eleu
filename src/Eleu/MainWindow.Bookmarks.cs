using Eleu.Documents;
using Eleu.Editor;
using Eleu.UI;

namespace Eleu;

// Bookmarks: named points to return to (Inserisci > Segnalibri), kept in RTF and DOCX.
// Each bookmark is a RichEdit Text Object Model range, which moves by itself while typing.
public sealed partial class MainWindow
{
    private readonly List<(string Name, dynamic Range)> _bookmarks = [];
    private dynamic? _tom;

    // If the box recreates its window (for example when changing "A capo automatico") the ranges are no longer valid:
    // the positions are remembered and the ranges recreated.
    private List<(string Name, int Position)>? _pendingBookmarks;

    private dynamic? Tom => _tom ??= NativeRichEdit.TomDocument(_text);

    private void PrepareBookmarks()
    {
        _text.HandleDestroyed += (_, _) =>
        {
            _pendingBookmarks = BookmarkList();
            _bookmarks.Clear();
            _tom = null;
        };
        _text.HandleCreated += (_, _) =>
        {
            if (_pendingBookmarks is { } pending)
                BeginInvoke(() => SetBookmarks(pending));
            _pendingBookmarks = null;
        };
    }

    /// <summary>The bookmarks in position order.</summary>
    private List<(string Name, int Position)> BookmarkList() =>
        _bookmarks.Select(s => (s.Name, Position: Math.Clamp((int)s.Range.Start, 0, _text.TextLength)))
            .OrderBy(s => s.Position).ToList();

    private void SetBookmarks(IEnumerable<(string Name, int Position)> bookmarks)
    {
        _bookmarks.Clear();
        if (Tom is not { } tom)
            return;
        foreach (var (name, position) in bookmarks)
            _bookmarks.Add((name, tom.Range(position, position)));
    }

    /// <summary>The document's bookmarks, footnotes and images, in the order they appear: their placeholder is the number in the list.</summary>
    private sealed record Placeholders(List<string> Bookmarks, List<string> Footnotes, List<EmbeddedImage> Images, List<Comment> Comments)
    {
        public bool Empty => Bookmarks.Count == 0 && Footnotes.Count == 0 && Images.Count == 0 && Comments.Count == 0;
    }

    /// <summary>
    /// Puts the RTF into the text box with bookmarks, footnotes and images. If <paramref name="model"/> is null they are
    /// still as in the file ({\*\bkmkstart}, {\footnote}, {\pict}), otherwise they are already placeholders with the model's data.
    /// </summary>
    private void LoadRtf(string rtf, DocumentContent? model = null)
    {
        // The text box loses the table header rows: they are read first and remembered separately.
        var withHeaderRows = model ?? (rtf.Contains(@"\trhdr", StringComparison.Ordinal) ? RtfReader.Read(rtf) : null);
        LoadRtfWithPlaceholders(rtf, model);
        HeaderRowsAfterLoad(withHeaderRows);
    }

    private void LoadRtfWithPlaceholders(string rtf, DocumentContent? model)
    {
        var names = model?.Bookmarks.ToList() ?? [];
        var footnoteTexts = model?.Footnotes.ToList() ?? [];
        var images = model?.Images.ToList() ?? [];
        var comments = model?.Comments.ToList() ?? [];
        if (model is null)
            rtf = Images.ToPlaceholders(FootnoteMarkers.ToPlaceholders(Bookmarks.ToPlaceholders(CommentMarkers.ToPlaceholders(rtf, comments), names), footnoteTexts), images);
        _lastCell = null;
        _text.Rtf = rtf;
        _bookmarks.Clear();
        _footnotes.Clear();
        _images.Clear();
        _comments.Clear();
        if ((names.Count == 0 && footnoteTexts.Count == 0 && images.Count == 0 && comments.Count == 0) || Tom is not { } tom)
            return;

        // From the end to the start: bookmarks leave an empty range, footnotes their superscript number,
        // images the real image. Ranges already created move by themselves when the text before them changes.
        var text = _text.Text;
        int footnoteNumber = text.Count(c => FootnoteMarkers.Index(c) >= 0 && FootnoteMarkers.Index(c) < footnoteTexts.Count);
        var bookmarks = new List<(string, dynamic)>();
        var footnotes = new List<(string, dynamic)>();
        var figures = new List<(EmbeddedImage, dynamic)>();
        // For comments the end is met first (going backward): an empty range holds it until the start arrives.
        var commentEnds = new Dictionary<int, dynamic>();
        var commentRanges = new SortedDictionary<int, dynamic>();
        for (int i = text.Length - 1; i >= 0; i--)
        {
            int bookmark = Bookmarks.Index(text[i]), footnote = FootnoteMarkers.Index(text[i]), image = Images.Index(text[i]);
            int commentStart = CommentMarkers.StartIndex(text[i]), commentEnd = CommentMarkers.EndIndex(text[i]);
            if (commentStart >= 0 || commentEnd >= 0)
            {
                tom.Range(i, i + 1).Text = "";
                if (commentEnd >= 0 && commentEnd < comments.Count)
                    commentEnds[commentEnd] = tom.Range(i, i);
                else if (commentStart >= 0 && commentStart < comments.Count)
                    commentRanges[commentStart] = tom.Range(i, commentEnds.TryGetValue(commentStart, out var end) ? (int)end.Start : i);
                continue;
            }
            if (bookmark >= 0)
            {
                tom.Range(i, i + 1).Text = "";
                if (bookmark < names.Count)
                    bookmarks.Insert(0, (names[bookmark], tom.Range(i, i)));
            }
            else if (footnote >= 0 && footnote < footnoteTexts.Count)
            {
                var reference = tom.Range(i, i + 1);
                reference.Text = (footnoteNumber--).ToString();
                reference.Font.Superscript = RealTom;
                footnotes.Insert(0, (footnoteTexts[footnote], reference));
            }
            else if (image >= 0 && image < images.Count)
            {
                _text.Select(i, 1);
                _text.SelectedRtf = Images.RtfImageOnly(images[image]);
                figures.Insert(0, (images[image], tom.Range(i, i + 1)));
            }
        }
        _bookmarks.AddRange(bookmarks);
        _footnotes.AddRange(footnotes);
        _images.AddRange(figures);
        _comments.AddRange(commentRanges.Select(c => (comments[c.Key], c.Value)));
        _text.Select(0, 0);
        _text.ClearUndo();
    }

    /// <summary>
    /// The document RTF with bookmarks, footnotes and images as placeholders, and their data. The text box does not
    /// change: the work is done on an invisible copy, so text, undo and cursor stay as they are.
    /// </summary>
    private string RtfWithPlaceholders(out Placeholders placeholders)
    {
        var bookmarks = BookmarkList();
        var footnotes = FootnoteList();
        var images = ImageList();
        var comments = CommentList();
        placeholders = new Placeholders(bookmarks.Select(s => s.Name).ToList(), footnotes.Select(n => n.Text).ToList(),
            images.Select(i => i.Image).ToList(), comments.Select(c => c.Comment).ToList());
        if (placeholders.Empty)
            return _text.Rtf ?? "";

        // From the end to the start; at the same position footnotes and images first, so the bookmark stays in front.
        var operations = bookmarks.Select((s, i) => (Start: s.Position, End: s.Position, Character: Bookmarks.Placeholder(i), Order: 1))
            .Concat(footnotes.Select((n, i) => (Start: n.Start, End: n.End, Character: FootnoteMarkers.Placeholder(i), Order: 0)))
            .Concat(images.Select((m, i) => (Start: m.Position, End: m.Position + 1, Character: Images.Placeholder(i), Order: 0)))
            // The end before the start, so in a comment at a point the start stays in front.
            .Concat(comments.Select((c, i) => (Start: c.End, End: c.End, Character: CommentMarkers.End(i), Order: 2)))
            .Concat(comments.Select((c, i) => (Start: c.Start, End: c.Start, Character: CommentMarkers.Start(i), Order: 3)))
            .OrderByDescending(o => o.Start).ThenBy(o => o.Order);
        using var copy = new RichTextBox();
        copy.CreateControl();
        copy.Rtf = _text.Rtf;
        foreach (var o in operations)
        {
            copy.Select(o.Start, o.End - o.Start);
            copy.SelectedText = o.Character.ToString();
        }
        return copy.Rtf ?? "";
    }

    /// <summary>The RTF to save: the text box's with bookmarks, footnotes, comments, images and table header rows.</summary>
    private string RtfToSave()
    {
        var rtf = RtfWithPlaceholders(out var placeholders);
        if (placeholders.Bookmarks.Count > 0)
            rtf = Bookmarks.FromPlaceholders(rtf, placeholders.Bookmarks);
        if (placeholders.Footnotes.Count > 0)
            rtf = FootnoteMarkers.FromPlaceholders(rtf, placeholders.Footnotes);
        if (placeholders.Comments.Count > 0)
            rtf = CommentMarkers.FromPlaceholders(rtf, placeholders.Comments);
        return HeaderRowsInRtf(placeholders.Images.Count > 0 ? Images.FromPlaceholders(rtf, placeholders.Images) : rtf);
    }

    /// <summary>The document as a model, with bookmarks, footnotes and images (to save as DOCX and to read it).</summary>
    private DocumentContent CompleteModel()
    {
        var content = RtfReader.Read(RtfWithPlaceholders(out var placeholders));
        content.Bookmarks.AddRange(placeholders.Bookmarks);
        content.Footnotes.AddRange(placeholders.Footnotes);
        content.Images.AddRange(placeholders.Images);
        content.Comments.AddRange(placeholders.Comments);
        MarkHeaderRows(content);
        return content;
    }

    // ---------- Commands ----------

    private void AddBookmark()
    {
        int position = _text.SelectionStart;
        // Proposal: the first words of the paragraph, without cutting one.
        var proposed = Summary(_text.Text, CurrentParagraphStart()).TrimEnd('.');
        if (proposed.Length > 30)
            proposed = proposed[..(proposed.LastIndexOf(' ', 30) is var space and > 0 ? space : 30)];
        if (proposed == "(riga vuota)")
            proposed = $"Segnalibro {_bookmarks.Count + 1}";

        using var window = new NameDialog("Aggiungi segnalibro", "&Nome del segnalibro:", proposed);
        if (window.ShowDialog(this) != DialogResult.OK || Tom is not { } tom)
            return;
        var name = window.EnteredName;
        // A name used again moves the bookmark here, as in Word.
        bool moved = _bookmarks.RemoveAll(s => string.Equals(s.Name, name, StringComparison.CurrentCultureIgnoreCase)) > 0;
        _bookmarks.Add((name, tom.Range(position, position)));
        _text.Modified = true;
        AnnounceShortly(moved ? $"Segnalibro {name} spostato qui" : $"Segnalibro {name} aggiunto");
    }

    private void NearbyBookmark(bool forward)
    {
        var bookmarks = BookmarkList();
        int here = _text.SelectionStart;
        var found = forward ? bookmarks.Where(s => s.Position > here).Cast<(string, int)?>().FirstOrDefault()
                             : bookmarks.Where(s => s.Position < here).Cast<(string, int)?>().LastOrDefault();
        if (found is var (name, position))
            GoToBookmark(name, position);
        else
            _announcer.Announce(bookmarks.Count == 0 ? "Nessun segnalibro nel documento" : forward ? "Nessun segnalibro dopo" : "Nessun segnalibro prima");
    }

    private void GoToBookmark(string name, int position)
    {
        _text.Select(position, 0);
        _text.ScrollToCaret();
        _announcer.Announce($"Segnalibro {name}. {DescribeLine(_text.Text, position)}");
    }

    /// <summary>Deletes the bookmark where the cursor is or, if there is none, the first one of the paragraph.</summary>
    private void DeleteBookmark()
    {
        int here = _text.SelectionStart, start = CurrentParagraphStart();
        int end = _text.Text.IndexOf('\n', here) is var f and >= 0 ? f : _text.TextLength;
        var candidates = BookmarkList().Where(s => s.Position >= start && s.Position <= end).ToList();
        if (candidates.Count == 0)
        {
            _announcer.Announce("Nessun segnalibro in questo paragrafo");
            return;
        }
        var chosen = candidates.OrderBy(s => Math.Abs(s.Position - here)).First();
        _bookmarks.RemoveAll(s => s.Name == chosen.Name);
        _text.Modified = true;
        _announcer.Announce($"Segnalibro {chosen.Name} eliminato");
    }

    /// <summary>
    /// Inserisci > Segnalibri > Riassunto dai segnalibri: the paragraphs that contain a bookmark, in document order and
    /// with their formatting, gathered in a new document in another window. For studying: mark the key passages,
    /// then get them all together.
    /// </summary>
    private void BookmarkSummary()
    {
        var bookmarks = BookmarkList();
        if (bookmarks.Count == 0)
        {
            _announcer.Announce("Nel documento non ci sono segnalibri");
            return;
        }
        var summary = SummaryOf(_text.Text, ParagraphsWithPosition().Select(p => p.Paragraph).ToList(),
            bookmarks.Select(b => b.Position), DocumentTitle);

        var name = $"Riassunto di {DocumentTitle}";
        var window = EleuContext.Open();
        window.LoadContent(summary);
        window.SetCurrentDocument(null, FileFormat.Docx, Path.Combine(InitialFolder(), name + ".docx"));
        window._text.Modified = true;
        window.UpdateTitle();
        int paragraphs = summary.Paragraphs.Count - 1;
        window.AnnounceShortly($"{name}: {(paragraphs == 1 ? "1 paragrafo" : $"{paragraphs} paragrafi")}, uno per ogni punto segnato");
    }

    /// <summary>
    /// The summary document: a title, then every paragraph that contains one of the positions (once, in order).
    /// <paramref name="paragraphs"/> are the document paragraphs, one for each paragraph of <paramref name="text"/>.
    /// </summary>
    public static DocumentContent SummaryOf(string text, IReadOnlyList<Paragraph> paragraphs, IEnumerable<int> positions, string title)
    {
        var spans = TextStructure.Paragraphs(text);
        var chosen = positions.Select(p => TextStructure.ParagraphOf(spans, Math.Clamp(p, 0, text.Length)))
            .Where(i => i < paragraphs.Count).Distinct().Order();
        var summary = new DocumentContent();
        var heading = new Paragraph { HeadingLevel = 1 };
        heading.Add(new Segment($"Riassunto di {title}"));
        summary.Paragraphs.Add(heading);
        foreach (var i in chosen)
        {
            var p = paragraphs[i];
            if (p.Text.Trim().Length == 0)
                continue;
            // Out of its table and without its page break: here it is a paragraph among the others.
            p.Cell = null;
            p.PageBreak = false;
            summary.Paragraphs.Add(p);
        }
        return summary;
    }

    private void ShowBookmarkList()
    {
        var bookmarks = BookmarkList();
        var starts = Pagination.PageStarts(_text, _page);
        var text = _text.Text;
        // The "position" of each item is its number in the list: several bookmarks can be at the same point.
        var items = bookmarks.Select((s, i) => new ListItem(
            [s.Name, $"Pagina {Pagination.PageOf(starts, s.Position)}, {Summary(text, text.LastIndexOf('\n', Math.Max(0, s.Position - 1)) + 1)}"], i)).ToList();
        using var window = new ListDialog("Segnalibri", ["Nome", "Dove"], items);
        window.ShowDialog(this);
        if (window.ToDelete.Count > 0)
        {
            var names = window.ToDelete.Select(i => bookmarks[i].Name).ToHashSet();
            _bookmarks.RemoveAll(s => names.Contains(s.Name));
            _text.Modified = true;
            AnnounceShortly(names.Count == 1 ? "Segnalibro eliminato" : $"{names.Count} segnalibri eliminati");
        }
        else if (window.Target is int index)
        {
            BeginInvoke(() => GoToBookmark(bookmarks[index].Name, bookmarks[index].Position));
        }
    }
}

/// <summary>Asks for a name (for example of a bookmark).</summary>
public sealed class NameDialog : DialogBase
{
    private readonly TextBox _name;

    public NameDialog(string heading, string label, string proposed) : base(heading, 280)
    {
        _name = Field(label, new TextBox { Text = proposed });
        AddButtons();
        ActiveControl = _name;
        _name.SelectAll();
        FormClosing += (_, e) =>
        {
            if (DialogResult == DialogResult.OK && EnteredName.Length == 0)
            {
                e.Cancel = true;
                MessageBox.Show(this, "Scrivi un nome.", heading, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        };
    }

    public string EnteredName => _name.Text.Trim();
}
