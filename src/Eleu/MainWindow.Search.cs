using Eleu.Documents;
using Eleu.UI;

namespace Eleu;

// Find and replace.
public sealed partial class MainWindow
{
    private string _lastSearch = "";
    private string _lastReplacement = "";
    private SearchOptions _searchOptions = new();

    private void OpenFind(bool withReplacement)
    {
        var selection = _text.SelectedText;
        var initial = selection.Length is > 0 and < 100 && !selection.Contains('\n') ? selection : _lastSearch;

        using var window = new FindDialog(withReplacement, initial, _lastReplacement, _searchOptions);
        void Remember()
        {
            _lastSearch = window.SearchText;
            _lastReplacement = window.ReplacementText;
            _searchOptions = window.Options;
        }
        window.FindRequested += (_, _) =>
        {
            Remember();
            _announcer.Announce(Find(forward: true));
        };
        window.ReplaceRequested += (_, _) =>
        {
            Remember();
            ReplaceCurrent();
        };
        window.ReplaceAllRequested += (_, _) =>
        {
            Remember();
            ReplaceAll();
        };

        var outcome = window.ShowDialog(this);
        Remember();
        if (!withReplacement && outcome == DialogResult.OK)
            AnnounceShortly(Find(forward: true));
    }

    private void FindAndAnnounce(bool forward)
    {
        if (_lastSearch.Length == 0)
        {
            OpenFind(false);
            return;
        }
        _announcer.Announce(Find(forward));
    }

    /// <summary>Selects the next occurrence (wrapping to the start if needed) and returns what to announce.</summary>
    private string Find(bool forward)
    {
        if (_lastSearch.Length == 0)
            return "Nessun testo da cercare";

        var text = _text.Text;
        int from = forward ? _text.SelectionStart + _text.SelectionLength : _text.SelectionStart;
        var found = Search.FindMatch(text, _lastSearch, from, forward, _searchOptions);
        string prefix = "";
        if (found is null)
        {
            found = Search.FindMatch(text, _lastSearch, forward ? 0 : text.Length, forward, _searchOptions);
            prefix = forward ? "Ricerca ripresa dall'inizio. " : "Ricerca ripresa dalla fine. ";
        }
        if (found is not var (start, length))
            return $"{_lastSearch} non trovato";

        _text.Select(start, length);
        _text.ScrollToCaret();
        return prefix + DescribeLine(text, start);
    }

    /// <summary>
    /// Replaces the selected occurrence and moves to the next one (F4) or the previous one (Shift+F4).
    /// Without a previous search it opens the Replace window.
    /// </summary>
    private void ReplaceCurrent(bool forward = true)
    {
        if (_lastSearch.Length == 0)
        {
            OpenFind(true);
            return;
        }
        string prefix = "";
        if (Search.Matches(_text.SelectedText, _lastSearch, _searchOptions))
        {
            int start = _text.SelectionStart;
            _text.SelectedText = _lastReplacement;
            if (!forward)
                _text.Select(start, 0); // going backward, restart from before the replacement
            prefix = "Sostituito. ";
        }
        _announcer.Announce(prefix + Find(forward));
    }

    private void ReplaceAll()
    {
        var positions = Search.FindAllMatches(_text.Text, _lastSearch, _searchOptions);
        if (positions.Count == 0)
        {
            _announcer.Announce($"{_lastSearch} non trovato");
            return;
        }

        // From the end to the start, so the positions still to replace do not move.
        for (int i = positions.Count - 1; i >= 0; i--)
        {
            _text.Select(positions[i].Start, positions[i].Length);
            _text.SelectedText = _lastReplacement;
        }
        _text.Select(positions[0].Start, _lastReplacement.Length);

        _announcer.Announce(positions.Count == 1 ? "1 sostituzione eseguita" : $"{positions.Count} sostituzioni eseguite");
    }

    /// <summary>"Riga 3: testo della riga", to know where you got to without moving the cursor.</summary>
    private static string DescribeLine(string text, int index)
    {
        var paragraphs = TextStructure.Paragraphs(text);
        var (start, end) = paragraphs[TextStructure.ParagraphOf(paragraphs, Math.Clamp(index, 0, text.Length))];
        var row = TextStructure.Readable(text[start..end]).Trim();
        if (row.Length > 120)
            row = row[..120] + "...";
        // In a table the cell is said, not the line.
        if (TextStructure.CellAt(text, index) is { } cell)
            return $"Tabella {cell.TableNumber + 1}, riga {cell.Row + 1}, colonna {cell.Column + 1}: {(row.Length > 0 ? row : "vuota")}";
        return $"Riga {Position.Compute(text, index).Line}: {row}";
    }
}
