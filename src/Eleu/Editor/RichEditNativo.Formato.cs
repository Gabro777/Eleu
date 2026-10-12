using System.Runtime.InteropServices;
using Eleu.Documento;

namespace Eleu.Editor;

/// <summary>Un colore: Rgb null = automatico (testo) o nessuno (sfondo).</summary>
public readonly record struct ValoreColore(int? Rgb);

/// <summary>
/// Formattazione del carattere nella selezione. Ogni proprietà null significa "mista" in lettura
/// e "non cambiare" in scrittura.
/// </summary>
public sealed record FormatoCarattere(
    string? Carattere = null,
    float? Dimensione = null,
    bool? Grassetto = null,
    bool? Corsivo = null,
    bool? Sottolineato = null,
    bool? Barrato = null,
    PosizioneTesto? Posizione = null,
    ValoreColore? Colore = null,
    ValoreColore? Sfondo = null,
    float? Spaziatura = null);

public enum TipoElenco { Nessuno, Puntato, Numerato }

/// <summary>Formattazione del paragrafo, in punti. Null = misto in lettura, "non cambiare" in scrittura.</summary>
public sealed record FormatoParagrafo(
    Allineamento? Allineamento = null,
    float? RientroSinistro = null,
    float? RientroDestro = null,
    float? RientroPrimaRiga = null,
    float? SpazioPrima = null,
    float? SpazioDopo = null,
    float? Interlinea = null,
    TipoElenco? Elenco = null);

public enum Maiuscole { Minuscolo = 0, Maiuscolo = 1, Iniziali = 2 }

