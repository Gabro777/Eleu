using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using PdfWindows = Windows.Data.Pdf;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Eleu.Importa;

public sealed record AvanzamentoImportazione(int Pagina, int TotalePagine, bool Ocr);

public sealed record TestoImportato(IReadOnlyList<string> Paragrafi, int Pagine, bool ConOcr);

/// <summary>
/// Apre PDF e immagini. Se il PDF contiene testo lo si legge direttamente; se è una scansione
/// (o è un'immagine) si usa l'OCR di Windows. Il risultato passa sempre dalla pulizia automatica.
/// </summary>
public static class Importatore
{
    public static readonly string[] EstensioniImmagine = [".png", ".jpg", ".jpeg", ".tif", ".tiff", ".bmp", ".gif"];

    /// <summary>Sotto questa media di caratteri per pagina il PDF è considerato una scansione.</summary>
    private const int CaratteriMinimiPerPagina = 40;

    public static bool Importabile(string percorso)
    {
        var estensione = Path.GetExtension(percorso).ToLowerInvariant();
        return estensione == ".pdf" || EstensioniImmagine.Contains(estensione);
    }

    public static async Task<TestoImportato> ImportaAsync(string percorso, IProgress<AvanzamentoImportazione>? avanzamento, CancellationToken annulla)
    {
        if (EstensioniImmagine.Contains(Path.GetExtension(percorso).ToLowerInvariant()))
        {
            avanzamento?.Report(new AvanzamentoImportazione(1, 1, Ocr: true));
            var righe = await OcrImmagineAsync(percorso, annulla);
            return new TestoImportato(PuliziaTesto.Paragrafi([righe]), 1, ConOcr: true);
        }

        var pagine = await Task.Run(() => TestoDelPdf(percorso), annulla);
        if (pagine.Count > 0 && pagine.Sum(p => p.Sum(r => r.Length)) >= pagine.Count * CaratteriMinimiPerPagina)
            return new TestoImportato(PuliziaTesto.Paragrafi(pagine), pagine.Count, ConOcr: false);

        var ocr = await OcrPdfAsync(percorso, avanzamento, annulla);
        return new TestoImportato(PuliziaTesto.Paragrafi(ocr), ocr.Count, ConOcr: true);
    }

    private static List<IReadOnlyList<string>> TestoDelPdf(string percorso)
    {
        using var documento = PdfDocument.Open(percorso);
        return documento.GetPages()
            .Select(p => (IReadOnlyList<string>)ContentOrderTextExtractor.GetText(p, false)
                .Replace("\r\n", "\n").Split('\n'))
            .ToList();
    }

    private static OcrEngine Motore() =>
        OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("it-IT"))
        ?? OcrEngine.TryCreateFromUserProfileLanguages()
        ?? throw new InvalidOperationException(
            "Il riconoscimento del testo (OCR) non è installato. Aggiungilo da Impostazioni di Windows, Ora e lingua, Lingua, Italiano, Opzioni.");

    private static async Task<List<IReadOnlyList<string>>> OcrPdfAsync(string percorso, IProgress<AvanzamentoImportazione>? avanzamento, CancellationToken annulla)
    {
        var motore = Motore();
        var file = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(percorso));
        var pdf = await PdfWindows.PdfDocument.LoadFromFileAsync(file);
        var pagine = new List<IReadOnlyList<string>>();
        for (uint i = 0; i < pdf.PageCount; i++)
        {
            annulla.ThrowIfCancellationRequested();
            avanzamento?.Report(new AvanzamentoImportazione((int)i + 1, (int)pdf.PageCount, Ocr: true));
            using var pagina = pdf.GetPage(i);
            using var immagine = new InMemoryRandomAccessStream();
            // Una pagina larga circa 2000 pixel (300 dpi su A4 è ~2480) dà un buon riconoscimento.
            var larghezza = (uint)Math.Min(2400, OcrEngine.MaxImageDimension);
            await pagina.RenderToStreamAsync(immagine, new PdfWindows.PdfPageRenderOptions { DestinationWidth = larghezza }).AsTask(annulla);
            pagine.Add(await RiconosciAsync(motore, immagine, annulla));
        }
        return pagine;
    }

    private static async Task<IReadOnlyList<string>> OcrImmagineAsync(string percorso, CancellationToken annulla)
    {
        var file = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(percorso));
        using var flusso = await file.OpenAsync(FileAccessMode.Read);
        return await RiconosciAsync(Motore(), flusso, annulla);
    }

    private static async Task<IReadOnlyList<string>> RiconosciAsync(OcrEngine motore, IRandomAccessStream immagine, CancellationToken annulla)
    {
        var decodificatore = await BitmapDecoder.CreateAsync(immagine).AsTask(annulla);
        uint lato = Math.Max(decodificatore.PixelWidth, decodificatore.PixelHeight);
        var trasformazione = new BitmapTransform();
        if (lato > OcrEngine.MaxImageDimension)
        {
            double scala = (double)OcrEngine.MaxImageDimension / lato;
            trasformazione.ScaledWidth = (uint)(decodificatore.PixelWidth * scala);
            trasformazione.ScaledHeight = (uint)(decodificatore.PixelHeight * scala);
        }
        using var bitmap = await decodificatore.GetSoftwareBitmapAsync(
            BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, trasformazione,
            ExifOrientationMode.RespectExifOrientation, ColorManagementMode.DoNotColorManage).AsTask(annulla);
        var risultato = await motore.RecognizeAsync(bitmap).AsTask(annulla);
        return risultato.Lines.Select(l => l.Text).ToList();
    }
}
