using Eleu.Accessibility;
using Eleu.Documents;
using Eleu.Editor;
using Eleu.UI;

namespace Eleu;

// Cues while moving through the text: arriving on a heading, a bookmark, a footnote, a comment or a page
// break Eleu makes it known with a short sound (or by voice, according to the Options), without covering the screen reader
// reading the line. A low sound warns when the cursor reaches a misspelled word.
// While typing the cues are silent: they are there to help orientation, not to disturb.
public sealed partial class MainWindow
{
    private int _lastLength = -1;
    private int _lastReportedParagraph = -1;
    private int _lastReportedFootnote;
    private int? _lastReportedComment;
    private (int Start, int End) _lastCheckedWord = (-1, -1);

    private void ReportElements()
    {
        if (_silentCells || !_text.Focused)
            return;
        var text = _text.Text;
        int here = Math.Min(_text.SelectionStart, text.Length);
        int start = here == 0 ? 0 : text.LastIndexOf('\n', here - 1) + 1;
        int end = text.IndexOf('\n', here);
        if (end < 0)
            end = text.Length;
        bool newParagraph = start != _lastReportedParagraph;
        _lastReportedParagraph = start;
        var word = WordAround(text, here);

        // Text changed: the user is typing or editing, no cues.
        if (text.Length != _lastLength)
        {
            _lastLength = text.Length;
            _lastCheckedWord = word;
            return;
        }

        var cues = new List<(Cue Sound, string Speech)>();
        var mode = _settings.ElementCue;
        if (mode != CueMode.None)
        {
            if (newParagraph)
            {
                int level = NativeRichEdit.HeadingLevel(_text);
                if (level > 0)
                    cues.Add((Cue.Heading, $"Titolo {level}"));
                if (text.AsSpan(start, end - start).Contains(Pagination.PageBreak))
                    cues.Add((Cue.PageBreak, "Interruzione di pagina"));
                if (_bookmarks.Count > 0)
                    foreach (var (name, position) in BookmarkList().Where(s => s.Position >= start && s.Position <= end))
                        cues.Add((Cue.Bookmark, $"Segnalibro {name}"));
            }
            int footnote = _footnotes.Count > 0 && FootnoteHere() is var (number, _, _, _) ? number : 0;
            if (footnote != _lastReportedFootnote && footnote > 0)
                cues.Add((Cue.Footnote, $"Nota {footnote}"));
            _lastReportedFootnote = footnote;
            var comment = _comments.Count > 0 ? CommentHere() : null;
            if (comment is var (_, commentData, commentStart, _) && commentStart != _lastReportedComment)
                cues.Add((Cue.Comment, commentData.Author.Length > 0 ? $"Commento di {commentData.Author}" : "Commento"));
            _lastReportedComment = comment?.Start;
        }

        if (word != _lastCheckedWord)
        {
            _lastCheckedWord = word;
            if (_settings.MisspelledWordSound && cues.Count == 0 && _text.SelectionLength == 0 && MisspelledWord(text[word.Start..word.End]))
            {
                Sounds.Play(Cue.MisspelledWord, _settings.SoundVolume);
                return;
            }
        }

        if (cues.Count == 0)
            return;
        if (mode is CueMode.Sound or CueMode.SpeechAndSound)
            Sounds.Play(cues[0].Sound, _settings.SoundVolume);
        if (mode is CueMode.Speech or CueMode.SpeechAndSound)
            _announcer.Announce(string.Join(", ", cues.Select(s => s.Speech)));
    }

    /// <summary>The word (letters, digits and inner apostrophes) containing the position, or an empty range.</summary>
    private static (int Start, int End) WordAround(string text, int position)
    {
        static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c is '\'' or '’';
        int start = position, end = position;
        while (start > 0 && IsWordChar(text[start - 1]))
            start--;
        while (end < text.Length && IsWordChar(text[end]))
            end++;
        while (start < end && !char.IsLetterOrDigit(text[start]))
            start++;
        while (end > start && !char.IsLetterOrDigit(text[end - 1]))
            end--;
        return (start, end);
    }

    private bool MisspelledWord(string word) =>
        word.Length > 1 && WordToCheck(word) && Speller is { } speller && !speller.IsCorrect(word);

    /// <summary>
    /// Whether a word must be checked: not if it is in the personal dictionary, ignored until closing, or if the
    /// Options say to skip uppercase acronyms and words with digits.
    /// </summary>
    private bool WordToCheck(string word)
    {
        if (_ignoredToday.Contains(word) || _settings.PersonalDictionary.Contains(word, StringComparer.CurrentCultureIgnoreCase))
            return false;
        if (_settings.IgnoreWithDigits && word.Any(char.IsDigit))
            return false;
        if (_settings.IgnoreUppercase && word.Length > 1 && word.Where(char.IsLetter).All(char.IsUpper) && word.Any(char.IsLetter))
            return false;
        return true;
    }

    /// <summary>A confirmation sound (end of reading, save), if the Options want it.</summary>
    private void ConfirmationSound(Cue cue)
    {
        if (_settings.ConfirmationSounds)
            Sounds.Play(cue, _settings.SoundVolume);
    }

    /// <summary>Strumenti > Opzioni.</summary>
    private void OpenOptions()
    {
        using var window = new OptionsDialog(_settings);
        if (window.ShowDialog(this) != DialogResult.OK)
            return;
        window.Apply();
        _settings.Save();
        NativeRichEdit.SpellCheckAsYouType(_text, _settings.UnderlineErrors);
        _recoveryTimer.Interval = _settings.RecoveryMinutes * 60_000;
        AnnounceShortly("Opzioni salvate");
    }
}
