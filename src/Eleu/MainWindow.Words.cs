using System.Diagnostics;
using System.Globalization;
using Eleu.Documents;
using Eleu.UI;

namespace Eleu;

// Word helpers: insert a symbol that is not on the keyboard, look the word up on the web (encyclopedia,
// dictionary) and see the most used words in the document.
public sealed partial class MainWindow
{
    private void InsertSymbol()
    {
        using var window = new SymbolsDialog();
        if (window.ShowDialog(this) != DialogResult.OK || window.Chosen.Length == 0)
            return;
        _text.SelectedText = window.Chosen;
        AnnounceShortly($"Inserito {window.Chosen}");
    }

    /// <summary>Search addresses: {0} is the word, already escaped for the address.</summary>
    public static readonly IReadOnlyList<(string Name, string Url)> WebServices =
    [
        ("&Wikipedia", "https://it.wikipedia.org/w/index.php?search={0}"),
        ("&Vocabolario Treccani", "https://www.treccani.it/vocabolario/ricerca/{0}/"),
        ("Wi&kizionario (significati e sinonimi)", "https://it.wiktionary.org/w/index.php?search={0}"),
        ("&Google", "https://www.google.com/search?q={0}"),
    ];

    /// <summary>Searches the selection, or the word the cursor is in, in the browser.</summary>
    private void SearchOnWeb(string url)
    {
        var text = _text.Text;
        var find = _text.SelectionLength > 0
            ? TextStructure.Readable(_text.SelectedText).Trim()
            : WordAround(text, Math.Min(_text.SelectionStart, text.Length)) is var (start, end) ? text[start..end] : "";
        if (find.Length == 0)
        {
            _announcer.Announce("Seleziona il testo da cercare o porta il cursore su una parola");
            return;
        }
        if (find.Length > 200)
            find = find[..200];
        try
        {
            Process.Start(new ProcessStartInfo(string.Format(CultureInfo.InvariantCulture, url, Uri.EscapeDataString(find))) { UseShellExecute = true });
            _announcer.Announce($"Cerco {find} nel browser");
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            _announcer.Announce("Impossibile aprire il browser");
        }
    }

    /// <summary>Strumenti > Informazioni > Parole più usate: the words of at least three letters, most frequent first.</summary>
    private void MostUsedWords()
    {
        var text = TextStructure.Readable(_text.SelectionLength > 0 ? _text.SelectedText : _text.Text);
        var count = Frequencies(text);
        if (count.Count == 0)
        {
            _announcer.Announce("Nessuna parola");
            return;
        }
        var rows = new List<string>
        {
            $"{count.Count} parole diverse di almeno tre lettere{(_text.SelectionLength > 0 ? " nella selezione" : "")}.",
            "",
        };
        rows.AddRange(count.Take(200).Select(c => $"{c.Word}: {c.Times} {(c.Times == 1 ? "volta" : "volte")}"));
        using var window = new TextDialog("Parole più usate", string.Join(Environment.NewLine, rows));
        window.ShowDialog(this);
    }

    /// <summary>The words (lowercase, at least three letters) with how many times they appear, most used first.</summary>
    public static List<(string Word, int Times)> Frequencies(string text)
    {
        var count = new Dictionary<string, int>(StringComparer.CurrentCultureIgnoreCase);
        int i = 0;
        while (i < text.Length)
        {
            if (!char.IsLetter(text[i]))
            {
                i++;
                continue;
            }
            int start = i;
            while (i < text.Length && char.IsLetter(text[i]))
                i++;
            if (i - start >= 3)
            {
                var word = text[start..i].ToLower(CultureInfo.CurrentCulture);
                count[word] = count.GetValueOrDefault(word) + 1;
            }
        }
        return count.OrderByDescending(c => c.Value).ThenBy(c => c.Key, StringComparer.CurrentCulture)
            .Select(c => (c.Key, c.Value)).ToList();
    }
}
