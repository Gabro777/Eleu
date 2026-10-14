using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;
using W= DocumentFormat.OpenXml.Wordprocessing;

namespace Eleu.Documents;

/// <summary>
/// Reading and writing of Word documents (.docx).
/// Simple formatting is kept: bold, italic, underline, size,
/// alignment, headings and lists. Tables and images become plain text or are ignored.
/// </summary>
public static partial class DocxFormat
{
    /// <summary>Line separator in the text of a footnote.</summary>
    private const string LineBreak = "\n";

    private const int BulletListId = 1;
    private const int NumberedListId = 2;

    public static DocumentContent Read(Stream stream)
    {
        using var documents = WordprocessingDocument.Open(stream, false);
        var main = documents.MainDocumentPart;
        var body = main?.Document?.Body;
        var content = new DocumentContent();
        if (main is null || body is null)
            return content;

        // Simple tables yes; merged cells and tables inside tables no.
        if (body.Descendants<GridSpan>().Any(g => g.Val?.Value > 1) || body.Descendants<VerticalMerge>().Any()
            || body.Descendants<Table>().Any(t => t.Ancestors<Table>().Any()))
            content.NotPreserved.Add("celle unite o tabelle dentro le tabelle");
        var tableNumbers = new Dictionary<Table, int>();
        // Today's images (Drawing) yes; those in the old format (VML) and shapes without an image no.
        if (body.Descendants<Picture>().Any())
            content.NotPreserved.Add("immagini nel formato vecchio di Word");
        bool lostDrawings = false;
        if (main.EndnotesPart?.Endnotes?.Elements<Endnote>().Any(n => n.Id?.Value > 0) == true)
            content.NotPreserved.Add("note di chiusura");
        var footnoteTexts = main.FootnotesPart?.Footnotes?.Elements<Footnote>()
            .Where(n => n.Id?.Value > 0)
            .ToDictionary(n => n.Id!.Value, n => string.Join(LineBreak, n.Elements<W.Paragraph>().Select(ParagraphText)).Trim()) ?? [];
        // Tracked changes are read as already accepted (deleted text is not there, inserted text is).
        if (body.Descendants<InsertedRun>().Any() || body.Descendants<DeletedRun>().Any() || body.Descendants<MoveFromRun>().Any())
            content.NotPreserved.Add("revisioni (modifiche tracciate), che Eleu mostra già accettate");
        var commentData = main.WordprocessingCommentsPart?.Comments?.Elements<W.Comment>()
            .Where(c => c.Id?.Value is not null)
            .GroupBy(c => c.Id!.Value!).ToDictionary(g => g.Key, g => g.First()) ?? [];
        var commentIndexes = new Dictionary<string, int>();
        var closedComments = new HashSet<string>();
        content.Page = ReadPage(main, body);
        var properties = documents.PackageProperties;
        content.Metadata = new Metadata(properties.Title ?? "", properties.Creator ?? "", properties.Subject ?? "", properties.Keywords ?? "", properties.Created);

        var styles = main.StyleDefinitionsPart?.Styles;
        var numbering = main.NumberingDefinitionsPart?.Numbering;
        bool pendingBreak = false;

        foreach (var p in body.Descendants<W.Paragraph>())
        {
            var cell = CellAt(p, tableNumbers);
            int firstParagraph = content.Paragraphs.Count;
            ReadParagraph(p);
            // Paragraphs created by a line break inside the cell stay in the cell too.
            for (int i = firstParagraph; i < content.Paragraphs.Count; i++)
                content.Paragraphs[i].Cell = cell;
        }
        if (lostDrawings)
            content.NotPreserved.Add("immagini o disegni che Eleu non sa leggere");
        return content;

        // The comment with that Word number becomes the next one in the list; null if it is missing or there are too many.
        int? NewComment(string? id)
        {
            if (id is null || commentIndexes.ContainsKey(id) || content.Comments.Count >= CommentMarkers.Max || !commentData.TryGetValue(id, out var data))
                return null;
            content.Comments.Add(new Comment(
                string.Join(LineBreak, data.Elements<W.Paragraph>().Select(ParagraphText)).Trim(),
                data.Author?.Value ?? "",
                data.Date?.Value));
            commentIndexes[id] = content.Comments.Count - 1;
            return content.Comments.Count - 1;
        }

        void ReadParagraph(W.Paragraph p)
        {
            var properties = p.ParagraphProperties;
            var paragraph = new Paragraph
            {
                HeadingLevel = HeadingLevel(properties, styles),
                Alignment = ReadAlignment(properties?.Justification),
                PageBreak = pendingBreak || Active(properties?.PageBreakBefore),
            };
            pendingBreak = false;
            ReadSpacingAndIndents(properties, paragraph);

            switch (ReadList(properties?.NumberingProperties, numbering))
            {
                case ListKind.Bulleted:
                    paragraph.Bulleted = true;
                    break;
                case ListKind.Numbered:
                    paragraph.Numbered = true;
                    break;
            }
            if (paragraph.Bulleted || paragraph.Numbered)
            {
                // In lists the indent is decided by the numbering, not by the user.
                paragraph.LeftIndent = 0;
                paragraph.FirstLineIndent = 0;
            }

            // Only the runs (and bookmarks) of this paragraph, not those of nested text boxes.
            foreach (var part in p.Descendants().Where(e => e is Run or BookmarkStart or CommentRangeStart or CommentRangeEnd && e.Ancestors<W.Paragraph>().First() == p))
            {
                if (part is BookmarkStart bookmark)
                {
                    if (bookmark.Name?.Value is string name && !Bookmarks.Hidden(name) && content.Bookmarks.Count < Bookmarks.Max)
                    {
                        content.Bookmarks.Add(Bookmarks.NameFromWord(name));
                        paragraph.Add(new Segment(Bookmarks.Placeholder(content.Bookmarks.Count - 1).ToString()));
                    }
                    continue;
                }
                if (part is CommentRangeStart commentStart)
                {
                    if (NewComment(commentStart.Id?.Value) is int index)
                        paragraph.Add(new Segment(CommentMarkers.Start(index).ToString()));
                    continue;
                }
                if (part is CommentRangeEnd commentEnd)
                {
                    if (commentEnd.Id?.Value is string id && commentIndexes.TryGetValue(id, out int index) && closedComments.Add(id))
                        paragraph.Add(new Segment(CommentMarkers.End(index).ToString()));
                    continue;
                }
                var run = (Run)part;
                var format = ReadFormat(run.RunProperties);
                foreach (var element in run.ChildElements)
                {
                    switch (element)
                    {
                        case Text t:
                            paragraph.Add(format with { Text = t.Text });
                            break;
                        case TabChar:
                            paragraph.Add(format with { Text = "\t" });
                            break;
                        case Drawing drawing:
                            if (content.Images.Count < Images.Max && ReadDrawing(drawing, main) is { } image)
                            {
                                content.Images.Add(image);
                                paragraph.Add(new Segment(Images.Placeholder(content.Images.Count - 1).ToString()));
                            }
                            else
                            {
                                lostDrawings = true;
                            }
                            break;
                        case FootnoteReference reference when content.Footnotes.Count < FootnoteMarkers.Max:
                            content.Footnotes.Add(reference.Id?.Value is long id && footnoteTexts.TryGetValue(id, out var footnoteText) ? footnoteText : "");
                            paragraph.Add(new Segment(FootnoteMarkers.Placeholder(content.Footnotes.Count - 1).ToString()));
                            break;
                        case CommentReference commentReference when commentReference.Id?.Value is string referenceId && !commentIndexes.ContainsKey(referenceId):
                            // A comment at a point, with no commented text: start and end in the same place.
                            if (NewComment(referenceId) is int pointLike)
                            {
                                closedComments.Add(referenceId);
                                paragraph.Add(new Segment(CommentMarkers.Start(pointLike).ToString() + CommentMarkers.End(pointLike)));
                            }
                            break;
                        case NoBreakHyphen:
                            paragraph.Add(format with { Text = "-" });
                            break;
                        case Break pageBreak when pageBreak.Type?.Value == BreakValues.Page:
                            // Page break: what follows starts on a new page.
                            if (paragraph.Segments.Count == 0)
                            {
                                paragraph.PageBreak = true;
                            }
                            else
                            {
                                content.Paragraphs.Add(paragraph);
                                paragraph = paragraph.CopyProperties();
                                paragraph.PageBreak = true;
                            }
                            break;
                        case Break or CarriageReturn:
                            content.Paragraphs.Add(paragraph);
                            paragraph = paragraph.CopyProperties();
                            break;
                    }
                }
            }
            // A paragraph that contains only the page break passes it to the next paragraph.
            if (paragraph.Segments.Count == 0 && paragraph.PageBreak)
                pendingBreak = true;
            else
                content.Paragraphs.Add(paragraph);
        }
    }

