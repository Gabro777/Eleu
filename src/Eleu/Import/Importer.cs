using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using PdfWindows = Windows.Data.Pdf;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Eleu.Import;

public sealed record ImportProgress(int Page, int TotalPages, bool Ocr);

public sealed record ImportedText(IReadOnlyList<string> Paragraphs, int Pages, bool WithOcr);

/// <summary>
/// Opens PDFs and images. If the PDF contains text it is read directly; if it is a scan
/// (or an image) Windows OCR is used. The result always goes through the automatic cleanup.
/// </summary>
public static class Importer
{
    public static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".tif", ".tiff", ".bmp", ".gif"];

    /// <summary>Below this average of characters per page the PDF is considered a scan.</summary>
    private const int MinCharactersPerPage = 40;

    public static bool IsImportable(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        return extension == ".pdf" || ImageExtensions.Contains(extension);
    }

    public static async Task<ImportedText> ImportAsync(string path, IProgress<ImportProgress>? progress, CancellationToken cancel)
    {
        if (ImageExtensions.Contains(Path.GetExtension(path).ToLowerInvariant()))
        {
            progress?.Report(new ImportProgress(1, 1, Ocr: true));
            var rows = await OcrImageAsync(path, cancel);
            return new ImportedText(TextCleanup.Paragraphs([rows]), 1, WithOcr: true);
        }

        var pages = await Task.Run(() => PdfText(path), cancel);
        if (pages.Count > 0 && pages.Sum(p => p.Sum(r => r.Length)) >= pages.Count * MinCharactersPerPage)
            return new ImportedText(TextCleanup.Paragraphs(pages), pages.Count, WithOcr: false);

        var ocr = await OcrPdfAsync(path, progress, cancel);
        return new ImportedText(TextCleanup.Paragraphs(ocr), ocr.Count, WithOcr: true);
    }

    /// <summary>Scanned pages (image files, in order) recognized with OCR and cleaned up as a single document.</summary>
    public static async Task<ImportedText> RecognizePagesAsync(IReadOnlyList<string> images, IProgress<ImportProgress>? progress, CancellationToken cancel)
    {
        var engine = CreateOcrEngine();
        var pages = new List<IReadOnlyList<string>>();
        for (int i = 0; i < images.Count; i++)
        {
            cancel.ThrowIfCancellationRequested();
            progress?.Report(new ImportProgress(i + 1, images.Count, Ocr: true));
            var file = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(images[i]));
            using var stream = await file.OpenAsync(FileAccessMode.Read);
            pages.Add(await RecognizeAsync(engine, stream, cancel));
        }
        return new ImportedText(TextCleanup.Paragraphs(pages), pages.Count, WithOcr: true);
    }

    private static List<IReadOnlyList<string>> PdfText(string path)
    {
        using var documents = PdfDocument.Open(path);
        return documents.GetPages()
            .Select(p => (IReadOnlyList<string>)ContentOrderTextExtractor.GetText(p, false)
                .Replace("\r\n", "\n").Split('\n'))
            .ToList();
    }

    private static OcrEngine CreateOcrEngine() =>
        OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("it-IT"))
        ?? OcrEngine.TryCreateFromUserProfileLanguages()
        ?? throw new InvalidOperationException(
            "Il riconoscimento del testo (OCR) non è installato. Aggiungilo da Impostazioni di Windows, Ora e lingua, Lingua, Italiano, Opzioni.");

    private static async Task<List<IReadOnlyList<string>>> OcrPdfAsync(string path, IProgress<ImportProgress>? progress, CancellationToken cancel)
    {
        var engine = CreateOcrEngine();
        var file = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(path));
        var pdf = await PdfWindows.PdfDocument.LoadFromFileAsync(file);
        var pages = new List<IReadOnlyList<string>>();
        for (uint i = 0; i < pdf.PageCount; i++)
        {
            cancel.ThrowIfCancellationRequested();
            progress?.Report(new ImportProgress((int)i + 1, (int)pdf.PageCount, Ocr: true));
            using var page = pdf.GetPage(i);
            using var image = new InMemoryRandomAccessStream();
            // A page about 2000 pixels wide (300 dpi on A4 is ~2480) gives good recognition.
            var width = (uint)Math.Min(2400, OcrEngine.MaxImageDimension);
            await page.RenderToStreamAsync(image, new PdfWindows.PdfPageRenderOptions { DestinationWidth = width }).AsTask(cancel);
            pages.Add(await RecognizeAsync(engine, image, cancel));
        }
        return pages;
    }

    private static async Task<IReadOnlyList<string>> OcrImageAsync(string path, CancellationToken cancel)
    {
        var file = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(path));
        using var stream = await file.OpenAsync(FileAccessMode.Read);
        return await RecognizeAsync(CreateOcrEngine(), stream, cancel);
    }

    private static async Task<IReadOnlyList<string>> RecognizeAsync(OcrEngine engine, IRandomAccessStream image, CancellationToken cancel)
    {
        var decoder = await BitmapDecoder.CreateAsync(image).AsTask(cancel);
        uint side = Math.Max(decoder.PixelWidth, decoder.PixelHeight);
        var transform = new BitmapTransform();
        if (side > OcrEngine.MaxImageDimension)
        {
            double scale = (double)OcrEngine.MaxImageDimension / side;
            transform.ScaledWidth = (uint)(decoder.PixelWidth * scale);
            transform.ScaledHeight = (uint)(decoder.PixelHeight * scale);
        }
        using var bitmap = await decoder.GetSoftwareBitmapAsync(
            BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, transform,
            ExifOrientationMode.RespectExifOrientation, ColorManagementMode.DoNotColorManage).AsTask(cancel);
        var result = await engine.RecognizeAsync(bitmap).AsTask(cancel);
        return result.Lines.Select(l => l.Text).ToList();
    }
}
