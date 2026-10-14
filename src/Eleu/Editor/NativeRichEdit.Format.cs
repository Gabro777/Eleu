using System.Runtime.InteropServices;
using Eleu.Documents;
using System.Text.Json.Serialization;

namespace Eleu.Editor;

/// <summary>A color: Rgb null = automatic (text) or none (background).</summary>
public readonly record struct ColorValue(int? Rgb);

/// <summary>
/// Character formatting of the selection. Each null property means "mixed" when reading
/// and "do not change" when writing.
/// </summary>
public sealed record CharacterFormat(
    string? Font = null,
    float? Size = null,
    bool? Bold = null,
    bool? Italic = null,
    bool? Underline = null,
    bool? Strikethrough = null,
    TextPosition? Position = null,
    ColorValue? Color = null,
    ColorValue? Background = null,
    float? Spacing = null);

public enum ListKind { None, Bulleted, Numbered }

/// <summary>Paragraph formatting, in points. Null = mixed when reading, "do not change" when writing.</summary>
public sealed record ParagraphFormat(
    [property: JsonPropertyName("Allineamento")] Alignment? Alignment = null,
    [property: JsonPropertyName("RientroSinistro")] float? LeftIndent = null,
    [property: JsonPropertyName("RientroDestro")] float? RightIndent = null,
    [property: JsonPropertyName("RientroPrimaRiga")] float? FirstLineIndent = null,
    [property: JsonPropertyName("SpazioPrima")] float? SpaceBefore = null,
    [property: JsonPropertyName("SpazioDopo")] float? SpaceAfter = null,
    [property: JsonPropertyName("Interlinea")] float? LineSpacing = null,
    [property: JsonPropertyName("Elenco")] ListKind? ListType = null);

public enum Uppercase { Lower = 0, Upper = 1, Initials = 2 }

public static partial class NativeRichEdit
{
    private const uint CfmStrikeout = 0x00000008;
    private const uint CfmFace = 0x20000000;
    private const uint CfmColor = 0x40000000;
    private const uint CfmBackColor = 0x04000000;
    private const uint CfmSpacing = 0x00200000;
    private const uint CfmSuperSub = 0x00030000;
    private const uint CfeSubscript = 0x00010000;
    private const uint CfeSuperscript = 0x00020000;
    private const uint CfeAutoColor = 0x40000000;
    private const uint CfeAutoBackColor = 0x04000000;

    private const uint PfmStartIndent = 0x00000001;
    private const uint PfmRightIndent = 0x00000002;
    private const uint PfmOffset = 0x00000004;
    private const uint PfmNumbering = 0x00000020;
    private const uint PfmSpaceBefore = 0x00000040;
    private const uint PfmSpaceAfter = 0x00000080;
    private const uint PfmLineSpacing = 0x00000100;
    private const uint PfmNumberingStyle = 0x00002000;
    private const uint PfmNumberingTab = 0x00004000;
    private const uint PfmNumberingStart = 0x00008000;

    private const short PfnBullet = 1;
    private const short PfnArabic = 2;
    private const short PfnsPeriod = 0x200;
    private const int ListIndentTwips = 360;

    private const int EmGetOleInterface = WmUser + 60;
    private static readonly Guid IidTextDocument = new("8CC497C0-A1DF-11CE-8098-00AA0047BE5D");

    public static CharacterFormat ReadCharacterFormat(RichTextBox field)
    {
        var f = ReadCharacter(field);
        bool Has(uint mask) => (f.dwMask & mask) == mask;
        bool? Effect(uint bit) => Has(bit) ? (f.dwEffects & bit) != 0 : null;

        TextPosition? position = null;
        if (Has(CfmSuperSub))
            position = (f.dwEffects & CfeSuperscript) != 0 ? TextPosition.Superscript
                : (f.dwEffects & CfeSubscript) != 0 ? TextPosition.Subscript
                : TextPosition.Normal;

        return new CharacterFormat(
            Has(CfmFace) ? System.Text.Encoding.Unicode.GetString(f.szFaceName).TrimEnd('\0') : null,
            Has(CfmSize) ? f.yHeight / 20f : null,
            Effect(CfmBold),
            Effect(CfmItalic),
            Effect(CfmUnderline),
            Effect(CfmStrikeout),
            position,
            Has(CfmColor) ? new ColorValue((f.dwEffects & CfeAutoColor) != 0 ? null : FromColorRef(f.crTextColor)) : null,
            Has(CfmBackColor) ? new ColorValue((f.dwEffects & CfeAutoBackColor) != 0 ? null : FromColorRef(f.crBackColor)) : null,
            Has(CfmSpacing) ? f.sSpacing / 20f : null);
    }

