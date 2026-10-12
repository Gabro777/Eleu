using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Eleu.Documento;

/// <summary>
/// Un'immagine del documento: i dati (PNG o JPEG), la dimensione con cui si vede, in punti, e la descrizione
/// che legge lo screen reader (il "testo alternativo" di Word).
/// </summary>
public sealed record Immagine(byte[] Dati, string Formato, float Larghezza, float Altezza, string Descrizione)
{
    public string Misure => $"{Larghezza * 2.54f / 72f:0.#} per {Altezza * 2.54f / 72f:0.#} centimetri";
}

/// <summary>
/// Immagini nei file e nel campo di testo. Come per segnalibri e note, durante caricamento e salvataggio
/// ogni immagine è un carattere segnaposto (area Unicode a uso privato, da U+F200); nel documento aperto è
/// l'immagine vera, che il campo di testo mostra e stampa da sé.
/// </summary>
public static partial class Immagini
{
    private const int Primo = 0xF200;
    public const int Massimo = 1024; // da U+F200 a U+F5FF; dopo vengono i commenti

    /// <summary>Larghezza massima di un'immagine appena inserita: 15 cm, in punti.</summary>
    public const float LarghezzaMassima = 15 * 72f / 2.54f;

    public static char Segnaposto(int indice) => (char)(Primo + indice);

    public static int Indice(char carattere) =>
        carattere >= Primo && carattere < Primo + Massimo ? carattere - Primo : -1;

    /// <summary>
    /// Un'immagine da un file o da dei dati qualsiasi: PNG e JPEG restano come sono, gli altri formati
    /// (BMP, GIF, TIFF, ...) diventano PNG. La dimensione è quella naturale, ridotta se più larga di 15 cm.
    /// </summary>
    public static Immagine DaDati(byte[] dati, string descrizione, float? larghezzaVoluta = null)
    {
        using var flusso = new MemoryStream(dati);
        using var bitmap = System.Drawing.Image.FromStream(flusso);
        string formato;
        if (bitmap.RawFormat.Guid == ImageFormat.Png.Guid)
            formato = "png";
        else if (bitmap.RawFormat.Guid == ImageFormat.Jpeg.Guid)
            formato = "jpeg";
        else
        {
            using var png = new MemoryStream();
            bitmap.Save(png, ImageFormat.Png);
            dati = png.ToArray();
            formato = "png";
        }
        // Il PNG conserva la risoluzione in punti per metro: 96 dpi tornano 95,98. Si arrotonda.
        float dpiX = bitmap.HorizontalResolution > 1 ? MathF.Round(bitmap.HorizontalResolution) : 96;
        float dpiY = bitmap.VerticalResolution > 1 ? MathF.Round(bitmap.VerticalResolution) : 96;
        float larghezza = bitmap.Width * 72f / dpiX, altezza = bitmap.Height * 72f / dpiY;
        float voluta = larghezzaVoluta ?? Math.Min(larghezza, LarghezzaMassima);
        return new Immagine(dati, formato, voluta, altezza * voluta / larghezza, descrizione);
    }

    /// <summary>Lo stesso con un'altra larghezza; l'altezza segue, per non deformarla.</summary>
    public static Immagine ConLarghezza(Immagine immagine, float larghezza) =>
        immagine with { Larghezza = larghezza, Altezza = immagine.Altezza * larghezza / immagine.Larghezza };

    /// <summary>
    /// L'immagine in RTF, come la scrive Word: dati in esadecimale, dimensione in twip e la descrizione
    /// in wzDescription (dentro \picprop, che il campo di testo ignora ma Word legge).
    /// </summary>
    public static string Pict(Immagine immagine)
    {
        using var flusso = new MemoryStream(immagine.Dati);
        using var bitmap = System.Drawing.Image.FromStream(flusso);
        var rtf = new StringBuilder("{");
        rtf.Append(@"\pict{\*\picprop{\sp{\sn wzDescription}{\sv ");
        Escapa(immagine.Descrizione, rtf);
        rtf.Append("}}}");
        rtf.Append(immagine.Formato == "jpeg" ? @"\jpegblip" : @"\pngblip");
        rtf.Append(@"\picw").Append(bitmap.Width).Append(@"\pich").Append(bitmap.Height);
        rtf.Append(@"\picwgoal").Append(RtfScrittore.Twip(immagine.Larghezza)).Append(@"\pichgoal").Append(RtfScrittore.Twip(immagine.Altezza));
        rtf.Append(' ');
        for (int i = 0; i < immagine.Dati.Length; i++)
        {
            rtf.Append(immagine.Dati[i].ToString("x2", CultureInfo.InvariantCulture));
            if (i % 64 == 63)
                rtf.Append("\r\n");
        }
        return rtf.Append('}').ToString();
    }

    /// <summary>Un documento RTF che contiene solo l'immagine, da inserire nel campo di testo.</summary>
    public static string RtfSoloImmagine(Immagine immagine) => "{" + @"\rtf1\ansi " + Pict(immagine) + "}";

