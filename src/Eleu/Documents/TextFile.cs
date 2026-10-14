using System.Text;

namespace Eleu.Documents;

/// <summary>
/// Reading and writing of plain text files, with automatic encoding detection:
/// UTF-8 (with or without BOM), UTF-16, otherwise Western ANSI (Windows-1252).
/// </summary>
public static class TextFile
{
    public static Encoding Utf8WithoutBom { get; } = new UTF8Encoding(false);

    public static Encoding Ansi
    {
        get
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(1252);
        }
    }

    public static (string Text, Encoding Encoding) Read(byte[] data)
    {
        Encoding encoding;
        int jump = 0;

        if (data.AsSpan().StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
        {
            encoding = new UTF8Encoding(true);
            jump = 3;
        }
        else if (data.AsSpan().StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE]))
        {
            encoding = Encoding.Unicode;
            jump = 2;
        }
        else if (data.AsSpan().StartsWith((ReadOnlySpan<byte>)[0xFE, 0xFF]))
        {
            encoding = Encoding.BigEndianUnicode;
            jump = 2;
        }
        else if (ValidUtf(data))
        {
            encoding = Utf8WithoutBom;
        }
        else
        {
            encoding = Ansi;
        }

        var text = encoding.GetString(data, jump, data.Length - jump);
        return (NormalizeLineBreaks(text), encoding);
    }

    public static byte[] Write(string text, Encoding encoding)
    {
        var content = NormalizeLineBreaks(text).Replace("\n", "\r\n");
        return [.. encoding.GetPreamble(), .. encoding.GetBytes(content)];
    }

    private static string NormalizeLineBreaks(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n');

    private static bool ValidUtf(byte[] data)
    {
        try
        {
            new UTF8Encoding(false, throwOnInvalidBytes: true).GetCharCount(data);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }
}
