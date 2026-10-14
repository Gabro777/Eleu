using System.Runtime.InteropServices;
using Eleu.Documents;

namespace Eleu.Editor;

/// <summary>
/// RichEdit control functions that WinForms does not expose: heading styles, justified text
/// and formatting that also works on selections with mixed formats.
/// </summary>
public static partial class NativeRichEdit
{
    private const int WmUser = 0x0400;
    private const int EmGetCharFormat = WmUser + 58;
    private const int EmSetCharFormat = WmUser + 68;
    private const int EmGetParaFormat = WmUser + 61;
    private const int EmSetParaFormat = WmUser + 71;
    private const int EmSetFontSize = WmUser + 223;
    private const int EmSetTypographyOptions = WmUser + 202;

    private const int ScfSelection = 0x0001;
    private const int ToAdvancedTypography = 0x0001;

    private const uint CfmBold = 0x00000001;
    private const uint CfmItalic = 0x00000002;
    private const uint CfmUnderline = 0x00000004;
    private const uint CfmSize = 0x80000000;

    private const uint PfmAlignment = 0x00000008;
    private const uint PfmStyle = 0x00000400;

    private const short PfaLeft = 1;
    private const short PfaRight = 2;
    private const short PfaCenter = 3;
    private const short PfaJustify = 4;

    // RichEdit predefined styles: Normal = -1, Heading 1 = -2, Heading 2 = -3, ...
    private const short NormalStyle = -1;

    public enum Effect : uint
    {
        Bold = CfmBold,
        Italic = CfmItalic,
        Underline = CfmUnderline,
    }

    /// <summary>Needed for justified text to be displayed as such.</summary>
    public static void EnableAdvancedTypography(RichTextBox field) =>
        SendMessage(field.Handle, EmSetTypographyOptions, ToAdvancedTypography, ToAdvancedTypography);

    /// <summary>
    /// Red underline of misspelled words while typing, done by the text box with the Windows spell
    /// checker (since Windows 8). It is only for sighted users: screen reader users have F7.
    /// </summary>
    public static void SpellCheckAsYouType(RichTextBox field, bool active)
    {
        const int EmSetLangOptions = WmUser + 120, EmGetLangOptions = WmUser + 121, ImfSpellChecking = 0x0800;
        const int EmSetEditStyle = WmUser + 204, SesUseCtf = 0x00010000, SesCtfAllowProofing = 0x00800000;
        // The check goes through the Windows text services framework (TSF): without it, the option alone underlines nothing.
        if (active)
            SendMessage(field.Handle, EmSetEditStyle, SesUseCtf | SesCtfAllowProofing, SesUseCtf | SesCtfAllowProofing);
        int options = (int)SendMessage(field.Handle, EmGetLangOptions, 0, 0);
        options = active ? options | ImfSpellChecking : options & ~ImfSpellChecking;
        SendMessage(field.Handle, EmSetLangOptions, 0, options);
    }

    /// <summary>State of the effect in the selection: null if mixed.</summary>
    public static bool? EffectState(RichTextBox field, Effect effect)
    {
        var format = ReadCharacter(field);
        if ((format.dwMask & (uint)effect) == 0)
            return null;
        return (format.dwEffects & (uint)effect) != 0;
    }

    public static void SetEffect(RichTextBox field, Effect effect, bool active)
    {
        var format = NewCharacter();
        format.dwMask = (uint)effect;
        format.dwEffects = active ? (uint)effect : 0;
        SendMessage(field.Handle, EmSetCharFormat, ScfSelection, ref format);
    }

    /// <summary>Size in points of the selection, null if mixed.</summary>
    public static float? Size(RichTextBox field)
    {
        var format = ReadCharacter(field);
        return (format.dwMask & CfmSize) == 0 ? null : format.yHeight / 20f;
    }

    public static void SetSize(RichTextBox field, float points)
    {
        var format = NewCharacter();
        format.dwMask = CfmSize;
        format.yHeight = (int)Math.Round(points * 20);
        SendMessage(field.Handle, EmSetCharFormat, ScfSelection, ref format);
    }

