using Eleu.Proofing;
using Eleu.Documents;

namespace Eleu;

// The document's context menu (Applications key, Shift+F10 or right click): the commands useful at the point
// where you are. On a misspelled word the suggestions first; in a table, on a footnote or on an image
// their commands; always cut, copy, paste and the element's properties.
public sealed partial class MainWindow
{
    private ContextMenuStrip CreateContextMenu()
    {
        var menu = new ContextMenuStrip { AccessibleName = "Menu del documento" };
        menu.Opening += (_, e) =>
        {
            menu.Items.Clear();
            var error = ErrorHere();
            menu.AccessibleName = error is null ? "Menu del documento" : $"Correzione di {Spoken.Of(error.Word)}";
            menu.Items.AddRange([.. ContextItems(error)]);
            e.Cancel = false;
        };
        return menu;
    }

    private IEnumerable<ToolStripItem> ContextItems(LikelyError? error)
    {
        if (error is not null)
        {
            foreach (var item in ErrorItems(error))
                yield return item;
            yield return new ToolStripSeparator();
        }

        bool selection = _text.SelectionLength > 0;
        yield return ContextItem("&Taglia", "Ctrl+X", Cut, selection);
        yield return ContextItem("&Copia", "Ctrl+C", Copy, selection);
        yield return ContextItem("&Incolla", "Ctrl+V", Paste);
        yield return ContextItem("Seleziona t&utto", "Ctrl+A", () => _text.SelectAll());

        if (CellHere(warn: false) is not null)
        {
            yield return new ToolStripSeparator();
            var table = new ToolStripMenuItem("T&abella");
            table.DropDownItems.AddRange(
            [
                ContextItem("Inserisci riga sop&ra", null, () => InsertRow(bottom: false)),
                ContextItem("Inserisci riga so&tto", null, () => InsertRow(bottom: true)),
                ContextItem("Inserisci colonna a si&nistra", null, () => InsertColumn(onRight: false)),
                ContextItem("Inserisci colonna a &destra", null, () => InsertColumn(onRight: true)),
                ContextItem("Elimina ri&ga", null, DeleteRow),
                ContextItem("Elimina co&lonna", null, DeleteColumn),
                ContextItem("&Elimina tabella", null, DeleteTable),
            ]);
            yield return table;
        }
        if (FootnoteHere() is not null)
        {
            yield return new ToolStripSeparator();
            yield return ContextItem("&Modifica nota...", null, EditFootnote);
            yield return ContextItem("Elimina n&ota", null, DeleteFootnote);
        }
        if (CommentHere() is not null)
        {
            yield return new ToolStripSeparator();
            yield return ContextItem("A&scolta il commento", "Alt+Invio", AnnounceFormatting);
            yield return ContextItem("Modi&fica commento...", null, EditComment);
            yield return ContextItem("Elimina comme&nto", null, DeleteComment);
        }
        if (ImageHere() is not null)
        {
            yield return new ToolStripSeparator();
            yield return ContextItem("&Modifica immagine...", null, EditImage);
            yield return ContextItem("Elimina immagi&ne", null, DeleteImage);
            yield return ContextItem("Gra&fica in rilievo...", null, TactileGraphics);
        }

        yield return new ToolStripSeparator();
        yield return ContextItem("Ca&rattere...", "Ctrl+Maiusc+F", OpenCharacter);
        yield return ContextItem("&Paragrafo...", "Ctrl+Maiusc+P", OpenParagraph);
        yield return ContextItem("&Leggi", "F11", ReadOrPause);
        yield return ContextItem("D&ettagli del punto", "Alt+Invio", AnnounceFormatting);
    }

    /// <summary>
    /// The items for the error under the cursor: the suggestions (the first line says what kind of error it is, so
    /// the screen reader reads it when the menu opens), then Ignore and Add to dictionary.
    /// </summary>
    private IEnumerable<ToolStripItem> ErrorItems(LikelyError error)
    {
        void Correct(string proofing)
        {
            _text.Select(error.Start, error.Length);
            _text.SelectedText = proofing;
            AnnounceShortly(proofing.Any(char.IsLetterOrDigit) ? $"Corretto in {proofing}" : "Corretto");
        }

        List<string> suggestions = error.Reason is not null || Speller is null
            ? (error.Suggestion is null ? [] : [error.Suggestion])
            : [.. Speller.Suggestions(error.Word).Prepend(WithSuggestion(error).Suggestion).OfType<string>().Distinct().Take(5)];

        if (suggestions.Count == 0)
            yield return new ToolStripMenuItem($"{Describe(error)}") { Enabled = false };
        foreach (var suggestion in suggestions)
        {
            var text = suggestion.Length == 0 ? "Togli" : suggestion;
            var item = ContextItem(text.Replace("&", "&&"), null, () => Correct(suggestion));
            item.AccessibleDescription = error.Reason is null
                ? $"Correzione di {error.Word}"
                : $"{error.Reason}: {Spoken.Of(error.Word)}";
            yield return item;
        }
        yield return ContextItem("I&gnora tutti", null, () =>
        {
            _ignoredToday.Add(error.Word);
            _announcer.Announce($"{error.Word} ignorata fino alla chiusura di Eleu");
        });
        if (error.Reason is null)
            yield return ContextItem("Aggiungi al &dizionario", null, () =>
            {
                if (!_settings.PersonalDictionary.Contains(error.Word, StringComparer.CurrentCultureIgnoreCase))
                    _settings.PersonalDictionary.Add(error.Word);
                _settings.Save();
                _announcer.Announce($"{error.Word} aggiunta al dizionario personale");
            });
    }

    /// <summary>The spelling or punctuation error the cursor is in, searched only in its paragraph.</summary>
    private LikelyError? ErrorHere()
    {
        var text = _text.Text;
        if (text.Length == 0)
            return null;
        int here = _text.SelectionStart;
        var paragraphs = TextStructure.Paragraphs(text);
        var (start, end) = paragraphs[TextStructure.ParagraphOf(paragraphs, here)];
        var error = FindErrors(text[start..end]).FirstOrDefault(e => here - start >= e.Start && here - start <= e.Start + e.Length);
        return error is null ? null : error with { Start = error.Start + start };
    }

    /// <summary>
    /// A context menu item. The shortcut is only written next to it: if it were real, the menu would
    /// run it a second time together with the main menu's one.
    /// </summary>
    private ToolStripMenuItem ContextItem(string text, string? shortcut, Action action, bool activate = true) =>
        new(text, null, (_, _) => BeginInvoke(() => RunCommand(action)))
        {
            ShortcutKeyDisplayString = shortcut,
            ShowShortcutKeys = shortcut is not null,
            Enabled = activate,
        };
}
