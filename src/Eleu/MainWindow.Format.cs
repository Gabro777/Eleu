using Eleu.Documents;
using Eleu.Editor;
using Eleu.UI;

namespace Eleu;

// Formatting, zoom and reading commands ("where am I").
public sealed partial class MainWindow
{
    private void ToggleEffect(NativeRichEdit.Effect effect, string name)
    {
        bool active = NativeRichEdit.EffectState(_text, effect) != true;
        NativeRichEdit.SetEffect(_text, effect, active);
        _announcer.Announce($"{name} {(active ? "attivato" : "disattivato")}");
    }

    private void ChangeSize(int steps)
    {
        NativeRichEdit.ChangeSize(_text, steps);
        var size = NativeRichEdit.Size(_text);
        _announcer.Announce(size is float d ? $"{d:0.#} punti" : steps > 0 ? "Carattere aumentato" : "Carattere ridotto");
    }

    private void SetHeading(int level)
    {
        int selectionStart = _text.SelectionStart;
        int selectionLength = _text.SelectionLength;

        // A heading applies to whole paragraphs.
        var text = _text.Text;
        int start = selectionStart == 0 ? 0 : text.LastIndexOf('\n', selectionStart - 1) + 1;
        int end = text.IndexOf('\n', selectionStart + selectionLength);
        if (end < 0)
            end = text.Length;

        _text.Select(start, end - start);
        NativeRichEdit.SetHeadingLevel(_text, level);
        // RichEdit's default style has its own look: Eleu's is imposed, the same as the DOCX one.
        NativeRichEdit.SetSize(_text, level > 0 ? DocumentContent.HeadingSize(level) : DocumentContent.BaseSize);
        NativeRichEdit.SetEffect(_text, NativeRichEdit.Effect.Bold, level > 0);
        NativeRichEdit.SetEffect(_text, NativeRichEdit.Effect.Italic, false);
        _text.Select(selectionStart, selectionLength);

        _text.Modified = true;
        _announcer.Announce(level > 0 ? $"Titolo {level}" : "Testo normale");
    }

    private void SetList(ListKind kind)
    {
        NativeRichEdit.SetParagraphFormat(_text, new ParagraphFormat(ListType: kind));
        _text.Modified = true;
        _announcer.Announce(kind switch
        {
            ListKind.Bulleted => "Elenco puntato",
            ListKind.Numbered => "Elenco numerato",
            _ => "Punti elenco eliminati",
        });
    }

    private void ToggleStrikethrough()
    {
        bool active = NativeRichEdit.ReadCharacterFormat(_text).Strikethrough != true;
        NativeRichEdit.SetCharacterFormat(_text, new CharacterFormat(Strikethrough: active));
        _text.Modified = true;
        _announcer.Announce(active ? "Barrato attivato" : "Barrato disattivato");
    }

    private void ToggleWordWrap()
    {
        _text.WordWrap = !_text.WordWrap;
        _settings.WordWrap = _text.WordWrap;
        _announcer.Announce(_text.WordWrap ? "A capo automatico attivato" : "A capo automatico disattivato");
    }

    private void SetLineSpacing(float lineSpacing, string name)
    {
        NativeRichEdit.SetParagraphFormat(_text, new ParagraphFormat(LineSpacing: lineSpacing));
        _text.Modified = true;
        _announcer.Announce($"Interlinea {name}");
    }

    private void OpenCharacter()
    {
        var characters = FontFamily.Families.Select(f => f.Name).Where(n => !n.StartsWith('@')).Order().ToList();
        using var window = new CharacterDialog(NativeRichEdit.ReadCharacterFormat(_text), characters);
        if (window.ShowDialog(this) != DialogResult.OK)
            return;
        NativeRichEdit.SetCharacterFormat(_text, window.Edits);
        _text.Modified = true;
        if (window.Default && window.Base.Character is string character)
        {
            _settings.BaseFont = character;
            _settings.BaseSize = window.Base.Size;
            _settings.Save();
        }
    }

