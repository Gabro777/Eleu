using System.Text;

namespace Eleu.Braille;

/// <summary>
/// BRF file: the Braille text format accepted by almost all Braille printers
/// and embossing programs. Each cell becomes an ASCII character ("North American
/// ASCII Braille" encoding), lines end with CR LF and pages with a form feed.
/// </summary>
public static class BrfFormat
{
    // ASCII character for each of the 64 six-dot cells, in order of dot value (dot 1 = bit 0).
    public const string AsciiTable = @" A1B'K2L@CIF/MSP""E3H9O6R^DJG>NTQ,*5<-U8V.%[$+X!&;:4\0Z7(_?W]#Y)=";

    public static char ToAscii(char cell) =>
        cell >= BrailleTranslator.EmptyCell && cell - BrailleTranslator.EmptyCell < AsciiTable.Length ? AsciiTable[cell - BrailleTranslator.EmptyCell] : ' ';

    public static string Text(IReadOnlyList<List<string>> pages)
    {
        var brf = new StringBuilder();
        foreach (var page in pages)
        {
            foreach (var row in page)
            {
                foreach (char cell in row)
                    brf.Append(ToAscii(cell));
                brf.Append("\r\n");
            }
            brf.Append('\f');
        }
        return brf.ToString();
    }

    public static byte[] Byte(IReadOnlyList<List<string>> pages) => Encoding.ASCII.GetBytes(Text(pages));
}
