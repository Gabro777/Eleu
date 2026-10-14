using Eleu.Import;

namespace Eleu.Tests;

/// <summary>
/// A simulated scanner: the "pages" are the images in a folder, in name order. From the glass each scan gives the
/// next page (starting again after the last); from the feeder it gives them all.
/// </summary>
internal sealed class FolderScanner(string pages) : IScanners
{
    private int _next;

    private List<string> Pages() =>
        [.. Directory.EnumerateFiles(pages)
            .Where(f => Importer.ImageExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .Order(StringComparer.OrdinalIgnoreCase)];

    public Task<IReadOnlyList<ScannerInfo>> FindAsync(CancellationToken cancel) =>
        Task.FromResult<IReadOnlyList<ScannerInfo>>(Directory.Exists(pages)
            ? [new ScannerInfo("fake", "Scanner simulato", HasFlatbed: true, HasFeeder: true)]
            : []);

    public async Task<IReadOnlyList<string>> ScanAsync(ScannerInfo scanner, ScanSource source, string folder, CancellationToken cancel)
    {
        var all = Pages();
        if (all.Count == 0)
            throw new InvalidOperationException("Nello scanner non c'è nessun foglio.");
        await Task.Delay(300, cancel); // a scan is never instant
        var taken = source == ScanSource.Feeder ? all : [all[_next++ % all.Count]];
        Directory.CreateDirectory(folder);
        var copies = new List<string>();
        foreach (var page in taken)
        {
            var copy = Path.Combine(folder, $"pagina{Directory.GetFiles(folder).Length + 1:000}{Path.GetExtension(page)}");
            File.Copy(page, copy);
            copies.Add(copy);
        }
        return copies;
    }
}