    /// <summary>
    /// The cell the paragraph is in (of the outermost table, if they are nested), or null.
    /// Tables are numbered in the order they are met.
    /// </summary>
    private static CellPosition? CellAt(W.Paragraph p, Dictionary<Table, int> tableNumbers)
    {
        if (p.Ancestors<TableCell>().LastOrDefault() is not { } cell || cell.Parent is not TableRow row || row.Parent is not Table table)
            return null;
        if (!tableNumbers.TryGetValue(table, out int number))
            tableNumbers[table] = number = tableNumbers.Count;
        int rowIndex = table.Elements<TableRow>().TakeWhile(r => r != row).Count();
        int column = row.Elements<TableCell>().TakeWhile(c => c != cell).Count();
        float width = cell.TableCellProperties?.TableCellWidth is { Type.Value: var kind, Width.Value: string w }
            && kind == TableWidthUnitValues.Dxa && int.TryParse(w, out int twip) ? twip / 20f : 0;
        if (width == 0 && table.GetFirstChild<TableGrid>()?.Elements<GridColumn>().ElementAtOrDefault(column)?.Width?.Value is string g
            && int.TryParse(g, out int gridTwips))
            width = gridTwips / 20f;
        bool header = row.TableRowProperties?.GetFirstChild<TableHeader>() is not null;
        return new CellPosition(number, rowIndex, column, width, header);
    }

    private static bool Active(OnOffType? @value) => @value is not null && (@value.Val is null || @value.Val.Value);