public static partial class RichEditNativo
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
    private const int RientroElencoTwip = 360;

    private const int EmGetOleInterface = WmUser + 60;
    private static readonly Guid IidTextDocument = new("8CC497C0-A1DF-11CE-8098-00AA0047BE5D");

    public static FormatoCarattere LeggiFormatoCarattere(RichTextBox campo)
    {
        var f = LeggiCarattere(campo);
        bool Ha(uint maschera) => (f.dwMask & maschera) == maschera;
        bool? Effetto(uint bit) => Ha(bit) ? (f.dwEffects & bit) != 0 : null;

        PosizioneTesto? posizione = null;
        if (Ha(CfmSuperSub))
            posizione = (f.dwEffects & CfeSuperscript) != 0 ? PosizioneTesto.Apice
                : (f.dwEffects & CfeSubscript) != 0 ? PosizioneTesto.Pedice
                : PosizioneTesto.Normale;

        return new FormatoCarattere(
            Ha(CfmFace) ? System.Text.Encoding.Unicode.GetString(f.szFaceName).TrimEnd('\0') : null,
            Ha(CfmSize) ? f.yHeight / 20f : null,
            Effetto(CfmBold),
            Effetto(CfmItalic),
            Effetto(CfmUnderline),
            Effetto(CfmStrikeout),
            posizione,
            Ha(CfmColor) ? new ValoreColore((f.dwEffects & CfeAutoColor) != 0 ? null : DaColorRef(f.crTextColor)) : null,
            Ha(CfmBackColor) ? new ValoreColore((f.dwEffects & CfeAutoBackColor) != 0 ? null : DaColorRef(f.crBackColor)) : null,
            Ha(CfmSpacing) ? f.sSpacing / 20f : null);
    }

    /// <summary>Applica alla selezione solo le proprietà non null.</summary>
    public static void ImpostaFormatoCarattere(RichTextBox campo, FormatoCarattere modifiche)
    {
        var f = NuovoCarattere();
        void Effetto(bool? valore, uint bit)
        {
            if (valore is not bool attivo)
                return;
            f.dwMask |= bit;
            if (attivo)
                f.dwEffects |= bit;
        }

        if (modifiche.Carattere is string carattere)
        {
            f.dwMask |= CfmFace;
            var nome = System.Text.Encoding.Unicode.GetBytes(carattere.Length > 31 ? carattere[..31] : carattere);
            nome.CopyTo(f.szFaceName, 0);
        }
        if (modifiche.Dimensione is float dimensione)
        {
            f.dwMask |= CfmSize;
            f.yHeight = (int)Math.Round(dimensione * 20);
        }
        Effetto(modifiche.Grassetto, CfmBold);
        Effetto(modifiche.Corsivo, CfmItalic);
        Effetto(modifiche.Sottolineato, CfmUnderline);
        Effetto(modifiche.Barrato, CfmStrikeout);
        if (modifiche.Posizione is PosizioneTesto posizione)
        {
            f.dwMask |= CfmSuperSub;
            f.dwEffects |= posizione switch
            {
                PosizioneTesto.Apice => CfeSuperscript,
                PosizioneTesto.Pedice => CfeSubscript,
                _ => 0,
            };
        }
        if (modifiche.Colore is ValoreColore colore)
        {
            f.dwMask |= CfmColor;
            if (colore.Rgb is int rgb)
                f.crTextColor = InColorRef(rgb);
            else
                f.dwEffects |= CfeAutoColor;
        }
        if (modifiche.Sfondo is ValoreColore sfondo)
        {
            f.dwMask |= CfmBackColor;
            if (sfondo.Rgb is int rgb)
                f.crBackColor = InColorRef(rgb);
            else
                f.dwEffects |= CfeAutoBackColor;
        }
        if (modifiche.Spaziatura is float spaziatura)
        {
            f.dwMask |= CfmSpacing;
            f.sSpacing = (short)Math.Round(spaziatura * 20);
        }
        if (f.dwMask != 0)
            SendMessage(campo.Handle, EmSetCharFormat, ScfSelection, ref f);
    }

    public static FormatoParagrafo LeggiFormatoParagrafo(RichTextBox campo)
    {
        var p = LeggiParagrafo(campo);
        bool Ha(uint maschera) => (p.dwMask & maschera) == maschera;

        TipoElenco? elenco = Ha(PfmNumbering)
            ? p.wNumbering switch { 0 => TipoElenco.Nessuno, PfnBullet => TipoElenco.Puntato, _ => TipoElenco.Numerato }
            : null;
        bool inElenco = elenco is TipoElenco.Puntato or TipoElenco.Numerato;

        // RichEdit: dxStartIndent è il rientro della prima riga, dxOffset quello delle righe seguenti rispetto alla prima.
        float? sinistro = Ha(PfmStartIndent | PfmOffset) ? (p.dxStartIndent + p.dxOffset) / 20f : null;
        float? primaRiga = Ha(PfmOffset) ? -p.dxOffset / 20f : null;
        if (inElenco)
        {
            // Negli elenchi lo spazio per il pallino o il numero non è un rientro scelto dall'utente.
            sinistro = sinistro is float s ? Math.Max(0, s - RientroElencoTwip / 20f) : null;
            primaRiga = 0;
        }

        float? interlinea = Ha(PfmLineSpacing)
            ? p.bLineSpacingRule switch
            {
                1 => 1.5f,
                2 => 2f,
                5 => p.dyLineSpacing / 20f,
                _ => 1f,
            }
            : null;

        return new FormatoParagrafo(
            Ha(PfmAlignment) ? LeggiAllineamento(campo) : null,
            sinistro,
            Ha(PfmRightIndent) ? p.dxRightIndent / 20f : null,
            primaRiga,
            Ha(PfmSpaceBefore) ? p.dySpaceBefore / 20f : null,
            Ha(PfmSpaceAfter) ? p.dySpaceAfter / 20f : null,
            interlinea,
            elenco);
    }

    /// <summary>Applica ai paragrafi selezionati solo le proprietà non null.</summary>
    public static void ImpostaFormatoParagrafo(RichTextBox campo, FormatoParagrafo modifiche)
    {
        if (modifiche.Allineamento is Allineamento allineamento)
            ImpostaAllineamento(campo, allineamento);

        var attuale = LeggiFormatoParagrafo(campo);
        var elenco = modifiche.Elenco ?? attuale.Elenco ?? TipoElenco.Nessuno;
        var p = NuovoParagrafo();

        if (modifiche.RientroSinistro is not null || modifiche.RientroPrimaRiga is not null || modifiche.Elenco is not null)
        {
            float sinistro = modifiche.RientroSinistro ?? attuale.RientroSinistro ?? 0;
            float primaRiga = elenco == TipoElenco.Nessuno ? modifiche.RientroPrimaRiga ?? attuale.RientroPrimaRiga ?? 0 : 0;
            int sinistroTwip = (int)Math.Round(sinistro * 20);
            int primaRigaTwip = (int)Math.Round(primaRiga * 20);
            p.dwMask |= PfmStartIndent | PfmOffset | PfmNumbering | PfmNumberingStyle | PfmNumberingStart | PfmNumberingTab;
            if (elenco == TipoElenco.Nessuno)
            {
                p.dxStartIndent = sinistroTwip + primaRigaTwip;
                p.dxOffset = -primaRigaTwip;
                p.wNumbering = 0;
            }
            else
            {
                // Come scrive l'RTF di Eleu: il pallino o il numero sporge di 18 punti dal testo.
                p.dxStartIndent = sinistroTwip;
                p.dxOffset = RientroElencoTwip;
                p.wNumbering = elenco == TipoElenco.Puntato ? PfnBullet : PfnArabic;
                p.wNumberingStyle = PfnsPeriod;
                p.wNumberingStart = 1;
                p.wNumberingTab = RientroElencoTwip;
            }
        }
        if (modifiche.RientroDestro is float destro)
        {
            p.dwMask |= PfmRightIndent;
            p.dxRightIndent = (int)Math.Round(destro * 20);
        }
        if (modifiche.SpazioPrima is float prima)
        {
            p.dwMask |= PfmSpaceBefore;
            p.dySpaceBefore = (int)Math.Round(prima * 20);
        }
        if (modifiche.SpazioDopo is float dopo)
        {
            p.dwMask |= PfmSpaceAfter;
            p.dySpaceAfter = (int)Math.Round(dopo * 20);
        }
        if (modifiche.Interlinea is float interlinea)
        {
            // Regola 5: dyLineSpacing / 20 = numero di righe (20 = singola, 30 = una e mezza, 40 = doppia).
            p.dwMask |= PfmLineSpacing;
            p.bLineSpacingRule = 5;
            p.dyLineSpacing = (int)Math.Round(interlinea * 20);
        }
        if (p.dwMask != 0)
            SendMessage(campo.Handle, EmSetParaFormat, 0, ref p);
    }

    /// <summary>
    /// Cambia maiuscole e minuscole tra due posizioni con il Text Object Model di RichEdit:
    /// la formattazione resta e l'operazione si annulla con un solo Ctrl+Z.
    /// </summary>
    public static void CambiaMaiuscole(RichTextBox campo, int inizio, int fine, Maiuscole tipo)
    {
        IntPtr ole = IntPtr.Zero, documento = IntPtr.Zero;
        try
        {
            SendMessage(campo.Handle, EmGetOleInterface, 0, out ole);
            if (ole == IntPtr.Zero)
                return;
            var iid = IidTextDocument;
            if (Marshal.QueryInterface(ole, in iid, out documento) != 0)
                return;
            dynamic tom = Marshal.GetObjectForIUnknown(documento);
            tom.Range(inizio, fine).ChangeCase((int)tipo);
        }
        finally
        {
            if (documento != IntPtr.Zero)
                Marshal.Release(documento);
            if (ole != IntPtr.Zero)
                Marshal.Release(ole);
        }
    }

    /// <summary>
    /// Il documento del Text Object Model di RichEdit (ITextDocument), da usare con dynamic. I suoi intervalli
    /// (Range) seguono il testo mentre si scrive: servono per i segnalibri. Null se non disponibile.
    /// </summary>
    public static object? DocumentoTom(RichTextBox campo)
    {
        IntPtr ole = IntPtr.Zero, documento = IntPtr.Zero;
        try
        {
            SendMessage(campo.Handle, EmGetOleInterface, 0, out ole);
            if (ole == IntPtr.Zero)
                return null;
            var iid = IidTextDocument;
            return Marshal.QueryInterface(ole, in iid, out documento) == 0 ? Marshal.GetObjectForIUnknown(documento) : null;
        }
        finally
        {
            if (documento != IntPtr.Zero)
                Marshal.Release(documento);
            if (ole != IntPtr.Zero)
                Marshal.Release(ole);
        }
    }

    // COLORREF di Windows è 0x00BBGGRR, Eleu usa 0xRRGGBB.
    private static int InColorRef(int rgb) => ((rgb & 0xFF) << 16) | (rgb & 0xFF00) | ((rgb >> 16) & 0xFF);

    private static int DaColorRef(int colorRef) => InColorRef(colorRef);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, out IntPtr lParam);
}
