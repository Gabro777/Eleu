using Eleu.Documents;

namespace Eleu.Tests;

internal static class Support
{
    private static readonly object OneWindowAtATime = new();

    /// <summary>
    /// WinForms controls must be used from an STA thread. One at a time: some parts of Windows Forms share
    /// data between threads and, with several tests creating windows together, they occasionally step on each other.
    /// </summary>
    public static void RunSta(Action action)
    {
        lock (OneWindowAtATime)
            RunStaAlone(action);
    }

    private static void RunStaAlone(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception e) { error = e; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null)
            throw new Exception("Errore nel thread STA", error);
    }

    /// <summary>Readable representation of the document, to compare it in tests.</summary>
    public static string Describe(DocumentContent content) =>
        string.Join("\n", content.Paragraphs.Select(p =>
            $"[T{p.HeadingLevel}{(p.PageBreak ? " nuova-pagina" : "")}{(p.Bulleted ? " elenco" : "")}{(p.Numbered ? " numerato" : "")} {p.Alignment}" +
            $" rientri {p.LeftIndent}/{p.RightIndent}/{p.FirstLineIndent} spazi {p.SpaceBefore}/{p.SpaceAfter} interlinea {p.LineSpacing}] " +
            string.Concat(p.Segments.Select(s =>
                $"<{(s.Bold ? "b" : "")}{(s.Italic ? "i" : "")}{(s.Underline ? "u" : "")}{(s.Strikethrough ? "s" : "")}" +
                $" {s.Size} {s.Font} {s.Position} colore={s.Color:X6} sfondo={s.Background:X6} spaz={s.Spacing}>{s.Text}"))));

    /// <summary>Sample document with everything Eleu can keep, with explicit sizes.</summary>
    public static DocumentContent Sample()
    {
        var content = new DocumentContent();

        var heading = new Paragraph { HeadingLevel = 1 };
        heading.Add(new Segment("Capitolo primo", Bold: true, Size: 18));
        content.Paragraphs.Add(heading);

        var text = new Paragraph { Alignment = Alignment.Justified };
        text.Add(new Segment("Testo ", Size: 12));
        text.Add(new Segment("grassetto", Bold: true, Size: 12));
        text.Add(new Segment(" e ", Size: 12));
        text.Add(new Segment("corsivo sottolineato", Italic: true, Underline: true, Size: 12));
        content.Paragraphs.Add(text);

        var point1 = new Paragraph { Bulleted = true };
        point1.Add(new Segment("primo punto", Size: 12));
        content.Paragraphs.Add(point1);

        var point2 = new Paragraph { Bulleted = true };
        point2.Add(new Segment("àèìòù € “virgolette” {graffe} \\ barra\tdopo tab", Size: 12));
        content.Paragraphs.Add(point2);

        var section = new Paragraph { HeadingLevel = 2, Alignment = Alignment.Center };
        section.Add(new Segment("Sezione", Bold: true, Size: 15));
        content.Paragraphs.Add(section);

        var right = new Paragraph { Alignment = Alignment.Right };
        right.Add(new Segment("Firma", Size: 20));
        content.Paragraphs.Add(right);

        // Character formatting: font, colors, strikethrough, superscript, subscript, spacing.
        var character = new Paragraph();
        character.Add(new Segment("Times ", Size: 12, Font: "Times New Roman"));
        character.Add(new Segment("rosso", Size: 12, Color: 0xC00000));
        character.Add(new Segment(" evidenziato", Size: 12, Background: 0xFFFF00));
        character.Add(new Segment(" barrato", Size: 12, Strikethrough: true));
        character.Add(new Segment(" m", Size: 12));
        character.Add(new Segment("2", Size: 12, Position: TextPosition.Superscript));
        character.Add(new Segment(" H", Size: 12));
        character.Add(new Segment("2", Size: 12, Position: TextPosition.Subscript));
        character.Add(new Segment("O spaziato", Size: 12, Spacing: 2));
        content.Paragraphs.Add(character);

        // Paragraph formatting: indents, spacing, line spacing.
        var indented = new Paragraph
        {
            LeftIndent = 36, RightIndent = 18, FirstLineIndent = 18,
            SpaceBefore = 6, SpaceAfter = 12, LineSpacing = 1.5f,
        };
        indented.Add(new Segment("Paragrafo rientrato con interlinea una e mezza.", Size: 12));
        content.Paragraphs.Add(indented);

        var hanging = new Paragraph { LeftIndent = 36, FirstLineIndent = -36, LineSpacing = 2 };
        hanging.Add(new Segment("Prima riga sporgente, interlinea doppia.", Size: 12));
        content.Paragraphs.Add(hanging);

        var newPage = new Paragraph { PageBreak = true };
        newPage.Add(new Segment("Questo paragrafo inizia su una pagina nuova.", Size: 12));
        content.Paragraphs.Add(newPage);

        foreach (var word in new[] { "uno", "due", "tre" })
        {
            var numbered = new Paragraph { Numbered = true };
            numbered.Add(new Segment(word, Size: 12));
            content.Paragraphs.Add(numbered);
        }

        // The text box always writes the font name: in the sample it is explicit everywhere.
        for (int i = 0; i < content.Paragraphs.Count; i++)
        {
            var rebuilt = content.Paragraphs[i].CopyProperties();
            foreach (var segment in content.Paragraphs[i].Segments)
                rebuilt.Add(segment with { Font = segment.Font ?? "Segoe UI" });
            content.Paragraphs[i] = rebuilt;
        }

        return content;
    }
}
