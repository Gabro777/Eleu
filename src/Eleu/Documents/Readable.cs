namespace Eleu.Documents;

/// <summary>
/// The document to read straight through, for Braille and audiobooks: bookmarks disappear, a footnote
/// reference becomes "[nota 1]", an image "[Immagine: descrizione]", tables are read row by row
/// (<see cref="Grid.Readable"/>) and at the end there is the "Note" chapter.
/// </summary>
public static class Readable
{
    public static DocumentContent From(DocumentContent content)
    {
        var result = new DocumentContent { Page = content.Page };
        foreach (var paragraph in content.Paragraphs)
        {
            var @new = paragraph.CopyProperties();
            @new.Cell = paragraph.Cell;
            @new.PageBreak = paragraph.PageBreak;
            @new.PrintPage = paragraph.PrintPage;
            foreach (var segment in paragraph.Segments)
                @new.Add(segment with { Text = Text(segment.Text, content) });
            result.Paragraphs.Add(@new);
        }
        result = Grid.Readable(result);

        if (content.Footnotes.Count > 0)
        {
            var heading = new Paragraph { HeadingLevel = 1, PageBreak = true };
            heading.Add(new Segment("Note"));
            result.Paragraphs.Add(heading);
            for (int i = 0; i < content.Footnotes.Count; i++)
            {
                var footnote = new Paragraph();
                footnote.Add(new Segment($"{i + 1}. {content.Footnotes[i].ReplaceLineEndings(" ")}"));
                result.Paragraphs.Add(footnote);
            }
        }
        return result;
    }

    private static string Text(string text, DocumentContent content)
    {
        var result = new System.Text.StringBuilder();
        foreach (var c in text)
        {
            if (Bookmarks.Index(c) >= 0 || CommentMarkers.Placeholder(c))
                continue;
            if (FootnoteMarkers.Index(c) is var footnote and >= 0)
                result.Append($" [nota {footnote + 1}]");
            else if (Images.Index(c) is var image and >= 0)
                result.Append(image < content.Images.Count && content.Images[image].Description.Length > 0
                    ? $" [Immagine: {content.Images[image].Description}] "
                    : " [Immagine senza descrizione] ");
            else
                result.Append(c);
        }
        return result.ToString();
    }
}