    private void OpenParagraph()
    {
        using var window = new ParagraphDialog(NativeRichEdit.ReadParagraphFormat(_text), NativeRichEdit.HeadingLevel(_text));
        if (window.ShowDialog(this) != DialogResult.OK)
            return;
        NativeRichEdit.SetParagraphFormat(_text, window.Edits);
        if (window.HeadingLevel is int level)
            SetHeading(level);
        _text.Modified = true;
        if (window.Default)
        {
            _settings.BaseParagraph = window.All;
            _settings.Save();
        }
    }

    /// <summary>The selection, or the word the cursor is in if there is no selection.</summary>
    private (int Start, int Length) SelectionOrWord()
    {
        int start = _text.SelectionStart;
        int length = _text.SelectionLength;
        if (length > 0)
            return (start, length);
        var text = _text.Text;
        while (start > 0 && char.IsLetterOrDigit(text[start - 1]))
            start--;
        int end = _text.SelectionStart;
        while (end < text.Length && char.IsLetterOrDigit(text[end]))
            end++;
        return (start, end - start);
    }

    /// <summary>UPPERCASE, lowercase or Title Case on the selection or on the cursor's word.</summary>
    private void SetCase(Uppercase kind, string name)
    {
        var (start, length) = SelectionOrWord();
        if (length == 0)
        {
            _announcer.Announce("Nessuna parola da cambiare");
            return;
        }
        int selectionStart = _text.SelectionStart, selectionLength = _text.SelectionLength;
        NativeRichEdit.ToggleCase(_text, start, start + length, kind);
        _text.Select(selectionStart, selectionLength);
        _text.Modified = true;
        _announcer.Announce(name);
    }

    /// <summary>Cycles UPPERCASE, lowercase and Title Case.</summary>
    private void ToggleCase()
    {
        var (start, length) = SelectionOrWord();
        var part = _text.Text.Substring(start, length);
        if (part == part.ToUpperInvariant() && part != part.ToLowerInvariant())
            SetCase(Uppercase.Lower, "minuscolo");
        else if (part == part.ToLowerInvariant())
            SetCase(Uppercase.Initials, "Iniziali maiuscole");
        else
            SetCase(Uppercase.Upper, "MAIUSCOLO");
    }

    private void Align(Alignment alignment, string description)
    {
        NativeRichEdit.SetAlignment(_text, alignment);
        _text.Modified = true;
        _announcer.Announce(description);
    }

    private void ChangeZoom(float variation) => SetZoom(_settings.Zoom + variation);

    private void SetZoom(float zoom)
    {
        _settings.Zoom = Math.Clamp(zoom, Settings.MinZoom, Settings.MaxZoom);
        ApplyZoom();
        _announcer.Announce($"Zoom {_settings.Zoom * 100:0} per cento");
    }

    private void ApplyZoom()
    {
        // RichTextBox ignores a value equal to the one it thinks it has, but the real zoom resets
        // at every load: going through 1 forces it to reapply it.
        _text.ZoomFactor = 1f;
        _text.ZoomFactor = _settings.Zoom;
        FitBrailleLineToZoom();
        _bar.Enlarge(_settings.Zoom);
        UpdateBars();
    }

    private void AnnouncePosition()
    {
        var (page, total, _) = CurrentPage();
        var cell = TextStructure.CellAt(_text.Text, _text.SelectionStart) is { } c
            ? $"Tabella {c.TableNumber + 1}, {Coordinates(_text.Text, c)}, "
            : "";
        _announcer.Announce($"{cell}{CurrentPosition().Description()}, pagina {page} di {total}");
    }

    private void AnnounceFormatting()
    {
        _announcer.Announce(ImageDescriptionHere() + FootnoteDescriptionHere() + CommentDescriptionHere() + FormatDescription.Describe(
            NativeRichEdit.HeadingLevel(_text),
            NativeRichEdit.ReadCharacterFormat(_text),
            NativeRichEdit.ReadParagraphFormat(_text)));
    }

    private void AnnounceStatistics()
    {
        if (_text.SelectionLength > 0)
            _announcer.Announce($"Selezione: {Statistics.Compute(_text.SelectedText).Description()}");
        else
            _announcer.Announce(Statistics.Compute(_text.Text).Description());
    }
}