    /// <summary>Enlarges or shrinks the size of the whole selection, even if mixed, keeping the proportions.</summary>
    public static void ChangeSize(RichTextBox field, int steps) =>
        SendMessage(field.Handle, EmSetFontSize, steps, 0);

    public static int HeadingLevel(RichTextBox field)
    {
        var format = ReadParagraph(field);
        return (format.dwMask & PfmStyle) != 0 && format.sStyle <= -2 && format.sStyle >= -10
            ? -format.sStyle - 1
            : 0;
    }

    public static void SetHeadingLevel(RichTextBox field, int level)
    {
        var format = NewParagraph();
        format.dwMask = PfmStyle;
        format.sStyle = level == 0 ? NormalStyle : (short)(-level - 1);
        SendMessage(field.Handle, EmSetParaFormat, 0, ref format);
    }

    public static Alignment ReadAlignment(RichTextBox field) =>
        ReadParagraph(field).wAlignment switch
        {
            PfaCenter => Alignment.Center,
            PfaRight => Alignment.Right,
            PfaJustify => Alignment.Justified,
            _ => Alignment.Left,
        };

    public static void SetAlignment(RichTextBox field, Alignment alignment)
    {
        var format = NewParagraph();
        format.dwMask = PfmAlignment;
        format.wAlignment = alignment switch
        {
            Alignment.Center => PfaCenter,
            Alignment.Right => PfaRight,
            Alignment.Justified => PfaJustify,
            _ => PfaLeft,
        };
        SendMessage(field.Handle, EmSetParaFormat, 0, ref format);
    }

    private static CharFormat2 ReadCharacter(RichTextBox field)
    {
        var format = NewCharacter();
        SendMessage(field.Handle, EmGetCharFormat, ScfSelection, ref format);
        return format;
    }

    private static ParaFormat2 ReadParagraph(RichTextBox field)
    {
        var format = NewParagraph();
        SendMessage(field.Handle, EmGetParaFormat, 0, ref format);
        return format;
    }

    private static CharFormat2 NewCharacter() => new() { cbSize = Marshal.SizeOf<CharFormat2>(), szFaceName = new byte[64] };

    private static ParaFormat2 NewParagraph() => new() { cbSize = Marshal.SizeOf<ParaFormat2>(), rgxTabs = new int[32] };

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CharFormat2
    {
        public int cbSize;
        public uint dwMask;
        public uint dwEffects;
        public int yHeight;
        public int yOffset;
        public int crTextColor;
        public byte bCharSet;
        public byte bPitchAndFamily;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
        public byte[] szFaceName; // 32 UTF-16 characters
        // In RichEdit CHARFORMAT2W derives from CHARFORMATW (90 bytes, rounded to 92): the added fields
        // start at 92, not 90. Without these 2 bytes wWeight was read instead of sSpacing.
        public short fill;
        public short wWeight;
        public short sSpacing;
        public int crBackColor;
        public int lcid;
        public int dwReserved;
        public short sStyle;
        public short wKerning;
        public byte bUnderlineType;
        public byte bAnimation;
        public byte bRevAuthor;
        public byte bUnderlineColor;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ParaFormat2
    {
        public int cbSize;
        public uint dwMask;
        public short wNumbering;
        public short wEffects;
        public int dxStartIndent;
        public int dxRightIndent;
        public int dxOffset;
        public short wAlignment;
        public short cTabCount;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public int[] rgxTabs;
        public int dySpaceBefore;
        public int dySpaceAfter;
        public int dyLineSpacing;
        public short sStyle;
        public byte bLineSpacingRule;
        public byte bOutlineLevel;
        public short wShadingWeight;
        public short wShadingStyle;
        public short wNumberingStart;
        public short wNumberingStyle;
        public short wNumberingTab;
        public short wBorderSpace;
        public short wBorderWidth;
        public short wBorders;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, ref CharFormat2 lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, ref ParaFormat2 lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);
}
