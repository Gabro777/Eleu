using Eleu.Documents;
using Eleu.Editor;

namespace Eleu.Braille;

public enum IssueKind
{
    ImageWithoutDescription,
    EmptyHeading,
    SymbolsOnly,
    NearlyEmptyPage,
    TableWithoutHeader,
    Untranslatable,
    LongWord,
}

/// <summary>Something that will come out badly in Braille, where it is in the text and whether Eleu can fix it.</summary>
public sealed record BrailleIssue(IssueKind Kind, int Position, string Description)
{
    /// <summary>Fixed without asking anything: symbol lines, almost empty pages, empty headings.</summary>
    public bool AutomaticFix => Kind is IssueKind.SymbolsOnly or IssueKind.NearlyEmptyPage or IssueKind.EmptyHeading;

    /// <summary>Fixed by asking the user (the image description).</summary>
    public bool Fixable => AutomaticFix || Kind == IssueKind.ImageWithoutDescription;
}

/// <summary>A paragraph of the text box: where it starts, its text, its heading level and whether it is in a table cell.</summary>
public sealed record CheckedParagraph(int Start, string Text, int HeadingLevel, bool InTable);

public sealed record CheckedImage(int Position, string Description);

/// <summary>A table: where it starts, its number (from 1), whether it has a header row, and its first row.</summary>
public sealed record CheckedTable(int Start, int Number, bool HasHeader, IReadOnlyList<string> FirstRow);

/// <summary>
/// Check before Braille: what would waste paper or be hard to read once embossed. Images without a description become
/// an empty "[Immagine]"; empty headings and lines of symbols only (separators like "*****") waste lines; a page break
/// after a line or two leaves an almost empty sheet; a table whose first row is already data gets the wrong column
/// names (in Braille the first row names the columns); characters liblouis cannot write disappear; words longer than
/// the line are cut.
/// </summary>
public static class BrailleCheck
{
    public static List<BrailleIssue> Check(string text, IReadOnlyList<CheckedParagraph> paragraphs, IReadOnlyList<CheckedImage> images,
        IReadOnlyList<CheckedTable> tables, int cellsPerLine, Func<string, string> translate)
    {
        var issues = new List<BrailleIssue>();

        foreach (var image in images.Where(i => string.IsNullOrWhiteSpace(i.Description)))
            issues.Add(new(IssueKind.ImageWithoutDescription, image.Position,
                "Immagine senza descrizione: in Braille resta solo la parola Immagine."));

        foreach (var p in paragraphs.Where(p => !p.InTable))
        {
            var content = Visible(p.Text);
            if (p.HeadingLevel > 0 && content.Length == 0)
                issues.Add(new(IssueKind.EmptyHeading, p.Start, $"Titolo {p.HeadingLevel} vuoto: occupa una riga senza dire niente."));
            else if (content.Length >= 3 && !content.Any(char.IsLetterOrDigit))
                issues.Add(new(IssueKind.SymbolsOnly, p.Start, $"Riga di soli simboli ({Shorten(content)}): in Braille è una riga di punti senza significato."));
        }

        NearlyEmptyPages(text, cellsPerLine, issues);

        foreach (var table in tables.Where(t => !t.HasHeader && t.FirstRow.Any(LooksLikeData)))
            issues.Add(new(IssueKind.TableWithoutHeader, table.Start,
                $"Tabella {table.Number}: la prima riga sembra già contenere dati, ma in Braille fa da nomi delle colonne. Aggiungi una riga con i nomi delle colonne."));

        Characters(text, paragraphs, translate, issues);
        LongWords(paragraphs, cellsPerLine, translate, issues);

        return [.. issues.OrderBy(i => i.Position).ThenBy(i => i.Kind)];
    }

    /// <summary>The text without placeholders, tab and the technical table characters.</summary>
    private static string Visible(string text) =>
        new string([.. text.Where(c => !char.IsControl(c) && c != '￼' && !(c >= '' && c <= ''))]).Trim();

    private static string Shorten(string text) => text.Length <= 12 ? text : text[..12] + "…";

    private static bool LooksLikeData(string cell) => cell.Trim().Length > 0 && cell.Trim().All(c => char.IsDigit(c) || c is ',' or '.' or '-' or '€' or '%' or ' ');

    /// <summary>A page break after so little text that the Braille sheet stays almost empty (one line or less).</summary>
    private static void NearlyEmptyPages(string text, int cellsPerLine, List<BrailleIssue> issues)
    {
        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != Pagination.PageBreak)
                continue;
            var before = Visible(TextStructure.Readable(text[start..i]).Replace('\n', ' '));
            if (before.Length > 0 && before.Length <= cellsPerLine && text[(i + 1)..].Trim().Length > 0)
                issues.Add(new(IssueKind.NearlyEmptyPage, i,
                    $"Interruzione di pagina dopo poche parole ({Shorten(before)}): lascia un foglio Braille quasi vuoto."));
            start = i + 1;
        }
    }

    /// <summary>Characters that come out as nothing in Braille (emoji, unusual symbols): the first place of each.</summary>
    private static void Characters(string text, IReadOnlyList<CheckedParagraph> paragraphs, Func<string, string> translate, List<BrailleIssue> issues)
    {
        var seen = new HashSet<string>();
        foreach (var p in paragraphs)
        {
            var visible = p.Text;
            for (int i = 0; i < visible.Length; i++)
            {
                int length = char.IsSurrogatePair(visible, i) ? 2 : 1;
                var symbol = visible.Substring(i, length);
                char c = visible[i];
                bool ordinary = c < 128 || char.IsLetter(c) || char.IsControl(c) || c == '￼' || c is >= '' and <= '';
                if (!ordinary && seen.Add(symbol) && translate(symbol).Trim(BrailleTranslator.EmptyCell).Length == 0)
                    issues.Add(new(IssueKind.Untranslatable, p.Start + i,
                        $"Il simbolo {symbol} (U+{char.ConvertToUtf32(visible, i):X4}) non ha una forma in Braille: sparisce."));
                i += length - 1;
            }
        }
    }

    /// <summary>Words (often web or e-mail addresses) longer than the Braille line: they will be cut.</summary>
    private static void LongWords(IReadOnlyList<CheckedParagraph> paragraphs, int cellsPerLine, Func<string, string> translate, List<BrailleIssue> issues)
    {
        foreach (var p in paragraphs)
        {
            int index = 0;
            foreach (var word in p.Text.Split(' ', '\t'))
            {
                if (word.Length * 2 > cellsPerLine && translate(word).Length > cellsPerLine)
                    issues.Add(new(IssueKind.LongWord, p.Start + index,
                        $"Parola più lunga della riga Braille ({Shorten(word)}): verrà spezzata su due righe."));
                index += word.Length + 1;
            }
        }
    }
}
