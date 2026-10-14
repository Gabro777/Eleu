using Eleu.Documents;
using Eleu.Editor;
using Eleu.UI;

namespace Eleu;

// Document elements with the same scheme for every kind: Add, Delete, Previous, Next,
// List. Plus Go to (F2 / Shift+F2 to repeat).
public sealed partial class MainWindow
{
    private int _goToKind; // 0 page, 1 line, 2 paragraph

    /// <summary>The document paragraphs with the position where they start in the text.</summary>
    private List<(Paragraph Paragraph, int Start)> ParagraphsWithPosition()
    {
        var paragraphs = RtfReader.Read(_text.Rtf ?? "").Paragraphs;
        var text = _text.Text;
        var starts = TextStructure.Paragraphs(text).Select(p => p.Start).ToList();
        return paragraphs.Select((p, i) => (p, i < starts.Count ? starts[i] : text.Length)).ToList();
    }

    private int CurrentParagraphStart()
    {
        var text = _text.Text;
        var paragraphs = TextStructure.Paragraphs(text);
        return paragraphs[TextStructure.ParagraphOf(paragraphs, Math.Min(_text.SelectionStart, text.Length))].Start;
    }

    /// <summary>Goes to the position and announces the line, with a prefix (e.g. "Titolo 2").</summary>
    private void GoAndAnnounce(int position, string prefix)
    {
        position = Pagination.FirstAfterTheBreak(_text.Text, Math.Clamp(position, 0, _text.TextLength));
        _text.Select(position, 0);
        _text.ScrollToCaret();
        _announcer.Announce($"{prefix}. {DescribeLine(_text.Text, position)}");
    }

    /// <summary>The next (or previous) element among the given ones, relative to the cursor's paragraph.</summary>
    private void GoToNearby(IReadOnlyList<int> positions, bool forward, string name, Func<int, string> prefix)
    {
        int here = CurrentParagraphStart();
        var found = forward ? positions.Where(p => p > here).Cast<int?>().FirstOrDefault()
                             : positions.Where(p => p < here).Cast<int?>().LastOrDefault();
        if (found is int position)
            GoAndAnnounce(position, prefix(position));
        else
            _announcer.Announce(positions.Count == 0 ? $"Nessun {name} nel documento" : forward ? $"Nessun {name} dopo" : $"Nessun {name} prima");
    }

    // ---------- Page breaks ----------

    private List<int> PageBreakPositions()
    {
        var text = _text.Text;
        var positions = new List<int>();
        for (int i = text.IndexOf(Pagination.PageBreak); i >= 0; i = text.IndexOf(Pagination.PageBreak, i + 1))
            positions.Add(i);
        return positions;
    }

    private void NearbyPageBreak(bool forward) =>
        GoToNearby(PageBreakPositions(), forward, "interruzione di pagina", _ => "Interruzione di pagina");

    /// <summary>Deletes the breaks in the selection, or the one of the cursor's paragraph.</summary>
    private void DeletePageBreaks()
    {
        var positions = _text.SelectionLength > 0
            ? PageBreakPositions().Where(p => p >= _text.SelectionStart && p < _text.SelectionStart + _text.SelectionLength).ToList()
            : PageBreakPositions().Where(p => p == CurrentParagraphStart() || p == _text.SelectionStart).Take(1).ToList();
        if (positions.Count == 0)
        {
            _announcer.Announce("Nessuna interruzione di pagina qui");
            return;
        }
        Delete(positions);
        _announcer.Announce(positions.Count == 1 ? "Interruzione di pagina eliminata" : $"{positions.Count} interruzioni di pagina eliminate");
    }

    private void Delete(IEnumerable<int> positions)
    {
        int cursor = _text.SelectionStart;
        foreach (int position in positions.OrderDescending())
        {
            _text.Select(position, 1);
            _text.SelectedText = "";
            if (position < cursor)
                cursor--;
        }
        _text.Select(Math.Min(cursor, _text.TextLength), 0);
        _text.Modified = true;
    }

