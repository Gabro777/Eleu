using Eleu.Proofing;
using Eleu.Documents;
using Eleu.UI;

namespace Eleu;

// Proofing (Strumenti > Correzione): spelling and punctuation errors, one at a time with F7 or in the
// Spell check window; personal dictionary; errors underlined while typing.
public sealed partial class MainWindow
{
    /// <summary>Words not to report until Eleu is closed ("Ignora tutti").</summary>
    private readonly HashSet<string> _ignoredToday = new(StringComparer.CurrentCultureIgnoreCase);

    /// <summary>The errors in the text: spelling (except personal dictionary words) and punctuation.</summary>
    private List<LikelyError> FindErrors(string text)
    {
        var speller = Speller;
        var personal = new HashSet<string>(_settings.PersonalDictionary, StringComparer.CurrentCultureIgnoreCase);
        bool ShouldReport(string word) => !personal.Contains(word) && !_ignoredToday.Contains(word);

        var spelling = LikelyErrors.Find(text,
                t => speller?.UnknownWords(t) ?? [],
                p => speller?.Suggestions(p) ?? [],
                p => speller?.IsCorrect(p) ?? false)
            .Where(e => WordToCheck(e.Word));
        var punctuation = Punctuation.Find(text).Where(e => ShouldReport(e.Word));

        // If two errors overlap the spelling one is kept.
        var errors = new List<LikelyError>();
        foreach (var error in spelling.Concat(punctuation).OrderBy(e => e.Start).ThenBy(e => e.Reason is null ? 0 : 1))
            if (errors.Count == 0 || error.Start >= errors[^1].Start + errors[^1].Length)
                errors.Add(error);
        return errors;
    }

    /// <summary>The best suggestion for a spelling error, asked of the dictionary only when needed.</summary>
    private LikelyError WithSuggestion(LikelyError error) =>
        error.Suggestion is not null || error.Reason is not null || Speller is not { } speller
            ? error
            : error with { Suggestion = LikelyErrors.BestSuggestion(error.Word, speller.Suggestions(error.Word), speller.IsCorrect) };

    private static string Describe(LikelyError error)
    {
        var what = error.Reason is null ? error.Word : $"{error.Reason}: {Spoken.Of(error.Word)}";
        var tip = error.Suggestion switch
        {
            null => "nessun suggerimento",
            "" => "da togliere",
            var s => $"forse {Spoken.Of(s)}",
        };
        return $"{what}, {tip}";
    }


    /// <summary>F7 / Shift+F7: selects the next (or previous) error and announces it with the suggestion.</summary>
    private void GoToError(bool forward, string prefix = "")
    {
        var text = _text.Text;
        var errors = FindErrors(text);
        if (errors.Count == 0)
        {
            _lastError = null;
            _announcer.Announce(prefix + (Speller is null
                ? "Nessun errore. Il dizionario italiano di Windows non è installato: controllo solo punteggiatura ed errori tipici del riconoscimento."
                : "Nessun errore"));
            return;
        }

        int position = forward ? _text.SelectionStart + _text.SelectionLength : _text.SelectionStart;
        var error = LikelyErrors.Next(errors, position, forward);
        string restarted = "";
        if (error is null)
        {
            error = forward ? errors[0] : errors[^1];
            restarted = forward ? "Ricominciato dall'inizio. " : "Ricominciato dalla fine. ";
        }
        error = WithSuggestion(error);
        _lastError = error;

        _text.Select(error.Start, error.Length);
        _text.ScrollToCaret();
        _announcer.Announce($"{prefix}{restarted}{Describe(error)}. Riga {Position.Compute(text, error.Start).Line}");
    }

    /// <summary>Ctrl+F7: replaces the selected error with the suggestion and moves to the next one.</summary>
    private void CorrectWithSuggestion()
    {
        if (_lastError is not LikelyError error || _text.SelectionStart != error.Start || _text.SelectedText != error.Word)
        {
            _announcer.Announce("Prima vai a un errore con F7");
            return;
        }
        if (error.Suggestion is not string proofing)
        {
            _announcer.Announce("Nessun suggerimento: scrivi tu la correzione");
            return;
        }
        _text.SelectedText = proofing;
        GoToError(forward: true, proofing.Any(char.IsLetterOrDigit) ? $"Corretto in {proofing}. " : "Corretto. ");
    }