    private static PageLayout ReadPage(MainDocumentPart main, Body body)
    {
        var defaults = new PageLayout();
        var section = body.Elements<SectionProperties>().LastOrDefault();
        var sheet = section?.GetFirstChild<PageSize>();
        var margins = section?.GetFirstChild<PageMargin>();
        bool horizontal = sheet?.Orient?.Value == PageOrientationValues.Landscape;
        float width = sheet?.Width?.Value / 20f ?? (horizontal ? defaults.Height : defaults.Width);
        float height = sheet?.Height?.Value / 20f ?? (horizontal ? defaults.Width : defaults.Height);

        string Text(OpenXmlCompositeElement? root) =>
            root is null ? "" : string.Join(" ", root.Elements<W.Paragraph>().Select(TextWithVariables).Where(t => t.Length > 0));

        var header = section?.Elements<HeaderReference>().FirstOrDefault(r => r.Type is null || r.Type.Value == HeaderFooterValues.Default);
        var footer = section?.Elements<FooterReference>().FirstOrDefault(r => r.Type is null || r.Type.Value == HeaderFooterValues.Default);

        return new PageLayout(
            horizontal ? height : width,
            horizontal ? width : height,
            horizontal,
            margins?.Left?.Value / 20f ?? defaults.LeftMargin,
            margins?.Right?.Value / 20f ?? defaults.RightMargin,
            margins?.Top?.Value / 20f ?? defaults.TopMargin,
            margins?.Bottom?.Value / 20f ?? defaults.BottomMargin,
            margins?.Header?.Value / 20f ?? defaults.HeaderDistance,
            margins?.Footer?.Value / 20f ?? defaults.FooterDistance,
            header?.Id?.Value is string idI && main.GetPartById(idI) is HeaderPart partI ? Text(partI.Header) : "",
            footer?.Id?.Value is string idP && main.GetPartById(idP) is FooterPart partP ? Text(partP.Footer) : "",
            section?.GetFirstChild<TitlePage>() is TitlePage firstPage && Active(firstPage));
    }

    /// <summary>The text of a header paragraph, with Word fields (PAGE, NUMPAGES, ...) as variables.</summary>
    private static string TextWithVariables(W.Paragraph paragraph)
    {
        var text = new StringBuilder();
        bool insideResult = false; // between "separate" and "end" of a field is the computed result: it is not text
        foreach (var element in paragraph.Descendants())
        {
            switch (element)
            {
                case SimpleField field:
                    text.Append(PageLayout.FieldPlaceholder(field.Instruction?.Value ?? ""));
                    break;
                case FieldCode code:
                    text.Append(PageLayout.FieldPlaceholder(code.Text));
                    break;
                case FieldChar character when character.FieldCharType?.Value == FieldCharValues.Separate:
                    insideResult = true;
                    break;
                case FieldChar character when character.FieldCharType?.Value == FieldCharValues.End:
                    insideResult = false;
                    break;
                case Text t when !insideResult && !t.Ancestors<SimpleField>().Any():
                    text.Append(t.Text);
                    break;
            }
        }
        return text.ToString().Trim();
    }

    private static SectionProperties CreateSection(MainDocumentPart main, PageLayout page)
    {
        var section = new SectionProperties();

        // Header and footer: variables become Word fields, which update by themselves.
        W.Paragraph Border(string model)
        {
            var p = new W.Paragraph(new ParagraphProperties(new Justification { Val = JustificationValues.Center }));
            foreach (var (text, field) in PageLayout.Pieces(model))
            {
                var run = new Run(new Text(field is null ? text : "1") { Space = SpaceProcessingModeValues.Preserve });
                p.Append(field is null ? run : new SimpleField(run) { Instruction = $" {field} " });
            }
            return p;
        }

        if (page.Header.Length > 0)
        {
            var part = main.AddNewPart<HeaderPart>();
            part.Header = new Header(Border(page.Header));
            section.Append(new HeaderReference { Type = HeaderFooterValues.Default, Id = main.GetIdOfPart(part) });
        }
        if (page.SkipFirstPage)
        {
            var part = main.AddNewPart<HeaderPart>();
            part.Header = new Header(new W.Paragraph());
            section.Append(new HeaderReference { Type = HeaderFooterValues.First, Id = main.GetIdOfPart(part) });
        }
        if (page.Footer.Length > 0)
        {
            var part = main.AddNewPart<FooterPart>();
            part.Footer = new Footer(Border(page.Footer));
            section.Append(new FooterReference { Type = HeaderFooterValues.Default, Id = main.GetIdOfPart(part) });
        }
        if (page.SkipFirstPage)
        {
            var part = main.AddNewPart<FooterPart>();
            part.Footer = new Footer(new W.Paragraph());
            section.Append(new FooterReference { Type = HeaderFooterValues.First, Id = main.GetIdOfPart(part) });
        }

        var (width, height) = page.Effective;
        var sheet = new PageSize { Width = (uint)RtfWriter.Twip(width), Height = (uint)RtfWriter.Twip(height) };
        if (page.Horizontal)
            sheet.Orient = PageOrientationValues.Landscape;
        section.Append(sheet);
        section.Append(new PageMargin
        {
            Left = (uint)RtfWriter.Twip(page.LeftMargin),
            Right = (uint)RtfWriter.Twip(page.RightMargin),
            Top = RtfWriter.Twip(page.TopMargin),
            Bottom = RtfWriter.Twip(page.BottomMargin),
            Header = (uint)RtfWriter.Twip(page.HeaderDistance),
            Footer = (uint)RtfWriter.Twip(page.FooterDistance),
            Gutter = 0,
        });
        if (page.SkipFirstPage)
            section.Append(new TitlePage());
        return section;
    }

