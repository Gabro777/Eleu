using Eleu.Braille;
using Eleu.Documents;
using Eleu.UI;

namespace Eleu;

// Check before Braille (Strumenti > Controllo per il Braille, and by itself before printing or saving Braille):
// what will come out badly once embossed, with the fixes.
public sealed partial class MainWindow
{
    private void CheckForBraille() => CheckBeforeBraille(continueText: null);

    /// <summary>
    /// Shows what will come out badly in Braille and lets the user go there or fix it. Returns true if Braille can go
    /// on: no issues, or the user chose to continue anyway.
    /// </summary>
    private bool CheckBeforeBraille(string? continueText)
    {
        int selected = 0;
        while (true)
        {
            var issues = BrailleIssues();
            if (issues.Count == 0)
            {
                if (continueText is null)
                    AnnounceShortly("Nessun problema: il documento è pronto per il Braille");
                return true;
            }
            var text = _text.Text;
            var lines = issues.Select(i => Position.Compute(text, i.Position).Line).ToList();
            BrailleIssue? chosen;
            CheckChoice choice;
            using (var dialog = new BrailleCheckDialog(issues, lines, continueText, selected))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                    return false;
                chosen = dialog.Chosen;
                choice = dialog.Choice;
            }

            switch (choice)
            {
                case CheckChoice.Continue:
                    return true;
                case CheckChoice.GoTo when chosen is not null:
                    _text.Select(Math.Min(chosen.Position, _text.TextLength), 0);
                    _text.ScrollToCaret();
                    AnnounceShortly(chosen.Description);
                    return false;
                case CheckChoice.Fix when chosen is not null:
                    selected = issues.IndexOf(chosen);
                    Fix(chosen);
                    break;
                case CheckChoice.FixAll:
                    // From the end, so the positions still to fix do not move.
                    var automatic = issues.Where(i => i.AutomaticFix).OrderByDescending(i => i.Position).ToList();
                    foreach (var issue in automatic)
                        Fix(issue);
                    _announcer.Announce(automatic.Count == 1 ? "1 correzione" : $"{automatic.Count} correzioni");
                    selected = 0;
                    break;
                default:
                    return false;
            }
        }
    }

    private List<BrailleIssue> BrailleIssues()
    {
        var text = _text.Text;
        var paragraphs = ParagraphsWithPosition()
            .Select(x => new CheckedParagraph(x.Start, x.Paragraph.Text, x.Paragraph.HeadingLevel, x.Paragraph.Cell is not null))
            .ToList();
        var images = ImageList().Select(i => new CheckedImage(i.Position, i.Image.Description)).ToList();
        var tables = TextStructure.Tables(text);
        var headers = HeaderRows(tables);
        var checkedTables = tables.Select((t, i) => new CheckedTable(t.Start, i + 1, headers[i] > 0,
            t.Rows.Count == 0 ? [] : [.. t.Rows[0].Select(c => text[c.Start..c.End])])).ToList();
        try
        {
            return BrailleCheck.Check(text, paragraphs, images, checkedTables, _settings.BrailleCells - _settings.BrailleMargin,
                BrailleTranslator.Translate);
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or InvalidOperationException)
        {
            return []; // without liblouis there is no Braille to check: printing says it by itself
        }
    }

    private void Fix(BrailleIssue issue)
    {
        var text = _text.Text;
        switch (issue.Kind)
        {
            case IssueKind.SymbolsOnly:
            {
                // The whole paragraph, with its line break.
                int end = text.IndexOf('\n', issue.Position);
                _text.Select(issue.Position, (end < 0 ? text.Length : end + 1) - issue.Position);
                _text.SelectedText = "";
                break;
            }
            case IssueKind.NearlyEmptyPage when issue.Position < text.Length && text[issue.Position] == Editor.Pagination.PageBreak:
                _text.Select(issue.Position, 1);
                _text.SelectedText = "";
                break;
            case IssueKind.EmptyHeading:
                _text.Select(issue.Position, 0);
                SetHeading(0);
                break;
            case IssueKind.ImageWithoutDescription:
                _text.Select(issue.Position, 0);
                EditImage();
                break;
        }
        _text.Modified = true;
    }
}
