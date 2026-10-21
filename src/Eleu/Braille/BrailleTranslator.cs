using System.Runtime.InteropServices;
using System.Text;

namespace Eleu.Braille;

/// <summary>A liblouis translation table that can be chosen: its file and its name in the window.</summary>
public sealed record BrailleTable(string File, string Name);

/// <summary>
/// Translates text into 6-dot Braille with liblouis, the standard library also used by NVDA and JAWS, with the table
/// chosen (Italian by default). Returns Unicode cells (U+2800–U+283F).
/// </summary>
public static class BrailleTranslator
{
    public const char EmptyCell = (char)0x2800;

    public const string DefaultTable = "it-it-comp6.utb";

    /// <summary>The tables Eleu ships, all 6-dot (the cells of BRF files and of 6-dot Braille fonts).</summary>
    public static readonly IReadOnlyList<BrailleTable> Tables =
    [
        new(DefaultTable, "Italiano"),
        new("en-ueb-g1.ctb", "Inglese UEB, integrale (grado 1)"),
        new("en-ueb-g2.ctb", "Inglese UEB, contratto (grado 2)"),
        new("fr-bfu-comp6.utb", "Francese, integrale"),
        new("fr-bfu-g2.ctb", "Francese, abbreviato"),
        new("de-g1.ctb", "Tedesco, Vollschrift (integrale)"),
        new("de-g2.ctb", "Tedesco, Kurzschrift (contratto)"),
        new("es-g1.ctb", "Spagnolo"),
        new("pt-pt-g1.utb", "Portoghese"),
    ];

    private static string _current = DefaultTable;

    /// <summary>The table used everywhere Eleu shows or reads Braille; an unknown one becomes the Italian table.</summary>
    public static string Current
    {
        get => _current;
        set => _current = KnownOrDefault(value);
    }

    /// <summary>The table if it is one of Eleu's, otherwise the Italian one (a settings file from elsewhere, a removed table).</summary>
    public static string KnownOrDefault(string? table) => Tables.Any(t => t.File == table) ? table! : DefaultTable;

    /// <summary>The name of a table for the user ("Italiano"); the file name if it is not one of Eleu's.</summary>
    public static string NameOf(string table) => Tables.FirstOrDefault(t => t.File == table)?.Name ?? table;

    // liblouis modes: output as dots (dotsIO) in Unicode Braille characters (ucBrl),
    // without escape sequences for unknown characters (noUndefined).
    private const int DotsIO = 4;
    private const int UcBrl = 64;
    private const int NoUndefined = 128;

    private static readonly Lock Block = new(); // liblouis is not thread-safe
    private static readonly Dictionary<string, string> Paths = [];

    /// <summary>The path of a table for liblouis (short, see <see cref="ShortPath"/>).</summary>
    private static string PathOf(string table)
    {
        lock (Paths)
        {
            if (!Paths.TryGetValue(table, out var path))
                Paths[table] = path = ShortPath(Path.Combine(AppContext.BaseDirectory, "liblouis", "tables", table));
            return path;
        }
    }

    /// <summary>Text into cells with the current table.</summary>
    public static string Translate(string text) => Translate(text, Current);

    public static string Translate(string text, string table)
    {
        if (text.Length == 0)
            return "";
        var path = PathOf(table);

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
                int outcome = lou_translateString(path, input, ref inputLength, output, ref outputLength,
                    IntPtr.Zero, IntPtr.Zero, DotsIO | UcBrl | NoUndefined);
                if (outcome == 0)
                    throw new InvalidOperationException($"liblouis non è riuscito a tradurre il testo (tabella: {table}).");

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

    /// <summary>Braille cells (U+2800–U+283F) back to text, with the current table: for BRF files to edit.</summary>
    public static string BackTranslate(string cells) => BackTranslate(cells, Current);

    public static string BackTranslate(string cells, string table)
    {
        if (cells.Length == 0)
            return "";
        var path = PathOf(table);

        var input = cells.Select(c => (uint)c).ToArray();
        int capacity = input.Length * 4 + 16;

        lock (Block)
        {
            while (true)
            {
                var output = new uint[capacity];
                int inputLength = input.Length;
                int outputLength = output.Length;
                int outcome = lou_backTranslateString(path, input, ref inputLength, output, ref outputLength,
                    IntPtr.Zero, IntPtr.Zero, DotsIO | UcBrl);
                if (outcome == 0)
                    throw new InvalidOperationException($"liblouis non è riuscito a leggere il Braille (tabella: {table}).");
                if (inputLength >= input.Length)
                    return Encoding.UTF32.GetString(MemoryMarshal.AsBytes(output.AsSpan(0, outputLength)));
                if (capacity > input.Length * 64)
                    throw new InvalidOperationException("liblouis non ha letto tutto il Braille.");
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

    [DllImport("liblouis", CharSet = CharSet.Ansi)]
    private static extern int lou_backTranslateString(string tables, uint[] input, ref int inputLength,
        [Out] uint[] output, ref int outputLength, IntPtr characterKind, IntPtr spacing, int mode);
}
