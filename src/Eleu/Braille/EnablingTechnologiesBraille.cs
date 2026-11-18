using System.Text;
using System.Text.RegularExpressions;

namespace Eleu.Braille;

/// <summary>
/// The earlier Enabling Technologies embossers (Juliet Pro and Classic, Romeo Pro and Attaché, Thomas, Trident, ET,
/// BookMaker, Braille Express, Marathon...) with the BRF file sent directly: before the text a few commands of their
/// own, each an escape, a letter and a value, set the table of BRF files, 6 dots, single or double sided, cells per
/// line and lines per page. Without the table command the embosser reads the file with the table set on it, and
/// accented letters, capitals and punctuation come out wrong. The newer Juliet 120 and Romeo 60 are built on the
/// Index embossers and take their commands instead (see <see cref="IndexBraille"/>).
/// </summary>
public static partial class EnablingTechnologiesBraille
{
    private const char Escape = '\u001b';

    /// <summary>The printer's name says it is one of these embossers (and not a Juliet 120 or a Romeo 60).</summary>
    public static bool LooksLikeEnablingTechnologies(string printer) =>
        !IndexBraille.LooksLikeIndex(printer) && (EarlierName().IsMatch(printer) || EtName().IsMatch(printer));

    [GeneratedRegex(@"enabling|juliet ?(pro|classic)|romeo ?(pro|25|attach)|trident|\bthomas\b|marathon|book ?maker|braille ?express|phoenix|cyclone", RegexOptions.IgnoreCase)]
    private static partial Regex EarlierName();

    /// <summary>"ET" as a word, in capitals: the model called just ET.</summary>
    [GeneratedRegex(@"\bET\b")]
    private static partial Regex EtName();

    /// <summary>A number as these embossers want it: the character "@" is 0, "A" is 1 and so on, up to 59.</summary>
    public static char Number(int value) => (char)('@' + Math.Clamp(value, 0, 59));

    /// <summary>
    /// The commands: A@@ the table of BRF files, K@ 6 dots, W@ no wrapping by the embosser (Eleu wraps the lines), i@
    /// both sides or iA one side, s@ the standard cell, LA the text from the first cell (Eleu puts the binding margin in
    /// the lines), R cells per line, Q lines per page. The length of the sheet is left as set on the embosser, which
    /// knows the paper loaded in it.
    /// </summary>
    public static string Header(PageFormat format) =>
        $"{Escape}A@@{Escape}K@{Escape}W@{Escape}i{(format.DoubleSided ? '@' : 'A')}{Escape}s@{Escape}LA" +
        $"{Escape}R{Number(format.CellsPerLine)}{Escape}Q{Number(format.LinesPerPage)}";

    /// <summary>The whole job: the commands, then the BRF file with the copies one after the other.</summary>
    public static byte[] Job(IReadOnlyList<List<string>> pages, PageFormat format, int copies) =>
        [.. Encoding.ASCII.GetBytes(Header(format)), .. BrfFormat.Copies(pages, copies, format.DoubleSided)];
}

/// <summary>What Eleu sends with the BRF file to a printer that receives it directly.</summary>
public enum EmbosserModel
{
    /// <summary>Only the BRF file: the printer's own settings apply.</summary>
    Generic,

    /// <summary>Index Braille, and the Juliet 120 and Romeo 60 built on them: see <see cref="IndexBraille"/>.</summary>
    Index,

    /// <summary>The earlier Enabling Technologies embossers: see <see cref="EnablingTechnologiesBraille"/>.</summary>
    EnablingTechnologies,
}

public static class EmbosserModels
{
    /// <summary>The model as saved in the settings, for each printer.</summary>
    public static string Key(EmbosserModel model) => model switch
    {
        EmbosserModel.Index => "Index",
        EmbosserModel.EnablingTechnologies => "Enabling Technologies",
        _ => "Generica",
    };

    public static EmbosserModel FromKey(string? key) => key switch
    {
        "Index" => EmbosserModel.Index,
        "Enabling Technologies" => EmbosserModel.EnablingTechnologies,
        _ => EmbosserModel.Generic,
    };

    /// <summary>The model guessed from the printer's name, for a printer never used before.</summary>
    public static EmbosserModel Guess(string printer) =>
        IndexBraille.LooksLikeIndex(printer) ? EmbosserModel.Index
        : EnablingTechnologiesBraille.LooksLikeEnablingTechnologies(printer) ? EmbosserModel.EnablingTechnologies
        : EmbosserModel.Generic;

    /// <summary>The bytes for the printer: the model's commands, if any, the pages and the copies.</summary>
    public static byte[] Job(EmbosserModel model, IReadOnlyList<List<string>> pages, PageFormat format, int copies) => model switch
    {
        EmbosserModel.Index => IndexBraille.Job(BrfFormat.Byte(pages), format, copies),
        EmbosserModel.EnablingTechnologies => EnablingTechnologiesBraille.Job(pages, format, copies),
        _ => BrfFormat.Copies(pages, copies, format.DoubleSided),
    };
}