    /// <summary>Applies only the non-null properties to the selection.</summary>
    public static void SetCharacterFormat(RichTextBox field, CharacterFormat edits)
    {
        var f = NewCharacter();
        void Effect(bool? @value, uint bit)
        {
            if (@value is not bool active)
                return;
            f.dwMask |= bit;
            if (active)
                f.dwEffects |= bit;
        }

        if (edits.Font is string character)
        {
            f.dwMask |= CfmFace;
            var name = System.Text.Encoding.Unicode.GetBytes(character.Length > 31 ? character[..31] : character);
            name.CopyTo(f.szFaceName, 0);
        }
        if (edits.Size is float size)
        {
            f.dwMask |= CfmSize;
            f.yHeight = (int)Math.Round(size * 20);
        }
        Effect(edits.Bold, CfmBold);
        Effect(edits.Italic, CfmItalic);
        Effect(edits.Underline, CfmUnderline);
        Effect(edits.Strikethrough, CfmStrikeout);
        if (edits.Position is TextPosition position)
        {
            f.dwMask |= CfmSuperSub;
            f.dwEffects |= position switch
            {
                TextPosition.Superscript => CfeSuperscript,
                TextPosition.Subscript => CfeSubscript,
                _ => 0,
            };
        }
        if (edits.Color is ColorValue color)
        {
            f.dwMask |= CfmColor;
            if (color.Rgb is int rgb)
                f.crTextColor = ToColorRef(rgb);
            else
                f.dwEffects |= CfeAutoColor;
        }
        if (edits.Background is ColorValue background)
        {
            f.dwMask |= CfmBackColor;
            if (background.Rgb is int rgb)
                f.crBackColor = ToColorRef(rgb);
            else
                f.dwEffects |= CfeAutoBackColor;
        }
        if (edits.Spacing is float spacing)
        {
            f.dwMask |= CfmSpacing;
            f.sSpacing = (short)Math.Round(spacing * 20);
        }
        if (f.dwMask != 0)
            SendMessage(field.Handle, EmSetCharFormat, ScfSelection, ref f);
    }

    public static ParagraphFormat ReadParagraphFormat(RichTextBox field)
    {
        var p = ReadParagraph(field);
        bool Has(uint mask) => (p.dwMask & mask) == mask;

        ListKind? list = Has(PfmNumbering)
            ? p.wNumbering switch { 0 => ListKind.None, PfnBullet => ListKind.Bulleted, _ => ListKind.Numbered }
            : null;
        bool inList = list is ListKind.Bulleted or ListKind.Numbered;

        // RichEdit: dxStartIndent is the first-line indent, dxOffset that of the following lines relative to the first.
        float? left = Has(PfmStartIndent | PfmOffset) ? (p.dxStartIndent + p.dxOffset) / 20f : null;
        float? firstLine = Has(PfmOffset) ? -p.dxOffset / 20f : null;
        if (inList)
        {
            // In lists the space for the bullet or number is not an indent chosen by the user.
            left = left is float s ? Math.Max(0, s - ListIndentTwips / 20f) : null;
            firstLine = 0;
        }

        float? lineSpacing = Has(PfmLineSpacing)
            ? p.bLineSpacingRule switch
            {
                1 => 1.5f,
                2 => 2f,
                5 => p.dyLineSpacing / 20f,
                _ => 1f,
            }
            : null;

        return new ParagraphFormat(
            Has(PfmAlignment) ? ReadAlignment(field) : null,
            left,
            Has(PfmRightIndent) ? p.dxRightIndent / 20f : null,
            firstLine,
            Has(PfmSpaceBefore) ? p.dySpaceBefore / 20f : null,
            Has(PfmSpaceAfter) ? p.dySpaceAfter / 20f : null,
            lineSpacing,
            list);
    }

