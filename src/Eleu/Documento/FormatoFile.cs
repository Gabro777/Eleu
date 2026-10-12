namespace Eleu.Documento;

public enum FormatoFile { Docx, Testo, Rtf, Html, Epub, Odt }

public static class FormatiFile
{
    public const string FiltroApri =
        "Documenti, libri, PDF e immagini|*.docx;*.odt;*.txt;*.rtf;*.htm;*.html;*.xhtml;*.epub;*.pdf;*.png;*.jpg;*.jpeg;*.tif;*.tiff;*.bmp;*.gif" +
        "|Documento Word (*.docx)|*.docx|Documento OpenDocument (*.odt)|*.odt|Testo semplice (*.txt)|*.txt|Rich Text (*.rtf)|*.rtf" +
        "|Pagina web (*.html)|*.htm;*.html;*.xhtml|Libro EPUB (*.epub)|*.epub|PDF (*.pdf)|*.pdf" +
        "|Immagini scansionate|*.png;*.jpg;*.jpeg;*.tif;*.tiff;*.bmp;*.gif|Tutti i file (*.*)|*.*";

    /// <summary>L'ordine corrisponde ai valori di <see cref="FormatoFile"/> (FilterIndex = valore + 1).</summary>
    public const string FiltroSalva =
        "Documento Word (*.docx)|*.docx|Testo semplice (*.txt)|*.txt|Rich Text (*.rtf)|*.rtf|Pagina web (*.html)|*.html|Libro EPUB (*.epub)|*.epub" +
        "|Documento OpenDocument (*.odt)|*.odt";

    /// <summary>Formato dedotto dall'estensione; i file sconosciuti si aprono come testo semplice.</summary>
    public static FormatoFile DaPercorso(string percorso) =>
        Path.GetExtension(percorso).ToLowerInvariant() switch
        {
            ".docx" => FormatoFile.Docx,
            ".odt" => FormatoFile.Odt,
            ".rtf" => FormatoFile.Rtf,
            ".htm" or ".html" or ".xhtml" => FormatoFile.Html,
            ".epub" => FormatoFile.Epub,
            _ => FormatoFile.Testo,
        };
}
