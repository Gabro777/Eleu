namespace Eleu.Documents;

public enum Alignment { Left, Center, Right, Justified }

public enum TextPosition { Normal, Superscript, Subscript }

/// <summary>
/// A piece of text with the same formatting. Sizes and spacing in points;
/// colors as 0xRRGGBB, null = automatic (text color) or none (background).
/// </summary>
public sealed record Segment(
    string Text,
    bool Bold = false,
    bool Italic = false,
    bool Underline = false,
    float? Size = null,
    string? Font = null,
    bool Strikethrough = false,
    TextPosition Position = TextPosition.Normal,
    int? Color = null,
    int? Background = null,
    float Spacing = 0)
{
    public bool SameFormat(Segment other) => this with { Text = "" } == other with { Text = "" };
}

public sealed class Paragraph
{
    private readonly List<Segment> _segments = [];

    public IReadOnlyList<Segment> Segments => _segments;

    /// <summary>0 = normal text, 1-9 = Heading 1, Heading 2, ...</summary>
    public int HeadingLevel { get; set; }

    /// <summary>Bulleted list.</summary>
    public bool Bulleted { get; set; }

    /// <summary>Numbered list (1. 2. 3.), with automatic numbering.</summary>
    public bool Numbered { get; set; }

    public Alignment Alignment { get; set; }

    // Indents and spacing in points. Negative FirstLineIndent = hanging first line.
    public float LeftIndent { get; set; }

    public float RightIndent { get; set; }

    public float FirstLineIndent { get; set; }

    public float SpaceBefore { get; set; }

    public float SpaceAfter { get; set; }

    /// <summary>Line spacing as a multiple of single spacing: 1, 1.15, 1.5, 2.</summary>
    public float LineSpacing { get; set; } = 1f;

    /// <summary>If the paragraph is in a table cell: which table, row and column. A cell can have several paragraphs.</summary>
    public CellPosition? Cell { get; set; }

    /// <summary>
    /// The page of the print document where the paragraph starts (0 = unknown). Not saved: Braille uses it
    /// to mark where the print page changes.
    /// </summary>
    public int PrintPage { get; set; }

    /// <summary>The paragraph starts on a new page (page break before). Not carried over to the following paragraphs.</summary>
    public bool PageBreak { get; set; }

    public string Text => string.Concat(_segments.Select(s => s.Text));

    /// <summary>Adds the segment, merging it into the previous one if it has the same formatting.</summary>
    public void Add(Segment segment)
    {
        if (segment.Text.Length == 0)
            return;
        if (_segments.Count > 0 && _segments[^1].SameFormat(segment))
            _segments[^1] = _segments[^1] with { Text = _segments[^1].Text + segment.Text };
        else
            _segments.Add(segment);
    }

    /// <summary>Removes the spaces at the end of the paragraph (from the last segment, and from those left empty).</summary>
    public void RemoveTrailingSpaces()
    {
        while (_segments.Count > 0)
        {
            var last = _segments[^1];
            var cleaned = last.Text.TrimEnd(' ', '\t');
            if (cleaned.Length > 0)
            {
                _segments[^1] = last with { Text = cleaned };
                return;
            }
            _segments.RemoveAt(_segments.Count - 1);
        }
    }

    /// <summary>Resets the paragraph properties to their defaults, keeping the text.</summary>
    public void ResetProperties()
    {
        var @default = new Paragraph();
        HeadingLevel = @default.HeadingLevel;
        Bulleted = @default.Bulleted;
        Numbered = @default.Numbered;
        Alignment = @default.Alignment;
        LeftIndent = @default.LeftIndent;
        RightIndent = @default.RightIndent;
        FirstLineIndent = @default.FirstLineIndent;
        SpaceBefore = @default.SpaceBefore;
        SpaceAfter = @default.SpaceAfter;
        LineSpacing = @default.LineSpacing;
        PageBreak = false;
    }

    public Paragraph CopyProperties() => new()
    {
        HeadingLevel = HeadingLevel,
        Bulleted = Bulleted,
        Numbered = Numbered,
        Alignment = Alignment,
        LeftIndent = LeftIndent,
        RightIndent = RightIndent,
        FirstLineIndent = FirstLineIndent,
        SpaceBefore = SpaceBefore,
        SpaceAfter = SpaceAfter,
        LineSpacing = LineSpacing,
    };
}

/// <summary>
/// Document model independent of the file format: the bridge between
/// the text box (RTF) and the formats saved on disk (DOCX, TXT).
/// </summary>
public sealed class DocumentContent
{
    public List<Paragraph> Paragraphs { get; } = [];

    public PageLayout Page { get; set; } = new();

    /// <summary>Title, author and the rest of the document properties (File > Proprietà).</summary>
    public Metadata Metadata { get; set; } = new();

    /// <summary>
    /// Parts of the original file that Eleu cannot keep (e.g. "tabelle", "immagini"):
    /// saving over the original would lose them.
    /// </summary>
    public List<string> NotPreserved { get; } = [];

    /// <summary>Bookmark names; in the text each one is its placeholder (see <see cref="Documents.Bookmarks"/>).</summary>
    public List<string> Bookmarks { get; } = [];

    /// <summary>Footnote texts; in the text each one is its placeholder (see <see cref="Documents.FootnoteMarkers"/>).</summary>
    public List<string> Footnotes { get; } = [];

    /// <summary>Images; in the text each one is its placeholder (see <see cref="Documents.Images"/>).</summary>
    public List<EmbeddedImage> Images { get; } = [];

    /// <summary>Comments; in the text each one has a start placeholder and an end one (see <see cref="Documents.CommentMarkers"/>).</summary>
    public List<Comment> Comments { get; } = [];

    public static DocumentContent FromPlainText(string text)
    {
        var content = new DocumentContent();
        foreach (var row in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var paragraph = new Paragraph();
            paragraph.Add(new Segment(row));
            content.Paragraphs.Add(paragraph);
        }
        return content;
    }

    public const float BaseSize = 12f;

    public static float HeadingSize(int level) => level switch
    {
        1 => 18f,
        2 => 15f,
        3 => 13f,
        _ => BaseSize,
    };
}

/// <summary>
/// Where a paragraph is in a table. The document's tables are numbered from 0, rows and columns too.
/// Column width in points (0 = automatic); Header = the row repeats at the top of every page.
/// </summary>
public sealed record CellPosition(int Table, int Row, int Column, float Width = 0, bool Header = false)
{
    public bool SameCell(CellPosition? other) =>
        other is not null && other.Table == Table && other.Row == Row && other.Column == Column;

    public bool SameLine(CellPosition? other) =>
        other is not null && other.Table == Table && other.Row == Row;
}
