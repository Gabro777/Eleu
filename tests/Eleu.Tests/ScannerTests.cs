using System.Drawing;
using System.Drawing.Imaging;
using Eleu.Import;

namespace Eleu.Tests;

/// <summary>Scanning with the simulated scanner (a folder of page images): no hardware needed.</summary>
public class ScannerTests : IDisposable
{
    private readonly string _pages = Folder(), _output = Folder();

    public void Dispose()
    {
        Directory.Delete(_pages, recursive: true);
        Directory.Delete(_output, recursive: true);
    }

    private static string Folder()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"eleu-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        return folder;
    }

    /// <summary>A white page with the given lines in black, like a 300 dpi scan of a printed sheet.</summary>
    private void Page(string name, params string[] lines)
    {
        using var image = new Bitmap(1700, 120 + lines.Length * 110);
        using (var g = Graphics.FromImage(image))
        using (var font = new Font("Arial", 40))
        {
            g.Clear(Color.White);
            for (int i = 0; i < lines.Length; i++)
                g.DrawString(lines[i], font, Brushes.Black, 60, 60 + i * 110);
        }
        image.Save(Path.Combine(_pages, name), ImageFormat.Png);
    }

    [Fact]
    public async Task FolderScanner_GlassOnePageAtATime_FeederAllTogether()
    {
        Page("1.png", "uno");
        Page("2.png", "due");
        var scanner = new FolderScanner(_pages);

        var found = Assert.Single(await scanner.FindAsync(CancellationToken.None));
        Assert.True(found.HasFlatbed && found.HasFeeder);

        var first = await scanner.ScanAsync(found, ScanSource.Flatbed, _output, CancellationToken.None);
        var second = await scanner.ScanAsync(found, ScanSource.Flatbed, _output, CancellationToken.None);
        Assert.Single(first);
        Assert.Single(second);
        Assert.Equal(File.ReadAllBytes(Path.Combine(_pages, "1.png")), File.ReadAllBytes(first[0]));
        Assert.Equal(File.ReadAllBytes(Path.Combine(_pages, "2.png")), File.ReadAllBytes(second[0]));

        var all = await scanner.ScanAsync(found, ScanSource.Feeder, _output, CancellationToken.None);
        Assert.Equal(2, all.Count);
        Assert.Equal(4, Directory.GetFiles(_output).Length); // every scan is a new file, nothing overwritten
    }

    [Fact]
    public async Task FolderScanner_MissingFolder_NoScanner_EmptyFolder_Error()
    {
        Assert.Empty(await new FolderScanner(Path.Combine(_pages, "manca")).FindAsync(CancellationToken.None));

        var scanner = new FolderScanner(_pages);
        var found = Assert.Single(await scanner.FindAsync(CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => scanner.ScanAsync(found, ScanSource.Flatbed, _output, CancellationToken.None));
    }

    [Fact]
    public async Task ScannedPages_RecognizedAsOneDocument()
    {
        if (!Windows.Media.Ocr.OcrEngine.AvailableRecognizerLanguages.Any())
            return;
        Page("1.png", "Il gatto dorme sul divano.", "Domani piove in montagna.");
        Page("2.png", "La seconda pagina parla del mare.");
        var scanner = new FolderScanner(_pages);
        var found = Assert.Single(await scanner.FindAsync(CancellationToken.None));
        var images = await scanner.ScanAsync(found, ScanSource.Feeder, _output, CancellationToken.None);

        var result = await Importer.RecognizePagesAsync(images, null, CancellationToken.None);

        Assert.True(result.WithOcr);
        Assert.Equal(2, result.Pages);
        var text = string.Join(" ", result.Paragraphs);
        Assert.Contains("gatto dorme sul divano", text);
        Assert.Contains("seconda pagina parla del mare", text);
        Assert.True(text.IndexOf("divano", StringComparison.Ordinal) < text.IndexOf("mare", StringComparison.Ordinal));
    }
}
