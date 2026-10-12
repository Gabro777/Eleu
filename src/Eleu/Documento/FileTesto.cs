using System.Text;

namespace Eleu.Documento;

/// <summary>
/// Lettura e scrittura dei file di testo semplice, con riconoscimento automatico della codifica:
/// UTF-8 (con o senza BOM), UTF-16, altrimenti ANSI occidentale (Windows-1252).
/// </summary>
public static class FileTesto
{
    public static Encoding Utf8SenzaBom { get; } = new UTF8Encoding(false);

    public static Encoding Ansi
    {
        get
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(1252);
        }
    }

    public static (string Testo, Encoding Codifica) Leggi(byte[] dati)
    {
        Encoding codifica;
        int salto = 0;

        if (dati.AsSpan().StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
        {
            codifica = new UTF8Encoding(true);
            salto = 3;
        }
        else if (dati.AsSpan().StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE]))
        {
            codifica = Encoding.Unicode;
            salto = 2;
        }
        else if (dati.AsSpan().StartsWith((ReadOnlySpan<byte>)[0xFE, 0xFF]))
        {
            codifica = Encoding.BigEndianUnicode;
            salto = 2;
        }
        else if (UtfValido(dati))
        {
            codifica = Utf8SenzaBom;
        }
        else
        {
            codifica = Ansi;
        }

        var testo = codifica.GetString(dati, salto, dati.Length - salto);
        return (NormalizzaAcapo(testo), codifica);
    }

    public static byte[] Scrivi(string testo, Encoding codifica)
    {
        var contenuto = NormalizzaAcapo(testo).Replace("\n", "\r\n");
        return [.. codifica.GetPreamble(), .. codifica.GetBytes(contenuto)];
    }

    private static string NormalizzaAcapo(string testo) => testo.Replace("\r\n", "\n").Replace('\r', '\n');

    private static bool UtfValido(byte[] dati)
    {
        try
        {
            new UTF8Encoding(false, throwOnInvalidBytes: true).GetCharCount(dati);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }
}