    /// <summary>Applies only the non-null properties to the selected paragraphs.</summary>
    public static void SetParagraphFormat(RichTextBox field, ParagraphFormat edits)
    {
        if (edits.Alignment is Alignment alignment)
            SetAlignment(field, alignment);

        var current = ReadParagraphFormat(field);
        var list = edits.ListType ?? current.ListType ?? ListKind.None;
        var p = NewParagraph();

        if (edits.LeftIndent is not null || edits.FirstLineIndent is not null || edits.ListType is not null)
        {
            float left = edits.LeftIndent ?? current.LeftIndent ?? 0;
            float firstLine = list == ListKind.None ? edits.FirstLineIndent ?? current.FirstLineIndent ?? 0 : 0;
            int leftTwips = (int)Math.Round(left * 20);
            int firstLineTwips = (int)Math.Round(firstLine * 20);
            p.dwMask |= PfmStartIndent | PfmOffset | PfmNumbering | PfmNumberingStyle | PfmNumberingStart | PfmNumberingTab;
            if (list == ListKind.None)
            {
                p.dxStartIndent = leftTwips + firstLineTwips;
                p.dxOffset = -firstLineTwips;
                p.wNumbering = 0;
            }
            else
            {
                // As Eleu's RTF writes it: the bullet or number hangs 18 points out of the text.
                p.dxStartIndent = leftTwips;
                p.dxOffset = ListIndentTwips;
                p.wNumbering = list == ListKind.Bulleted ? PfnBullet : PfnArabic;
                p.wNumberingStyle = PfnsPeriod;
                p.wNumberingStart = 1;
                p.wNumberingTab = ListIndentTwips;
            }
        }
        if (edits.RightIndent is float right)
        {
            p.dwMask |= PfmRightIndent;
            p.dxRightIndent = (int)Math.Round(right * 20);
        }
        if (edits.SpaceBefore is float before)
        {
            p.dwMask |= PfmSpaceBefore;
            p.dySpaceBefore = (int)Math.Round(before * 20);
        }
        if (edits.SpaceAfter is float after)
        {
            p.dwMask |= PfmSpaceAfter;
            p.dySpaceAfter = (int)Math.Round(after * 20);
        }
        if (edits.LineSpacing is float lineSpacing)
        {
            // Rule 5: dyLineSpacing / 20 = number of lines (20 = single, 30 = one and a half, 40 = double).
            p.dwMask |= PfmLineSpacing;
            p.bLineSpacingRule = 5;
            p.dyLineSpacing = (int)Math.Round(lineSpacing * 20);
        }
        if (p.dwMask != 0)
            SendMessage(field.Handle, EmSetParaFormat, 0, ref p);
    }

    /// <summary>
    /// Changes case between two positions with RichEdit's Text Object Model:
    /// the formatting stays and the operation is undone with a single Ctrl+Z.
    /// </summary>
    public static void ToggleCase(RichTextBox field, int start, int end, Uppercase kind)
    {
        IntPtr ole = IntPtr.Zero, documents = IntPtr.Zero;
        try
        {
            SendMessage(field.Handle, EmGetOleInterface, 0, out ole);
            if (ole == IntPtr.Zero)
                return;
            var iid = IidTextDocument;
            if (Marshal.QueryInterface(ole, in iid, out documents) != 0)
                return;
            dynamic tom = Marshal.GetObjectForIUnknown(documents);
            tom.Range(start, end).ChangeCase((int)kind);
        }
        finally
        {
            if (documents != IntPtr.Zero)
                Marshal.Release(documents);
            if (ole != IntPtr.Zero)
                Marshal.Release(ole);
        }
    }

    /// <summary>
    /// RichEdit's Text Object Model document (ITextDocument), to use with dynamic. Its ranges
    /// (Range) follow the text while typing: used for bookmarks. Null if not available.
    /// </summary>
    public static object? TomDocument(RichTextBox field)
    {
        IntPtr ole = IntPtr.Zero, documents = IntPtr.Zero;
        try
        {
            SendMessage(field.Handle, EmGetOleInterface, 0, out ole);
            if (ole == IntPtr.Zero)
                return null;
            var iid = IidTextDocument;
            return Marshal.QueryInterface(ole, in iid, out documents) == 0 ? Marshal.GetObjectForIUnknown(documents) : null;
        }
        finally
        {
            if (documents != IntPtr.Zero)
                Marshal.Release(documents);
            if (ole != IntPtr.Zero)
                Marshal.Release(ole);
        }
    }

    // Windows COLORREF is 0x00BBGGRR, Eleu uses 0xRRGGBB.
    private static int ToColorRef(int rgb) => ((rgb & 0xFF) << 16) | (rgb & 0xFF00) | ((rgb >> 16) & 0xFF);

    private static int FromColorRef(int colorRef) => ToColorRef(colorRef);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, out IntPtr lParam);
}
