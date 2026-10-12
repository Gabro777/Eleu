using System.Runtime.InteropServices;
using Eleu.Documento;

namespace Eleu.Editor;

/// <summary>
/// Funzioni del controllo RichEdit che WinForms non espone: stili di titolo, testo giustificato
/// e formattazione che funziona anche su selezioni con formati misti.
/// </summary>
public static partial class RichEditNativo
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

    // Stili predefiniti di RichEdit: Normale = -1, Titolo 1 = -2, Titolo 2 = -3, ...
    private const short StileNormale = -1;

    public enum Effetto : uint
    {
        Grassetto = CfmBold,
        Corsivo = CfmItalic,
        Sottolineato = CfmUnderline,
    }

    /// <summary>Necessario perché il testo giustificato venga mostrato come tale.</summary>
    public static void AbilitaTipografiaAvanzata(RichTextBox campo) =>
        SendMessage(campo.Handle, EmSetTypographyOptions, ToAdvancedTypography, ToAdvancedTypography);

    /// <summary>
    /// Sottolineatura rossa delle parole sbagliate mentre si scrive, fatta dal campo di testo con il controllo
    /// ortografico di Windows (da Windows 8). È solo per chi vede: chi usa lo screen reader ha F7.
    /// </summary>
    public static void ControlloMentreScrivi(RichTextBox campo, bool attivo)
    {
        const int EmSetLangOptions = WmUser + 120, EmGetLangOptions = WmUser + 121, ImfSpellChecking = 0x0800;
        const int EmSetEditStyle = WmUser + 204, SesUseCtf = 0x00010000, SesCtfAllowProofing = 0x00800000;
        // Il controllo passa dal servizio di testo di Windows (TSF): senza, l'opzione da sola non sottolinea nulla.
        if (attivo)
            SendMessage(campo.Handle, EmSetEditStyle, SesUseCtf | SesCtfAllowProofing, SesUseCtf | SesCtfAllowProofing);
        int opzioni = (int)SendMessage(campo.Handle, EmGetLangOptions, 0, 0);
        opzioni = attivo ? opzioni | ImfSpellChecking : opzioni & ~ImfSpellChecking;
        SendMessage(campo.Handle, EmSetLangOptions, 0, opzioni);
    }

    /// <summary>Stato dell'effetto nella selezione: null se è misto.</summary>
    public static bool? StatoEffetto(RichTextBox campo, Effetto effetto)
    {
        var formato = LeggiCarattere(campo);
        if ((formato.dwMask & (uint)effetto) == 0)
            return null;
        return (formato.dwEffects & (uint)effetto) != 0;
    }

    public static void ImpostaEffetto(RichTextBox campo, Effetto effetto, bool attivo)
    {
        var formato = NuovoCarattere();
        formato.dwMask = (uint)effetto;
        formato.dwEffects = attivo ? (uint)effetto : 0;
        SendMessage(campo.Handle, EmSetCharFormat, ScfSelection, ref formato);
    }

    /// <summary>Dimensione in punti della selezione, null se mista.</summary>
    public static float? Dimensione(RichTextBox campo)
    {
        var formato = LeggiCarattere(campo);
        return (formato.dwMask & CfmSize) == 0 ? null : formato.yHeight / 20f;
    }

    public static void ImpostaDimensione(RichTextBox campo, float punti)
    {
        var formato = NuovoCarattere();
        formato.dwMask = CfmSize;
        formato.yHeight = (int)Math.Round(punti * 20);
        SendMessage(campo.Handle, EmSetCharFormat, ScfSelection, ref formato);
    }

    /// <summary>Aumenta o riduce la dimensione di tutta la selezione, anche se mista, mantenendo le proporzioni.</summary>
    public static void CambiaDimensione(RichTextBox campo, int passi) =>
        SendMessage(campo.Handle, EmSetFontSize, passi, 0);

    public static int LivelloTitolo(RichTextBox campo)
    {
        var formato = LeggiParagrafo(campo);
        return (formato.dwMask & PfmStyle) != 0 && formato.sStyle <= -2 && formato.sStyle >= -10
            ? -formato.sStyle - 1
            : 0;
    }

    public static void ImpostaLivelloTitolo(RichTextBox campo, int livello)
    {
        var formato = NuovoParagrafo();
        formato.dwMask = PfmStyle;
        formato.sStyle = livello == 0 ? StileNormale : (short)(-livello - 1);
        SendMessage(campo.Handle, EmSetParaFormat, 0, ref formato);
    }

    public static Allineamento LeggiAllineamento(RichTextBox campo) =>
        LeggiParagrafo(campo).wAlignment switch
        {
            PfaCenter => Allineamento.Centro,
            PfaRight => Allineamento.Destra,
            PfaJustify => Allineamento.Giustificato,
            _ => Allineamento.Sinistra,
        };

    public static void ImpostaAllineamento(RichTextBox campo, Allineamento allineamento)
    {
        var formato = NuovoParagrafo();
        formato.dwMask = PfmAlignment;
        formato.wAlignment = allineamento switch
        {
            Allineamento.Centro => PfaCenter,
            Allineamento.Destra => PfaRight,
            Allineamento.Giustificato => PfaJustify,
            _ => PfaLeft,
        };
        SendMessage(campo.Handle, EmSetParaFormat, 0, ref formato);
    }

    private static CharFormat2 LeggiCarattere(RichTextBox campo)
    {
        var formato = NuovoCarattere();
        SendMessage(campo.Handle, EmGetCharFormat, ScfSelection, ref formato);
        return formato;
    }

    private static ParaFormat2 LeggiParagrafo(RichTextBox campo)
    {
        var formato = NuovoParagrafo();
        SendMessage(campo.Handle, EmGetParaFormat, 0, ref formato);
        return formato;
    }

    private static CharFormat2 NuovoCarattere() => new() { cbSize = Marshal.SizeOf<CharFormat2>(), szFaceName = new byte[64] };

    private static ParaFormat2 NuovoParagrafo() => new() { cbSize = Marshal.SizeOf<ParaFormat2>(), rgxTabs = new int[32] };

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
        public byte[] szFaceName; // 32 caratteri UTF-16
        // In RichEdit CHARFORMAT2W eredita da CHARFORMATW (90 byte, arrotondati a 92): i campi aggiunti
        // iniziano a 92, non a 90. Senza questi 2 byte wWeight veniva letto al posto di sSpacing.
        public short riempimento;
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