    /// <summary>
    /// Strumenti > Correzione > Controllo ortografico: a window that goes through the errors from the cursor on, with
    /// the suggestions, and lets you change, ignore or add the word to the personal dictionary.
    /// </summary>
    private void OpenSpellCheck()
    {
        using var window = new SpellingDialog();
        int from = _text.SelectionStart;
        int correct = 0;
        LikelyError? current = null;

        void Next()
        {
            var errors = FindErrors(_text.Text);
            var error = errors.FirstOrDefault(e => e.Start >= from) ?? (from > 0 ? errors.FirstOrDefault() : null);
            if (error is null)
            {
                window.End(correct == 0 ? "Nessun errore nel documento." : $"Controllo finito: {correct} correzioni.");
                return;
            }
            current = WithSuggestion(error);
            _text.Select(current.Start, current.Length);
            _text.ScrollToCaret();
            var suggestions = current.Reason is not null || Speller is null
                ? (current.Suggestion is null ? [] : [current.Suggestion])
                : Speller.Suggestions(current.Word).Prepend(current.Suggestion).OfType<string>().Distinct().Take(8).ToList();
            window.ShowError(current, Context(_text.Text, current), suggestions);
        }

        window.Change += (proofing, all) =>
        {
            if (current is null)
                return;
            if (all && current.Reason is null)
            {
                var occurrences = Search.FindAll(_text.Text, current.Word, new SearchOptions(MatchCase: true, WholeWord: true));
                foreach (var position in occurrences.OrderDescending())
                {
                    _text.Select(position, current.Word.Length);
                    _text.SelectedText = proofing;
                }
                correct += occurrences.Count;
            }
            else
            {
                _text.Select(current.Start, current.Length);
                _text.SelectedText = proofing;
                correct++;
            }
            from = current.Start + proofing.Length;
            Next();
        };
        window.Ignore += all =>
        {
            if (current is null)
                return;
            if (all)
                _ignoredToday.Add(current.Word);
            from = current.Start + current.Length;
            Next();
        };
        window.AddToDictionary += () =>
        {
            if (current is null || current.Reason is not null)
                return;
            if (!_settings.PersonalDictionary.Contains(current.Word, StringComparer.CurrentCultureIgnoreCase))
                _settings.PersonalDictionary.Add(current.Word);
            from = current.Start + current.Length;
            Next();
        };

        window.Shown += (_, _) => Next();
        window.ShowDialog(this);
        _text.Select(_text.SelectionStart, 0);
        if (correct > 0)
            AnnounceShortly(correct == 1 ? "1 correzione" : $"{correct} correzioni");
    }

    /// <summary>The sentence (or the piece of paragraph) around the error, to understand what it is about.</summary>
    private static string Context(string text, LikelyError error)
    {
        var paragraphs = TextStructure.Paragraphs(text);
        var (start, end) = paragraphs[TextStructure.ParagraphOf(paragraphs, error.Start)];
        int from = Math.Max(start, error.Start - 60), a = Math.Min(end, error.Start + error.Length + 60);
        return (from > start ? "..." : "") + TextStructure.Readable(text[from..a]).Trim() + (a < end ? "..." : "");
    }

    private void ManageDictionary()
    {
        using var window = new DictionaryDialog(_settings.PersonalDictionary);
        if (window.ShowDialog(this) != DialogResult.OK)
            return;
        _settings.PersonalDictionary = window.Words;
        _settings.Save();
        AnnounceShortly($"Dizionario personale: {window.Words.Count} parole");
    }

    private void ToggleErrorUnderline()
    {
        _settings.UnderlineErrors = !_settings.UnderlineErrors;
        Editor.NativeRichEdit.SpellCheckAsYouType(_text, _settings.UnderlineErrors);
        _announcer.Announce(_settings.UnderlineErrors ? "Errori sottolineati mentre scrivi" : "Errori non sottolineati");
    }
}
