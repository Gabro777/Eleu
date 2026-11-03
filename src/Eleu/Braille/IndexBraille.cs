using System.Text;
using System.Text.RegularExpressions;

namespace Eleu.Braille;

/// <summary>
/// Index Braille embossers (Basic-D, Everest-D, BrailleBox, FanFold) with the BRF file sent directly: before the text a
/// line of the embosser's own commands sets cells per line, lines per page, single or double sided, copies and the
/// Braille table, so the embosser follows what is chosen in Eleu instead of its own settings; after the text the end of
/// document character (Ctrl+Z), without which the embosser can keep waiting for more and not start. The same commands
/// other Braille programs send to these embossers.
/// </summary>
public static partial class IndexBraille
{
    /// <summary>The end of document character.</summary>
    public const byte EndOfDocument = 0x1A;

    /// <summary>The printer's name says it is an Index Braille embosser.</summary>
    public static bool LooksLikeIndex(string printer) => IndexName().IsMatch(printer);

    [GeneratedRegex(@"\bindex\b|everest|braille ?box|fan ?fold", RegexOptions.IgnoreCase)]
    private static partial Regex IndexName();

    /// <summary>
    /// The command line: BT0 the table of BRF files, LS50 the usual line distance, TD0 6 dots, PN0 no page number by the
    /// embosser (Eleu puts its own), MC copies, DP 1 one side or 2 both sides, BI0 no binding margin (Eleu puts it in the
    /// lines), CH cells per line, TM0 no top margin, LP lines per page.
    /// </summary>
    public static string Header(PageFormat format, int copies) =>
        $"\u001bDBT0,LS50,TD0,PN0,MC{Math.Max(1, copies)},DP{(format.DoubleSided ? 2 : 1)},BI0,CH{format.CellsPerLine},TM0,LP{format.LinesPerPage};";

    /// <summary>The whole job: the commands, the BRF file, the end of document.</summary>
    public static byte[] Job(byte[] brf, PageFormat format, int copies) =>
        [.. Encoding.ASCII.GetBytes(Header(format, copies)), .. brf, EndOfDocument];
}