    private void ShowPageBreakList()
    {
        var positions = PageBreakPositions();
        var starts = Pagination.PageStarts(_text, _page);
        var text = _text.Text;
        var items = positions.Select(p => new ListItem(
            [Summary(text, p + 1), $"Pagina {Pagination.PageOf(starts, p)}"], p)).ToList();
        using var window = new ListDialog("Interruzioni di pagina", ["Descrizione", "Posizione"], items);
        window.ShowDialog(this);
        if (window.ToDelete.Count > 0)
        {
            Delete(window.ToDelete);
            AnnounceShortly(window.ToDelete.Count == 1 ? "Interruzione di pagina eliminata" : $"{window.ToDelete.Count} interruzioni di pagina eliminate");
        }
        else if (window.Target is int target)
        {
            BeginInvoke(() => GoAndAnnounce(target, "Interruzione di pagina"));
        }
    }

    /// <summary>The text of the line starting at the position, shortened: the "description" of an element.</summary>
    private static string Summary(string text, int start)
    {
        start = Math.Clamp(start, 0, text.Length);
        int end = text.IndexOfAny(['\n', TextStructure.CellEnd], start);
        var row = TextStructure.Readable(text[start..(end < 0 ? text.Length : end)])
            .Replace(Pagination.PageBreak.ToString(), "").Trim();
        return row.Length > 70 ? row[..70] + "..." : row.Length == 0 ? "(riga vuota)" : row;
    }

    // ---------- Headings ----------

    private void AddHeading()
    {
        int current = NativeRichEdit.HeadingLevel(_text);
        using var window = new HeadingLevelDialog(current == 0 ? 1 : current);
        if (window.ShowDialog(this) == DialogResult.OK)
            SetHeading(window.Level);
    }

    private List<(Paragraph Paragraph, int Start)> Headings() =>
        ParagraphsWithPosition().Where(x => x.Paragraph.HeadingLevel > 0).ToList();

    private void NearbyHeading(bool forward)
    {
        var headings = Headings();
        GoToNearby(headings.Select(t => t.Start).ToList(), forward, "titolo",
            p => $"Titolo {headings.First(t => t.Start == p).Paragraph.HeadingLevel}");
    }

    private void ShowHeadingList()
    {
        var headings = Headings();
        var items = headings.Select(t => new ListItem(
            [$"{new string(' ', (t.Paragraph.HeadingLevel - 1) * 2)}{t.Paragraph.Text.Trim()}", $"Titolo {t.Paragraph.HeadingLevel}"], t.Start)).ToList();
        using var window = new ListDialog("Titoli", ["Titolo", "Livello"], items);
        window.ShowDialog(this);
        foreach (int position in window.ToDelete)
        {
            _text.Select(position, 0);
            SetHeading(0);
        }
        if (window.ToDelete.Count > 0)
            AnnounceShortly(window.ToDelete.Count == 1 ? "Titolo eliminato" : $"{window.ToDelete.Count} titoli eliminati");
        else if (window.Target is int target)
            BeginInvoke(() => GoAndAnnounce(target, $"Titolo {headings.First(t => t.Start == target).Paragraph.HeadingLevel}"));
    }


    /// <summary>
    /// Inserts the table of contents at the start of the paragraph: one paragraph per heading, "Titolo, pagina N",
    /// indented by level. It is normal text: if the document changes, insert it again.
    /// </summary>
    private void InsertTableOfContents()
    {
        var headings = Headings();
        if (headings.Count == 0)
        {
            _announcer.Announce("Nessun titolo nel documento: il sommario si crea dai titoli");
            return;
        }
        int start = CurrentParagraphStart();
        string Rows(Func<int, int> page) =>
            string.Concat(headings.Select((t, i) => $"{t.Paragraph.Text.Replace(Pagination.PageBreak.ToString(), "").Trim()}, pagina {page(i)}\n"));

        // Insert first, then count the pages: the table of contents itself can push the headings further on.
        var temporary = Rows(_ => 0);
        _text.Select(start, 0);
        _text.SelectedText = temporary;
        var starts = Pagination.PageStarts(_text, _page);
        var pages = headings.Select(t => Pagination.PageOf(starts, t.Start >= start ? t.Start + temporary.Length : t.Start)).ToList();
        var tableOfContents = Rows(i => pages[i]);
        _text.Select(start, temporary.Length);
        _text.SelectedText = tableOfContents;

        // Normal text lines, indented by a good half centimeter for each level below the first.
        int position = start;
        foreach (var (row, heading) in tableOfContents.TrimEnd('\n').Split('\n').Zip(headings))
        {
            _text.Select(position, 0);
            NativeRichEdit.SetHeadingLevel(_text, 0);
            NativeRichEdit.SetParagraphFormat(_text, new ParagraphFormat(
                LeftIndent: (heading.Paragraph.HeadingLevel - 1) * 18f, FirstLineIndent: 0, ListType: ListKind.None));
            position += row.Length + 1;
        }
        _text.Select(start, 0);
        _announcer.Announce($"Sommario inserito, {(headings.Count == 1 ? "1 titolo" : $"{headings.Count} titoli")}. {DescribeLine(_text.Text, start)}");
    }
    // ---------- Lists ----------

