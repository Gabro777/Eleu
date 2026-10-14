using System.Text;
using Eleu.Documents;

namespace Eleu;

// Selecting without holding Shift: F8 marks the start of the block, move with any keys you like and F8
// again selects up to there. Also select word, line and paragraph, and save the selection to a separate file.
public sealed partial class MainWindow
{
    private int? _blockStart;

    /// <summary>F8: the first time marks the start of the block, the second selects from the marked start to the cursor.</summary>
    private void MarkBlock()
    {
        int here = _text.SelectionStart + _text.SelectionLength;
        if (_blockStart is not int start || start > _text.TextLength)
        {
            _blockStart = _text.SelectionStart;
            _announcer.Announce("Inizio del blocco segnato. Spostati e premi di nuovo F8 per selezionare");
            return;
        }
        _blockStart = null;
        int from = Math.Min(start, here), a = Math.Max(start, here);
        _text.Select(from, a - from);
        AnnounceSelection();
    }

    /// <summary>Shift+F8: forgets the start of the block.</summary>
    private void CancelBlock()
    {
        bool hadBlock = _blockStart is not null;
        _blockStart = null;
        _announcer.Announce(hadBlock ? "Blocco annullato" : "Nessun blocco iniziato");
    }

    private void SelectWord()
    {
        var text = _text.Text;
        var (start, end) = WordAround(text, Math.Min(_text.SelectionStart, text.Length));
        if (end == start)
        {
            _announcer.Announce("Il cursore non è su una parola");
            return;
        }
        _text.Select(start, end - start);
        AnnounceSelection();
    }

    private void SelectLine()
    {
        int row = _text.GetLineFromCharIndex(_text.SelectionStart);
        int start = _text.GetFirstCharIndexFromLine(row);
        int after = _text.GetFirstCharIndexFromLine(row + 1);
        _text.Select(start, (after < 0 ? _text.TextLength : after) - start);
        AnnounceSelection();
    }

    private void SelectParagraph()
    {
        var text = _text.Text;
        var paragraphs = TextStructure.Paragraphs(text);
        var (start, end) = paragraphs[TextStructure.ParagraphOf(paragraphs, Math.Min(_text.SelectionStart, text.Length))];
        _text.Select(start, Math.Min(end + 1, text.Length) - start);
        AnnounceSelection();
    }

    /// <summary>Says how much was selected and how it starts, to know whether the block is the right one.</summary>
    private void AnnounceSelection()
    {
        var selection = TextStructure.Readable(_text.SelectedText).Trim();
        if (selection.Length == 0)
        {
            _announcer.Announce("Niente selezionato");
            return;
        }
        int words = selection.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        var start = selection.Length > 60 ? selection[..60] + "..." : selection;
        _announcer.Announce($"Selezionat{(words == 1 ? "a 1 parola" : $"e {words} parole")}: {start}");
    }

    /// <summary>File > Salva selezione: the selected part in a new file, with its formatting.</summary>
    private void SaveSelection()
    {
        if (_text.SelectionLength == 0)
        {
            _announcer.Announce("Nessun testo selezionato");
            return;
        }
        using var dialog = new SaveFileDialog
        {
            Title = "Salva selezione",
            Filter = FileFormats.SaveFilter,
            FilterIndex = (int)_format + 1,
            FileName = "Selezione",
            InitialDirectory = InitialFolder(),
            AddExtension = true,
            OverwritePrompt = true,
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        var rtf = _text.SelectedRtf ?? "";
        var model = RtfReader.Read(rtf);
        model.Page = _page;
        var format = FileFormats.FromPath(dialog.FileName);
        try
        {
            using var stream = new MemoryStream();
            switch (format)
            {
                case FileFormat.Text:
                    stream.Write(TextFile.Write(string.Join(Environment.NewLine,
                        Readable.From(model).Paragraphs.Select(p => p.Text.Replace(Editor.Pagination.PageBreak.ToString(), ""))), _textEncoding));
                    break;
                case FileFormat.Rtf:
                    stream.Write(Encoding.Latin1.GetBytes(rtf));
                    break;
                case FileFormat.Html:
                    stream.Write(new UTF8Encoding(false).GetBytes(HtmlFormat.Write(model)));
                    break;
                case FileFormat.Odt:
                    OdtFormat.Write(model, stream);
                    break;
                case FileFormat.Epub:
                    model.Metadata = model.Metadata with { Title = Path.GetFileNameWithoutExtension(dialog.FileName) };
                    EpubFormat.Write(model, stream);
                    break;
                default:
                    DocxFormat.Write(model, stream);
                    break;
            }
            File.WriteAllBytes(dialog.FileName, stream.ToArray());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"Impossibile salvare {Path.GetFileName(dialog.FileName)}.\n\n{e.Message}", AppName,
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        ConfirmationSound(Accessibility.Cue.Saved);
        AnnounceShortly($"Selezione salvata in {Path.GetFileName(dialog.FileName)}");
    }
}
