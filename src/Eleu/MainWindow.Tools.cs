using System.Globalization;
using System.Text;
using Eleu.Proofing;
using Eleu.Documents;
using Eleu.UI;

namespace Eleu;

// Writing tools: text cleanup, multiple replacements, paragraph sorting,
// full statistics and comparison with another document.
public sealed partial class MainWindow
{
    /// <summary>
    /// Applies the edits from the end to the start, so the positions still to do do not move; each
    /// edit touches only its piece and the formatting of the rest stays.
    /// </summary>
    private void ApplyEdits(IReadOnlyList<Edit> edits)
    {
        int cursor = _text.SelectionStart;
        foreach (var m in edits.OrderByDescending(m => m.Start))
        {
            _text.Select(m.Start, m.Length);
            _text.SelectedText = m.New;
            if (m.Start < cursor)
                cursor += m.New.Length - Math.Min(m.Length, cursor - m.Start);
        }
        _text.Select(Math.Clamp(cursor, 0, _text.TextLength), 0);
        if (edits.Count > 0)
            _text.Modified = true;
    }

    private static string Corrections(int n) => n switch
    {
        0 => "Nessuna correzione da fare",
        1 => "1 correzione fatta",
        _ => $"{n} correzioni fatte",
    };

    private void TextCleanup()
    {
        bool selection = _text.SelectionLength > 0;
        using var window = new CleanupDialog(_settings.Cleanup, selection);
        if (window.ShowDialog(this) != DialogResult.OK)
            return;
        _settings.Cleanup = window.Options;
        int from = window.SelectionOnly ? _text.SelectionStart : 0;
        int to = window.SelectionOnly ? _text.SelectionStart + _text.SelectionLength : -1;
        var edits = Cleanup.Edits(_text.Text, window.Options, from, to);
        ApplyEdits(edits);
        AnnounceShortly(Corrections(edits.Count));
    }

    private void OpenMultipleReplacements()
    {
        using var window = new ReplacementsDialog(_settings.Replacements, _settings.WholeWordReplacements);
        if (window.ShowDialog(this) != DialogResult.OK)
            return;
        _settings.Replacements = window.Pairs;
        _settings.WholeWordReplacements = window.WholeWords;
        _settings.Save();
        var edits = MultipleReplacements.Edits(_text.Text, window.Pairs,
            new SearchOptions(MatchCase: true, WholeWord: window.WholeWords));
        ApplyEdits(edits);
        AnnounceShortly(edits.Count switch
        {
            0 => "Nessuna sostituzione da fare",
            1 => "1 sostituzione fatta",
            var n => $"{n} sostituzioni fatte",
        });
    }

    /// <summary>
    /// Sorts the selected paragraphs (even partly selected) alphabetically, with their formatting.
    /// Numbers are sorted as numbers.
    /// </summary>
    private void SortParagraphs()
    {
        var text = _text.Text;
        var paragraphs = TextStructure.Paragraphs(text);
        int first = TextStructure.ParagraphOf(paragraphs, _text.SelectionStart);
        int last = TextStructure.ParagraphOf(paragraphs, Math.Max(_text.SelectionStart, _text.SelectionStart + _text.SelectionLength - 1));
        if (last <= first)
        {
            _announcer.Announce("Seleziona almeno due paragrafi da ordinare");
            return;
        }
        int start = paragraphs[first].Start, end = paragraphs[last].End;
        if (text.AsSpan(start, end - start).ContainsAny([TextStructure.RowStart, TextStructure.CellEnd]))
        {
            _announcer.Announce("Nella selezione c'è una tabella: per le tabelle usa Inserisci, Tabelle, Ordina righe");
            return;
        }
        _text.Select(start, end - start);
        var model = RtfReader.Read(_text.SelectedRtf ?? "");
        var comparison = new NaturalComparer();
        var sorted = model.Paragraphs.OrderBy(p => p.Text.Trim(), comparison).ToList();
        model.Paragraphs.Clear();
        model.Paragraphs.AddRange(sorted);
        // The last selected paragraph has no line break in the selection: it is removed from the new text too.
        var rtf = RtfWriter.Write(model, _settings.BaseFont, _settings.BaseSize);
        int lastParagraph = rtf.LastIndexOf("\\par", StringComparison.Ordinal);
        if (lastParagraph >= 0)
            rtf = rtf.Remove(lastParagraph, 4);
        _text.SelectedRtf = rtf;
        _text.Select(start, 0);
        _text.Modified = true;
        _announcer.Announce($"{sorted.Count} paragrafi in ordine alfabetico. {DescribeLine(_text.Text, start)}");
    }

