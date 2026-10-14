namespace Eleu.Documents;

public enum FileFormat { Docx, Text, Rtf, Html, Epub, Odt }

public static class FileFormats
{
    public const string OpenFilter =
        "Documenti, libri, PDF, Braille e immagini|*.docx;*.odt;*.txt;*.rtf;*.htm;*.html;*.xhtml;*.epub;*.pdf;*.brf;*.png;*.jpg;*.jpeg;*.tif;*.tiff;*.bmp;*.gif" +
        "|Documento Word (*.docx)|*.docx|Documento OpenDocument (*.odt)|*.odt|Testo semplice (*.txt)|*.txt|Rich Text (*.rtf)|*.rtf" +
        "|Pagina web (*.html)|*.htm;*.html;*.xhtml|Libro EPUB (*.epub)|*.epub|PDF (*.pdf)|*.pdf|File Braille (*.brf)|*.brf" +
        "|Immagini scansionate|*.png;*.jpg;*.jpeg;*.tif;*.tiff;*.bmp;*.gif|Tutti i file (*.*)|*.*";

    /// <summary>The order matches the values of <see cref="FileFormat"/> (FilterIndex = value + 1).</summary>
    public const string SaveFilter =
        "Documento Word (*.docx)|*.docx|Testo semplice (*.txt)|*.txt|Rich Text (*.rtf)|*.rtf|Pagina web (*.html)|*.html|Libro EPUB (*.epub)|*.epub" +
        "|Documento OpenDocument (*.odt)|*.odt";

    /// <summary>Format inferred from the extension; unknown files open as plain text.</summary>
    public static FileFormat FromPath(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".docx" => FileFormat.Docx,
            ".odt" => FileFormat.Odt,
            ".rtf" => FileFormat.Rtf,
            ".htm" or ".html" or ".xhtml" => FileFormat.Html,
            ".epub" => FileFormat.Epub,
            _ => FileFormat.Text,
        };
}