    /// <summary>
    /// RTF di un file → stesso RTF con un segnaposto al posto di ogni immagine; le immagini vanno in
    /// <paramref name="immagini"/>. Da Word arriva spesso la stessa immagine due volte, {\*\shppict} e
    /// {\nonshppict} (riserva per i programmi vecchi): si tiene la prima e si scarta la riserva.
    /// Le immagini in formati che non si sanno leggere restano come sono.
    /// </summary>
    public static string InSegnaposto(string rtf, List<Immagine> immagini)
    {
        var risultato = new StringBuilder();
        int da = 0;
        foreach (Match m in GruppoImmagine().Matches(rtf))
        {
            if (m.Index < da)
                continue;
            int fine = FineGruppo(rtf, m.Index);
            if (fine < 0)
                break;
            var gruppo = rtf[m.Index..(fine + 1)];
            risultato.Append(rtf, da, m.Index - da);
            da = fine + 1;
            if (gruppo.StartsWith(@"{\nonshppict", StringComparison.Ordinal))
                continue; // la riserva
            if (immagini.Count < Massimo && Leggi(gruppo) is { } immagine)
            {
                immagini.Add(immagine);
                risultato.Append('{').Append(CarattereRtf(Segnaposto(immagini.Count - 1))).Append('}');
            }
            else
            {
                risultato.Append(gruppo);
            }
        }
        risultato.Append(rtf, da, rtf.Length - da);
        return risultato.ToString();
    }

    /// <summary>RTF con i segnaposto → RTF con le immagini.</summary>
    public static string DaSegnaposto(string rtf, IReadOnlyList<Immagine> immagini) =>
        UnicodeRtf().Replace(rtf, m =>
        {
            int indice = Indice((char)(ushort)short.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture));
            return indice >= 0 && indice < immagini.Count ? Pict(immagini[indice]) : m.Value;
        });

    /// <summary>Un gruppo {\pict ...} (anche dentro {\*\shppict}) come immagine, o null se il formato non si sa leggere.</summary>
    private static Immagine? Leggi(string gruppo)
    {
        int inizioPict = gruppo.IndexOf(@"\pict", StringComparison.Ordinal);
        if (inizioPict < 0)
            return null;
        var descrizione = Descrizione().Match(gruppo) is { Success: true } d ? TestoRtf(d.Groups[1].Value) : "";
        // I dati esadecimali sono ciò che resta togliendo i gruppi interni e le parole di controllo.
        var corpo = GruppiInterni().Replace(gruppo[(inizioPict + 5)..], "");
        var parole = ParoleDiControllo().Matches(corpo).ToDictionary(p => p.Groups[1].Value, p => p.Groups[2].Value, StringComparer.Ordinal);
        var esadecimale = new StringBuilder();
        foreach (var c in ParoleDiControllo().Replace(corpo, ""))
            if (Uri.IsHexDigit(c))
                esadecimale.Append(c);
        if (esadecimale.Length < 2)
            return null;
        byte[] dati;
        try
        {
            dati = Convert.FromHexString(esadecimale.Length % 2 == 0 ? esadecimale.ToString() : esadecimale.ToString(0, esadecimale.Length - 1));
        }
        catch (FormatException)
        {
            return null;
        }

        float? larghezza = parole.TryGetValue("picwgoal", out var w) && int.TryParse(w, out var tw) && tw > 0 ? tw / 20f : null;
        float scala = parole.TryGetValue("picscalex", out var sx) && int.TryParse(sx, out var s) && s > 0 ? s / 100f : 1f;
        try
        {
            var immagine = DaDati(dati, descrizione, larghezza is float l ? l * scala : null);
            return immagine;
        }
        catch (Exception e) when (e is ArgumentException or ExternalException or OutOfMemoryException)
        {
            return null; // formato che System.Drawing non legge (per esempio WMF senza intestazione)
        }
    }

    private static int FineGruppo(string rtf, int apertura)
    {
        int livello = 0;
        for (int i = apertura; i < rtf.Length; i++)
        {
            switch (rtf[i])
            {
                case '\\':
                    i++;
                    break;
                case '{':
                    livello++;
                    break;
                case '}':
                    if (--livello == 0)
                        return i;
                    break;
            }
        }
        return -1;
    }

    private static string CarattereRtf(char c) => $@"\uc1\u{(short)c}?";

    private static void Escapa(string testo, StringBuilder rtf)
    {
        foreach (var c in testo)
        {
            if (c is '\\' or '{' or '}')
                rtf.Append('\\').Append(c);
            else if (c > 127)
                rtf.Append(CarattereRtf(c));
            else if (c >= 32)
                rtf.Append(c);
            else
                rtf.Append(' ');
        }
    }

    private static string TestoRtf(string rtf) =>
        CodiceRtf().Replace(SaltoUnicode().Replace(rtf, ""), m => m.Groups[1].Success
            ? ((char)byte.Parse(m.Groups[1].Value, NumberStyles.HexNumber)).ToString()
            : m.Groups[2].Success
                ? ((char)(ushort)short.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture)).ToString()
                : m.Groups[3].Value).Trim();

    [GeneratedRegex(@"\{\\(?:\*\\shppict|nonshppict|pict)(?![a-z])")]
    private static partial Regex GruppoImmagine();

    [GeneratedRegex(@"\{\\sn wzDescription\}\{\\sv ?((?:[^{}\\]|\\.)*)\}")]
    private static partial Regex Descrizione();

    [GeneratedRegex(@"\{[^{}]*(?:\{[^{}]*(?:\{[^{}]*\}[^{}]*)*\}[^{}]*)*\}")]
    private static partial Regex GruppiInterni();

    [GeneratedRegex(@"\\([a-z]+)(-?\d*) ?")]
    private static partial Regex ParoleDiControllo();

    [GeneratedRegex(@"\\u(-?\d+) ?(?:\\'[0-9a-fA-F]{2}|\?)?")]
    private static partial Regex UnicodeRtf();

    [GeneratedRegex(@"\\'([0-9a-fA-F]{2})|\\u(-?\d+)\??|\\([\\{}])")]
    private static partial Regex CodiceRtf();

    [GeneratedRegex(@"\\uc\d+ ?")]
    private static partial Regex SaltoUnicode();
}