    /// <summary>Italian alphabetical order, with numbers compared as numbers ("9" before "10").</summary>
    private sealed class NaturalComparer : IComparer<string>
    {
        private readonly CompareInfo _italian = CultureInfo.GetCultureInfo("it-IT").CompareInfo;

        public int Compare(string? a, string? b) =>
            _italian.Compare(a, b, CompareOptions.IgnoreCase | CompareOptions.NumericOrdering);
    }

    private void ShowFullStatistics()
    {
        bool selection = _text.SelectionLength > 0;
        var statistics = FullStatistics.Compute(selection ? _text.SelectedText : _text.Text);
        var heading = selection ? "Statistiche della selezione" : "Statistiche del documento";
        using var window = new TextDialog(heading, string.Join(Environment.NewLine, statistics.Lines()));
        window.ShowDialog(this);
    }

    private void CompareDocument()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Confronta con un altro documento",
            Filter = "Documenti|*.docx;*.odt;*.rtf;*.txt|Tutti i file|*.*",
            InitialDirectory = InitialFolder(),
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        List<string> other;
        try
        {
            other = ParagraphsOfFile(dialog.FileName);
        }
        catch (Exception e) // At the user boundary: an unreadable file becomes a message.
        {
            MessageBox.Show(this, $"Impossibile leggere {Path.GetFileName(dialog.FileName)}.\n\n{e.Message}", AppName,
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        var here = ParagraphsWithPosition();
        var differences = Comparison.Differences(here.Select(p => WithoutPlaceholders(p.Paragraph.Text)).ToList(), other);
        if (differences.Count == 0)
        {
            AnnounceShortly($"I due documenti hanno lo stesso testo");
            return;
        }
        int PositionOf(Difference d)
        {
            int index = d.Kind == DifferenceKind.OnlyInOther ? d.Here + 1 : d.Here;
            return index < 0 ? 0 : index < here.Count ? here[index].Start : _text.TextLength;
        }
        var items = differences.Select((d, i) => new ListItem([d.Description], i)).ToList();
        using var window = new ListDialog($"Differenze con {Path.GetFileName(dialog.FileName)}: {differences.Count}", ["Differenza"], items, deletable: false);
        window.ShowDialog(this);
        if (window.Target is int choice)
        {
            var d = differences[choice];
            BeginInvoke(() =>
            {
                int position = PositionOf(d);
                _text.Select(position, 0);
                _text.ScrollToCaret();
                _announcer.Announce(d.Description);
            });
        }
    }

    /// <summary>The paragraphs of a DOCX, ODT, RTF or text file, without the bookmark, footnote and image placeholders.</summary>
    private static List<string> ParagraphsOfFile(string path)
    {
        IEnumerable<string> paragraphs;
        switch (FileFormats.FromPath(path))
        {
            case FileFormat.Docx:
                using (var stream = File.OpenRead(path))
                    paragraphs = DocxFormat.Read(stream).Paragraphs.Select(p => p.Text).ToList();
                break;
            case FileFormat.Odt:
                using (var stream = File.OpenRead(path))
                    paragraphs = OdtFormat.Read(stream).Paragraphs.Select(p => p.Text).ToList();
                break;
            case FileFormat.Rtf:
                paragraphs = RtfReader.Read(File.ReadAllText(path, Encoding.Latin1)).Paragraphs.Select(p => p.Text);
                break;
            default:
                paragraphs = TextFile.Read(File.ReadAllBytes(path)).Text.ReplaceLineEndings("\n").Split('\n');
                break;
        }
        return paragraphs.Select(WithoutPlaceholders).ToList();
    }

    private static string WithoutPlaceholders(string text) =>
        string.Concat(text.Where(c => c is < (char)0xE000 or > (char)0xF8FF));
}