    private void NearbyListItem(bool forward)
    {
        var points = ParagraphsWithPosition().Where(x => x.Paragraph.Bulleted || x.Paragraph.Numbered).Select(x => x.Start).ToList();
        GoToNearby(points, forward, "punto elenco", _ => "Punto elenco");
    }

    // ---------- Go to ----------

    private void OpenGoTo()
    {
        int number = _goToKind switch
        {
            0 => CurrentPage().Page,
            1 => _text.GetLineFromCharIndex(_text.SelectionStart) + 1,
            _ => CurrentPosition().Line,
        };
        using var window = new GoToDialog(_goToKind, number);
        if (window.ShowDialog(this) != DialogResult.OK)
            return;
        _goToKind = window.Target;
        int @value = window.Value;
        BeginInvoke(() => GoToNumber(_goToKind, @value));
    }

    private void GoToNumber(int kind, int number)
    {
        switch (kind)
        {
            case 0:
                var (_, _, starts) = CurrentPage();
                GoToPage(number, starts);
                break;
            case 1:
                int rowStart = _text.GetFirstCharIndexFromLine(number - 1);
                if (rowStart < 0)
                    _announcer.Announce($"Il documento ha {_text.GetLineFromCharIndex(_text.TextLength) + 1} righe");
                else
                    GoAndAnnounce(rowStart, $"Riga {number}");
                break;
            default:
                var paragraphs = ParagraphsWithPosition();
                if (number > paragraphs.Count)
                    _announcer.Announce($"Il documento ha {paragraphs.Count} paragrafi");
                else
                    GoAndAnnounce(paragraphs[number - 1].Start, $"Paragrafo {number}");
                break;
        }
    }

    /// <summary>F2 / Shift+F2: repeats the last Go to on the next or previous element.</summary>
    private void GoToNext(bool forward)
    {
        switch (_goToKind)
        {
            case 0:
                NextPage(forward ? +1 : -1);
                break;
            case 1:
                int row = _text.GetLineFromCharIndex(_text.SelectionStart) + (forward ? 2 : 0);
                if (row < 1 || _text.GetFirstCharIndexFromLine(row - 1) < 0)
                    _announcer.Announce(forward ? "Ultima riga" : "Prima riga");
                else
                    GoToNumber(1, row);
                break;
            default:
                var paragraphs = ParagraphsWithPosition().Select(p => p.Start).ToList();
                GoToNearby(paragraphs, forward, "paragrafo", p => $"Paragrafo {paragraphs.IndexOf(p) + 1}");
                break;
        }
    }
}

/// <summary>Inserisci > Titoli > Aggiungi titolo: chooses the heading level.</summary>
public sealed class HeadingLevelDialog : DialogBase
{
    private readonly ComboBox _level;

    public HeadingLevelDialog(int level) : base("Aggiungi titolo", 160)
    {
        _level = Field("&Livello:", ComboBox(Enumerable.Range(1, 9).Select(n => $"Titolo {n}"), level - 1));
        AddButtons();
        ActiveControl = _level;
    }

    public int Level => _level.SelectedIndex + 1;
}
