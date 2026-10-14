using System.Runtime.InteropServices;
using System.Text;

namespace Eleu.Braille;

/// <summary>
/// Translates text into 6-dot Italian Braille with liblouis (table it-it-comp6),
/// the standard library also used by NVDA and JAWS. Returns Unicode cells (U+2800–U+283F).
/// </summary>
public static class BrailleTranslator
{
    public const char EmptyCell = (char)0x2800;

    // liblouis modes: output as dots (dotsIO) in Unicode Braille characters (ucBrl),
    // without escape sequences for unknown characters (noUndefined).
    private const int DotsIO = 4;
    private const int UcBrl = 64;
    private const int NoUndefined = 128;

    private static readonly Lock Block = new(); // liblouis is not thread-safe
    private static readonly string Table =
        ShortPath(Path.Combine(AppContext.BaseDirectory, "liblouis", "tables", "it-it-comp6.utb"));

    public static string Translate(string text)
    {
        if (text.Length == 0)
            return "";

        // In this liblouis build characters are 32-bit (UTF-32).
        var input = MemoryMarshal.Cast<byte, uint>(Encoding.UTF32.GetBytes(text)).ToArray();
        int capacity = input.Length * 4 + 16;

        lock (Block)
        {
            while (true)
            {
                var output = new uint[capacity];
                int inputLength = input.Length;
                int outputLength = output.Length;
                int outcome = lou_translateString(Table, input, ref inputLength, output, ref outputLength,
                    IntPtr.Zero, IntPtr.Zero, DotsIO | UcBrl | NoUndefined);
                if (outcome == 0)
                    throw new InvalidOperationException($"liblouis non è riuscito a tradurre il testo (tabella: {Table}).");

                // If the output buffer was too small, liblouis stops before the end: retry with a bigger one.
                if (inputLength >= input.Length)
                {
                    var cells = new StringBuilder(outputLength);
                    for (int i = 0; i < outputLength; i++)
                        cells.Append(InCell(output[i]));
                    return cells.ToString();
                }
                if (capacity > input.Length * 64)
                    throw new InvalidOperationException("liblouis non ha tradotto tutto il testo.");
                capacity *= 2;
            }
        }
    }

    /// <summary>Brings every value into the 6-dot cell range; anything else becomes an empty cell.</summary>
    private static char InCell(uint @value)
    {
        if (@value is >= 0x2800 and <= 0x28FF)
            return (char)(0x2800 | (@value & 0x3F));
        if (@value is >= 0x8000 and <= 0x80FF) // dots without Unicode: 0x8000 | dots
            return (char)(0x2800 | (@value & 0x3F));
        return EmptyCell;
    }

    /// <summary>
    /// liblouis opens the tables with the Windows ANSI encoding: if Eleu is in a folder with accented
    /// letters (e.g. C:\Users\Niccolò) the path would not be found. The 8.3 short path is always ASCII.
    /// </summary>
    private static string ShortPath(string path)
    {
        var @short = new StringBuilder(1024);
        return GetShortPathName(path, @short, @short.Capacity) > 0 ? @short.ToString() : path;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetShortPathNameW")]
    private static extern int GetShortPathName(string longText, StringBuilder @short, int capacity);

    [DllImport("liblouis", CharSet = CharSet.Ansi)]
    private static extern int lou_translateString(string tables, uint[] input, ref int inputLength,
        [Out] uint[] output, ref int outputLength, IntPtr characterKind, IntPtr spacing, int mode);
}