    public static void Write(DocumentContent content, Stream stream, string character = "Calibri", float baseSize = DocumentContent.BaseSize)
    {
        using var documents = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document);
        var main = documents.AddMainDocumentPart();
        var body = new Body();
        var metadata = content.Metadata;
        if (metadata.Title.Length > 0)
            documents.PackageProperties.Title = metadata.Title;
        if (metadata.Author.Length > 0)
            documents.PackageProperties.Creator = metadata.Author;
        if (metadata.Subject.Length > 0)
            documents.PackageProperties.Subject = metadata.Subject;
        if (metadata.Keywords.Length > 0)
            documents.PackageProperties.Keywords = metadata.Keywords;
        documents.PackageProperties.Created = metadata.Created ?? DateTime.Now;
        documents.PackageProperties.Modified = DateTime.Now;
        main.Document = new Document(body);

        var stylesPart = main.AddNewPart<StyleDefinitionsPart>();
        stylesPart.Styles = CreateStyles(character, baseSize);

        // Lists: one instance for bullets, and one for every numbered list so that each restarts from 1.
        var numbering = new Numbering(CreateListDefinition(BulletListId, NumberFormatValues.Bullet, "•"),
            CreateListDefinition(NumberedListId, NumberFormatValues.Decimal, "%1."));
        numbering.Append(new NumberingInstance(new AbstractNumId { Val = BulletListId }) { NumberID = BulletListId });
        int numberedInstance = 0;
        bool previousNumbered = false;
        var wordNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var comments = CompleteComments(content);
        Table? table = null;
        TableRow? row = null;
        TableCell? wordCell = null;
        CellPosition? lastCell = null;

        foreach (var paragraph in content.Paragraphs)
        {
            if (paragraph.Numbered && !previousNumbered)
            {
                numberedInstance = NumberedListId + 1 + numbering.Elements<NumberingInstance>().Count();
                numbering.Append(new NumberingInstance(
                    new AbstractNumId { Val = NumberedListId },
                    new LevelOverride(new StartOverrideNumberingValue { Val = 1 }) { LevelIndex = 0 })
                { NumberID = numberedInstance });
            }
            previousNumbered = paragraph.Numbered;

            var properties = new ParagraphProperties();
            if (paragraph.HeadingLevel > 0)
                properties.Append(new ParagraphStyleId { Val = $"Heading{paragraph.HeadingLevel}" });
            if (paragraph.PageBreak)
                properties.Append(new PageBreakBefore());
            if (paragraph.Bulleted || paragraph.Numbered)
                properties.Append(new NumberingProperties(
                    new NumberingLevelReference { Val = 0 },
                    new NumberingId { Val = paragraph.Numbered ? numberedInstance : BulletListId }));
            if (paragraph.SpaceBefore != 0 || paragraph.SpaceAfter != 0 || paragraph.LineSpacing != 1f)
            {
                var spaces = new SpacingBetweenLines();
                if (paragraph.SpaceBefore != 0)
                    spaces.Before = Twip(paragraph.SpaceBefore);
                if (paragraph.SpaceAfter != 0)
                    spaces.After = Twip(paragraph.SpaceAfter);
                if (paragraph.LineSpacing != 1f)
                {
                    spaces.Line = ((int)Math.Round(paragraph.LineSpacing * 240)).ToString(CultureInfo.InvariantCulture);
                    spaces.LineRule = LineSpacingRuleValues.Auto;
                }
                properties.Append(spaces);
            }
            if (paragraph.LeftIndent != 0 || paragraph.RightIndent != 0 || paragraph.FirstLineIndent != 0)
            {
                var indents = new Indentation();
                if (paragraph.LeftIndent != 0)
                    indents.Left = Twip(paragraph.LeftIndent);
                if (paragraph.RightIndent != 0)
                    indents.Right = Twip(paragraph.RightIndent);
                if (paragraph.FirstLineIndent > 0)
                    indents.FirstLine = Twip(paragraph.FirstLineIndent);
                else if (paragraph.FirstLineIndent < 0)
                    indents.Hanging = Twip(-paragraph.FirstLineIndent);
                properties.Append(indents);
            }
            if (paragraph.Alignment != Alignment.Left)
                properties.Append(new Justification
                {
                    Val = paragraph.Alignment switch
                    {
                        Alignment.Center => JustificationValues.Center,
                        Alignment.Right => JustificationValues.Right,
                        _ => JustificationValues.Both,
                    },
                });

            var p = new W.Paragraph();
            if (properties.HasChildren)
                p.Append(properties);
            foreach (var segment in paragraph.Segments)
                AddRun(p, segment, content, wordNames, main, comments);

            // In a table the paragraph goes in its cell: table, row and cell are opened when they change.
            if (paragraph.Cell is not { } cell)
            {
                body.Append(p);
                table = null;
                continue;
            }
            if (table is null || cell.Table != lastCell!.Table)
            {
                table = CreateTable(content, cell.Table);
                body.Append(table);
                row = null;
            }
            if (row is null || !cell.SameLine(lastCell))
            {
                row = new TableRow();
                if (cell.Header)
                    row.Append(new TableRowProperties(new TableHeader()));
                table.Append(row);
                wordCell = null;
            }
            if (wordCell is null || !cell.SameCell(lastCell))
            {
                wordCell = new TableCell(new TableCellProperties(new TableCellWidth
                {
                    Type = TableWidthUnitValues.Dxa,
                    Width = (cell.Width > 0 ? Twip(cell.Width) : DefaultColumnWidth),
                }));
                row.Append(wordCell);
            }
            wordCell.Append(p);
            lastCell = cell;
        }

