using System.Text.RegularExpressions;
using Eleu.Documents;

namespace Eleu.Speech;

/// <summary>A piece of text to read, with its position in the document.</summary>
public sealed record Passage(string Text, int Start);

/// <summary>An audiobook chapter: it becomes an MP3 file.</summary>
public sealed record Chapter(string Title, IReadOnlyList<string> Paragraphs, bool StartsWithHeading = false);

public static partial class Passages
{
    /// <summary>Beyond this length a paragraph is split into sentences, so reading starts without waiting.</summary>
    public const int MaxLength = 400;

    /// <summary>
    /// Splits the text between <paramref name="start"/> and <paramref name="end"/> into passages to read one after another:
    /// one passage per paragraph, and long paragraphs split into sentences. Blank lines are skipped.
    /// </summary>
    public static List<Passage> FromText(string text, int start, int end)
    {
        start = Math.Clamp(start, 0, text.Length);
        end = Math.Clamp(end, start, text.Length);
        var passages = new List<Passage>();

        int position = start;
        while (position < end)
        {
            // In a table each cell is a paragraph of its own.
            int lineBreak = text.IndexOfAny(['\n', TextStructure.CellEnd], position, end - position);
            int paragraphEnd = lineBreak < 0 ? end : lineBreak;
            AddParagraph(text, position, paragraphEnd, passages);
            position = paragraphEnd + 1;
        }
        return passages;
    }

    private static void AddParagraph(string text, int start, int end, List<Passage> passages)
    {
        while (start < end)
        {
            int pieceEnd = end;
            if (end - start > MaxLength)
            {
                // Cut after the last sentence end within the limit; if there is none, at the last space.
                var window = text.AsSpan(start, MaxLength);
                int cut = window.LastIndexOfAny(".!?;:");
                if (cut < MaxLength / 4)
                    cut = window.LastIndexOf(' ');
                if (cut > 0)
                    pieceEnd = start + cut + 1;
                else
                    pieceEnd = start + MaxLength;
            }

            var piece = TextStructure.Readable(text[start..pieceEnd]);
            int leadingSpaces = piece.Length - piece.TrimStart().Length;
            if (piece.Trim().Length > 0)
                passages.Add(new Passage(piece.Trim(), start + leadingSpaces));
            start = pieceEnd;
        }
    }

    /// <summary>
    /// Splits the document into chapters on the headings of the highest level present
    /// (Heading 1 if there is one, otherwise Heading 2, ...). The text before the first heading
    /// becomes an "Introduzione" chapter; without headings there is a single chapter.
    /// </summary>
    public static List<Chapter> Chapters(DocumentContent content, string documentTitle)
    {
        var levels = content.Paragraphs.Where(p => p.HeadingLevel > 0).Select(p => p.HeadingLevel).ToList();
        int chapterLevel = levels.Count > 0 ? levels.Min() : 0;

        var chapters = new List<Chapter>();
        string heading = chapterLevel == 0 ? documentTitle : "Introduzione";
        var paragraphs = new List<string>();
        bool startsWithHeading = false;

        void Close()
        {
            if (paragraphs.Count > 0)
                chapters.Add(new Chapter(heading, paragraphs.ToList(), startsWithHeading));
            paragraphs.Clear();
        }

        foreach (var paragraph in content.Paragraphs)
        {
            var text = paragraph.Text.Trim();
            if (paragraph.HeadingLevel == chapterLevel && chapterLevel > 0)
            {
                Close();
                heading = text.Length > 0 ? text : $"Capitolo {chapters.Count + 1}";
                startsWithHeading = text.Length > 0;
            }
            if (text.Length > 0)
                paragraphs.Add(text);
        }
        Close();
        return chapters;
    }

    /// <summary>"03 - Il ritorno.mp3": numbered to stay in order, without characters forbidden in file names.</summary>
    public static string FileName(int number, int total, string heading)
    {
        var cleaned = ForbiddenCharacters().Replace(heading, " ");
        cleaned = MultipleSpaces().Replace(cleaned, " ").Trim().TrimEnd('.');
        if (cleaned.Length > 80)
            cleaned = cleaned[..80].Trim();
        if (cleaned.Length == 0)
            cleaned = "Capitolo";
        int digits = Math.Max(2, total.ToString().Length);
        return $"{number.ToString().PadLeft(digits, '0')} - {cleaned}.mp3";
    }

    [GeneratedRegex(@"[<>:""/\\|?*\x00-\x1F]")]
    private static partial Regex ForbiddenCharacters();

    [GeneratedRegex(@"\s+")]
    private static partial Regex MultipleSpaces();
}