        // Word wants a paragraph after the last table of the document.
        if (body.LastChild is Table)
            body.Append(new W.Paragraph());

        if (content.Footnotes.Count > 0)
            main.AddNewPart<FootnotesPart>().Footnotes = CreateFootnotes(content.Footnotes);

        if (comments.Count > 0)
            main.AddNewPart<WordprocessingCommentsPart>().Comments = CreateComments(content.Comments, comments);

        if (content.Paragraphs.Any(p => p.Bulleted || p.Numbered))
            main.AddNewPart<NumberingDefinitionsPart>().Numbering = numbering;

        // Paper, margins, header and footer: the section properties go at the end of the body.
        body.Append(CreateSection(main, content.Page));
    }

    private static string Twip(float points) => ((int)Math.Round(points * 20)).ToString(CultureInfo.InvariantCulture);

    private static string Hex(int color) => (color & 0xFFFFFF).ToString("X6", CultureInfo.InvariantCulture);

    /// <summary>The segment as a run, with bookmarks, footnote references and images in place of their placeholders.</summary>
    private static void AddRun(W.Paragraph p, Segment segment, DocumentContent content, HashSet<string> wordNames, MainDocumentPart main,
        HashSet<int> comments)
    {
        var bookmarks = content.Bookmarks;
        int start = 0;
        for (int i = 0; i <= segment.Text.Length; i++)
        {
            char c = i < segment.Text.Length ? segment.Text[i] : '\0';
            int index = i < segment.Text.Length ? Bookmarks.Index(c) : -1;
            int footnote = i < segment.Text.Length ? FootnoteMarkers.Index(c) : -1;
            int image = i < segment.Text.Length ? Images.Index(c) : -1;
            int commentStart = i < segment.Text.Length ? CommentMarkers.StartIndex(c) : -1;
            int commentEnd = i < segment.Text.Length ? CommentMarkers.EndIndex(c) : -1;
            if (i < segment.Text.Length && index < 0 && footnote < 0 && image < 0 && commentStart < 0 && commentEnd < 0)
                continue;
            if (i > start)
                p.Append(CreateRun(segment with { Text = segment.Text[start..i] }));
            if (index >= 0 && index < bookmarks.Count)
            {
                var id = (wordNames.Count + 1).ToString(CultureInfo.InvariantCulture);
                p.Append(new BookmarkStart { Id = id, Name = Bookmarks.WordName(bookmarks[index], wordNames) });
                p.Append(new BookmarkEnd { Id = id });
            }
            if (footnote >= 0 && footnote < content.Footnotes.Count)
            {
                // Superscript reference; the footnote text is in the footnotes part, with the same number.
                p.Append(new Run(
                    new RunProperties(new VerticalTextAlignment { Val = VerticalPositionValues.Superscript }),
                    new FootnoteReference { Id = footnote + 1 }));
            }
            if (image >= 0 && image < content.Images.Count)
                p.Append(new Run(CreateDrawing(main, content.Images[image], image + 1)));
            if (comments.Contains(commentStart))
                p.Append(new CommentRangeStart { Id = commentStart.ToString(CultureInfo.InvariantCulture) });
            if (comments.Contains(commentEnd))
            {
                var id = commentEnd.ToString(CultureInfo.InvariantCulture);
                p.Append(new CommentRangeEnd { Id = id });
                p.Append(new Run(new CommentReference { Id = id }));
            }
            start = i + 1;
        }
    }

    /// <summary>An image inline with the text, with the description as alternative text (what the screen reader reads in Word).</summary>
    private static Drawing CreateDrawing(MainDocumentPart main, EmbeddedImage image, int number)
    {
        var part = main.AddImagePart(image.Format == "jpeg" ? ImagePartType.Jpeg : ImagePartType.Png);
        using (var data = new MemoryStream(image.Data))
            part.FeedData(data);
        string relationship = main.GetIdOfPart(part);
        long cx = (long)Math.Round(image.Width * 12700), cy = (long)Math.Round(image.Height * 12700); // EMU
        var name = $"Immagine {number}";
        return new Drawing(new DW.Inline(
            new DW.Extent { Cx = cx, Cy = cy },
            new DW.EffectExtent { LeftEdge = 0, TopEdge = 0, RightEdge = 0, BottomEdge = 0 },
            new DW.DocProperties { Id = (uint)number, Name = name, Description = image.Description },
            new DW.NonVisualGraphicFrameDrawingProperties(new A.GraphicFrameLocks { NoChangeAspect = true }),
            new A.Graphic(new A.GraphicData(
                new PIC.Picture(
                    new PIC.NonVisualPictureProperties(
                        new PIC.NonVisualDrawingProperties { Id = 0U, Name = name, Description = image.Description },
                        new PIC.NonVisualPictureDrawingProperties()),
                    new PIC.BlipFill(new A.Blip { Embed = relationship }, new A.Stretch(new A.FillRectangle())),
                    new PIC.ShapeProperties(
                        new A.Transform2D(new A.Offset { X = 0, Y = 0 }, new A.Extents { Cx = cx, Cy = cy }),
                        new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle })))
            { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" }))
        { DistanceFromTop = 0U, DistanceFromBottom = 0U, DistanceFromLeft = 0U, DistanceFromRight = 0U });
    }

    /// <summary>
    /// A Word drawing as an image (inline or floating: in Eleu it becomes inline), with the description;
    /// null if it does not contain an image that can be read.
    /// </summary>
    private static EmbeddedImage? ReadDrawing(Drawing drawing, MainDocumentPart main)
    {
        if (drawing.Descendants<A.Blip>().FirstOrDefault()?.Embed?.Value is not string relationship
            || main.GetPartById(relationship) is not ImagePart part)
            return null;
        using var stream = part.GetStream();
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        var extension = drawing.Descendants<DW.Extent>().FirstOrDefault();
        var properties = drawing.Descendants<DW.DocProperties>().FirstOrDefault();
        var description = properties?.Description?.Value ?? properties?.Title?.Value ?? "";
        try
        {
            var image = Images.FromData(copy.ToArray(), description.Trim());
            return extension?.Cx?.Value is long cx && extension.Cy?.Value is long cy && cx > 0 && cy > 0
                ? image with { Width = cx / 12700f, Height = cy / 12700f }
                : image;
        }
        catch (Exception e) when (e is ArgumentException or System.Runtime.InteropServices.ExternalException or OutOfMemoryException)
        {
            return null;
        }
    }

    /// <summary>Width of a column without a chosen width: 4 cm, in twips (as in RTF).</summary>
    private const string DefaultColumnWidth = "2268";

    /// <summary>An empty table with thin borders and the column grid; rows and cells are added later.</summary>
    private static Table CreateTable(DocumentContent content, int number)
    {
        BorderType Border<T>() where T : BorderType, new() => new T { Val = BorderValues.Single, Size = 4, Space = 0, Color = "auto" };
        var columns = content.Paragraphs.Select(p => p.Cell).OfType<CellPosition>().Where(c => c.Table == number)
            .GroupBy(c => c.Column).OrderBy(g => g.Key)
            .Select(g => g.Select(c => c.Width).FirstOrDefault(l => l > 0)).ToList();
        var grid = new TableGrid();
        foreach (var width in columns)
            grid.Append(new GridColumn { Width = width > 0 ? Twip(width) : DefaultColumnWidth });
        return new Table(
            new TableProperties(
                new TableWidth { Type = TableWidthUnitValues.Auto, Width = "0" },
                new TableBorders(Border<TopBorder>(), Border<LeftBorder>(), Border<BottomBorder>(), Border<RightBorder>(),
                    Border<InsideHorizontalBorder>(), Border<InsideVerticalBorder>())),
            grid);
    }

    /// <summary>
    /// The comments with both start and end in the text: only these are written (Word does not accept a range
    /// opened and never closed).
    /// </summary>
    private static HashSet<int> CompleteComments(DocumentContent content)
    {
        var starts = new HashSet<int>();
        var ends = new HashSet<int>();
        foreach (var c in content.Paragraphs.SelectMany(p => p.Segments).SelectMany(s => s.Text))
        {
            if (CommentMarkers.StartIndex(c) is var i and >= 0 && i < content.Comments.Count)
                starts.Add(i);
            else if (CommentMarkers.EndIndex(c) is var f and >= 0 && f < content.Comments.Count && starts.Contains(f))
                ends.Add(f);
        }
        return ends;
    }

    /// <summary>Word's comments part: author, initials, date and text, one paragraph per line.</summary>
    private static Comments CreateComments(IReadOnlyList<Comment> comments, HashSet<int> used)
    {
        var part = new Comments();
        foreach (int i in used.Order())
        {
            var comment = comments[i];
            var word = new W.Comment { Id = i.ToString(CultureInfo.InvariantCulture), Author = comment.Author.Length > 0 ? comment.Author : "Eleu" };
            if (comment.Initials.Length > 0)
                word.Initials = comment.Initials;
            if (comment.Date is DateTime date)
                word.Date = date;
            var rows = comment.Text.ReplaceLineEndings(LineBreak).Split(LineBreak);
            for (int r = 0; r < rows.Length; r++)
            {
                var paragraph = new W.Paragraph();
                if (r == 0)
                    paragraph.Append(new Run(new AnnotationReferenceMark()));
                paragraph.Append(new Run(new Text(rows[r]) { Space = SpaceProcessingModeValues.Preserve }));
                word.Append(paragraph);
            }
            part.Append(word);
        }
        return part;
    }

    /// <summary>Word's footnotes part: the two mandatory separators and then the footnotes, numbered from 1.</summary>
    private static Footnotes CreateFootnotes(IReadOnlyList<string> texts)
    {
        var footnotes = new Footnotes(
            new Footnote(new W.Paragraph(new Run(new SeparatorMark()))) { Type = FootnoteEndnoteValues.Separator, Id = -1 },
            new Footnote(new W.Paragraph(new Run(new ContinuationSeparatorMark()))) { Type = FootnoteEndnoteValues.ContinuationSeparator, Id = 0 });
        for (int i = 0; i < texts.Count; i++)
        {
            var rows = texts[i].ReplaceLineEndings(LineBreak).Split(LineBreak);
            var footnote = new Footnote { Id = i + 1 };
            for (int r = 0; r < rows.Length; r++)
            {
                var paragraph = new W.Paragraph();
                if (r == 0)
                    paragraph.Append(new Run(
                        new RunProperties(new VerticalTextAlignment { Val = VerticalPositionValues.Superscript }),
                        new FootnoteReferenceMark()));
                paragraph.Append(new Run(new Text((r == 0 ? " " : "") + rows[r]) { Space = SpaceProcessingModeValues.Preserve }));
                footnote.Append(paragraph);
            }
            footnotes.Append(footnote);
        }
        return footnotes;
    }

    private static string ParagraphText(W.Paragraph p) => string.Concat(p.Descendants<Text>().Select(t => t.Text));

    private static Run CreateRun(Segment segment)
    {
        var run = new Run();
        var properties = new RunProperties();
        // The element order is the one required by the Word schema.
        if (segment.Font is string character)
            properties.Append(new RunFonts { Ascii = character, HighAnsi = character, ComplexScript = character });
        if (segment.Bold)
            properties.Append(new Bold());
        if (segment.Italic)
            properties.Append(new Italic());
        if (segment.Strikethrough)
            properties.Append(new Strike());
        if (segment.Color is int color)
            properties.Append(new DocumentFormat.OpenXml.Wordprocessing.Color { Val = Hex(color) });
        if (segment.Spacing != 0)
            properties.Append(new Spacing { Val = (int)Math.Round(segment.Spacing * 20) });
        if (segment.Size is float size)
            properties.Append(new FontSize { Val = ((int)Math.Round(size * 2)).ToString(CultureInfo.InvariantCulture) });
        if (segment.Underline)
            properties.Append(new Underline { Val = UnderlineValues.Single });
        if (segment.Background is int background)
            properties.Append(new Shading { Val = ShadingPatternValues.Clear, Color = "auto", Fill = Hex(background) });
        if (segment.Position != TextPosition.Normal)
            properties.Append(new VerticalTextAlignment
            {
                Val = segment.Position == TextPosition.Superscript ? VerticalPositionValues.Superscript : VerticalPositionValues.Subscript,
            });
        if (properties.HasChildren)
            run.Append(properties);

        var pieces = segment.Text.Split('\t');
        for (int i = 0; i < pieces.Length; i++)
        {
            if (i > 0)
                run.Append(new TabChar());
            if (pieces[i].Length > 0)
                run.Append(new Text(pieces[i]) { Space = SpaceProcessingModeValues.Preserve });
        }
        return run;
    }

    private static Styles CreateStyles(string character, float baseSize)
    {
        string HalfPoints(float points) => ((int)Math.Round(points * 2)).ToString(CultureInfo.InvariantCulture);

        var styles = new Styles(
            new DocDefaults(
                new RunPropertiesDefault(new RunPropertiesBaseStyle(
                    new RunFonts { Ascii = character, HighAnsi = character, ComplexScript = character },
                    new FontSize { Val = HalfPoints(baseSize) },
                    new Languages { Val = "it-IT" })),
                new ParagraphPropertiesDefault(new ParagraphPropertiesBaseStyle(
                    new SpacingBetweenLines { After = "120" }))),
            new Style(new StyleName { Val = "Normal" }, new PrimaryStyle())
            {
                Type = StyleValues.Paragraph,
                StyleId = "Normal",
                Default = true,
            });

        for (int level = 1; level <= 9; level++)
        {
            styles.Append(new Style(
                new StyleName { Val = $"heading {level}" },
                new BasedOn { Val = "Normal" },
                new NextParagraphStyle { Val = "Normal" },
                new PrimaryStyle(),
                new StyleParagraphProperties(
                    new KeepNext(),
                    new SpacingBetweenLines { Before = "240", After = "60" },
                    new OutlineLevel { Val = level - 1 }),
                new StyleRunProperties(
                    new Bold(),
                    new FontSize { Val = HalfPoints(DocumentContent.HeadingSize(level)) }))
            {
                Type = StyleValues.Paragraph,
                StyleId = $"Heading{level}",
            });
        }
        return styles;
    }

    private static AbstractNum CreateListDefinition(int id, NumberFormatValues format, string text) =>
        new(new Level(
                new StartNumberingValue { Val = 1 },
                new NumberingFormat { Val = format },
                new LevelText { Val = text },
                new LevelJustification { Val = LevelJustificationValues.Left },
                new PreviousParagraphProperties(new Indentation { Left = "720", Hanging = "360" }))
            { LevelIndex = 0 })
        { AbstractNumberId = id };

    // Word highlighter colors (name → 0xRRGGBB).
    private static readonly Dictionary<string, int> HighlighterColors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["yellow"] = 0xFFFF00, ["green"] = 0x00FF00, ["cyan"] = 0x00FFFF, ["magenta"] = 0xFF00FF,
        ["blue"] = 0x0000FF, ["red"] = 0xFF0000, ["darkBlue"] = 0x000080, ["darkCyan"] = 0x008080,
        ["darkGreen"] = 0x008000, ["darkMagenta"] = 0x800080, ["darkRed"] = 0x800000, ["darkYellow"] = 0x808000,
        ["darkGray"] = 0x808080, ["lightGray"] = 0xC0C0C0, ["black"] = 0x000000, ["white"] = 0xFFFFFF,
    };

    private static int? ReadColor(string? hex) =>
        hex is not null && !hex.Equals("auto", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var color)
            ? color
            : null;

    private static float Points(string? twip) =>
        float.TryParse(twip, NumberStyles.Float, CultureInfo.InvariantCulture, out var @value) ? @value / 20f : 0f;

    private static Segment ReadFormat(RunProperties? properties)
    {
        static bool Active(OnOffType? @value) => @value is not null && (@value.Val is null || @value.Val.Value);

        float? size = null;
        if (float.TryParse(properties?.FontSize?.Val?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var halfPoints))
            size = halfPoints / 2f;

        var underline = properties?.Underline?.Val;
        var vertical = properties?.VerticalTextAlignment?.Val?.InnerText;
        var highlighted = properties?.Highlight?.Val?.InnerText;
        int? background = ReadColor(properties?.Shading?.Fill?.Value);
        if (background is null && highlighted is not null && HighlighterColors.TryGetValue(highlighted, out var highlightColor))
            background = highlightColor;

        return new Segment(
            "",
            Active(properties?.Bold),
            Active(properties?.Italic),
            underline is not null && underline.Value != UnderlineValues.None,
            size,
            properties?.RunFonts?.Ascii?.Value ?? properties?.RunFonts?.HighAnsi?.Value,
            Active(properties?.Strike),
            vertical == "superscript" ? TextPosition.Superscript : vertical == "subscript" ? TextPosition.Subscript : TextPosition.Normal,
            ReadColor(properties?.Color?.Val?.Value),
            background,
            (properties?.Spacing?.Val?.Value ?? 0) / 20f);
    }

    private static void ReadSpacingAndIndents(ParagraphProperties? properties, Paragraph paragraph)
    {
        var spaces = properties?.SpacingBetweenLines;
        paragraph.SpaceBefore = Points(spaces?.Before?.Value);
        paragraph.SpaceAfter = Points(spaces?.After?.Value);
        if (spaces?.Line?.Value is string row && (spaces.LineRule is null || spaces.LineRule.InnerText == "auto")
            && float.TryParse(row, NumberStyles.Float, CultureInfo.InvariantCulture, out var twoHundredFortieths) && twoHundredFortieths > 0)
            paragraph.LineSpacing = twoHundredFortieths / 240f;

        var indents = properties?.Indentation;
        paragraph.LeftIndent = Points(indents?.Left?.Value ?? indents?.Start?.Value);
        paragraph.RightIndent = Points(indents?.Right?.Value ?? indents?.End?.Value);
        paragraph.FirstLineIndent = indents?.Hanging?.Value is string hanging ? -Points(hanging) : Points(indents?.FirstLine?.Value);
    }

    private static Alignment ReadAlignment(Justification? justification)
    {
        var @value = justification?.Val?.Value;
        if (@value == JustificationValues.Center)
            return Alignment.Center;
        if (@value == JustificationValues.Right || @value == JustificationValues.End)
            return Alignment.Right;
        if (@value == JustificationValues.Both || @value == JustificationValues.Distribute)
            return Alignment.Justified;
        return Alignment.Left;
    }

    private static int HeadingLevel(ParagraphProperties? properties, Styles? styles)
    {
        if (properties?.OutlineLevel?.Val?.Value is int directLevel && directLevel < 9)
            return directLevel + 1;

        var styleId = properties?.ParagraphStyleId?.Val?.Value;
        for (int steps = 0; styleId is not null && steps < 10; steps++)
        {
            var style = styles?.Elements<Style>().FirstOrDefault(s => s.StyleId?.Value == styleId);
            var name = style?.StyleName?.Val?.Value ?? styleId;

            if (name.Equals("Title", StringComparison.OrdinalIgnoreCase))
                return 1;
            var heading = HeadingName().Match(name);
            if (heading.Success)
                return int.Parse(heading.Groups[1].Value);
            if (style?.StyleParagraphProperties?.OutlineLevel?.Val?.Value is int styleLevel && styleLevel < 9)
                return styleLevel + 1;

            styleId = style?.BasedOn?.Val?.Value;
        }
        return 0;
    }

    private enum ListKind { None, Bulleted, Numbered }

    private static ListKind ReadList(NumberingProperties? properties, Numbering? numbering)
    {
        int numberId = properties?.NumberingId?.Val?.Value ?? 0;
        if (numberId == 0)
            return ListKind.None;
        int level = properties?.NumberingLevelReference?.Val?.Value ?? 0;

        var instance = numbering?.Elements<NumberingInstance>().FirstOrDefault(n => n.NumberID?.Value == numberId);
        var abstractId = instance?.AbstractNumId?.Val?.Value;
        var abstractNum = numbering?.Elements<AbstractNum>().FirstOrDefault(a => a.AbstractNumberId?.Value == abstractId);
        var definition = abstractNum?.Elements<Level>().FirstOrDefault(l => l.LevelIndex?.Value == level);
        var format = definition?.NumberingFormat?.Val?.Value;

        return format is null || format == NumberFormatValues.Bullet || format == NumberFormatValues.None
            ? ListKind.Bulleted
            : ListKind.Numbered;
    }

    [GeneratedRegex(@"^(?:heading|titolo)\s*(\d)$", RegexOptions.IgnoreCase)]
    private static partial Regex HeadingName();
}
