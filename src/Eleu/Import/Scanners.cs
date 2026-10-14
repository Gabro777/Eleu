using Windows.Devices.Enumeration;
using Windows.Devices.Scanners;
using Windows.Storage;

namespace Eleu.Import;

/// <summary>A scanner found on this computer, with the sources it can scan from.</summary>
public sealed record ScannerInfo(string Id, string Name, bool HasFlatbed, bool HasFeeder);

/// <summary>Where the pages come from: the glass, one page at a time, or the document feeder, all pages at once.</summary>
public enum ScanSource
{
    Flatbed,
    Feeder,
}

/// <summary>Finds scanners and scans pages into image files.</summary>
public interface IScanners
{
    Task<IReadOnlyList<ScannerInfo>> FindAsync(CancellationToken cancel);

    /// <summary>
    /// Scans into <paramref name="folder"/> and returns the image files, in page order: one page from the glass,
    /// every page in the feeder from the feeder.
    /// </summary>
    Task<IReadOnlyList<string>> ScanAsync(ScannerInfo scanner, ScanSource source, string folder, CancellationToken cancel);
}

/// <summary>
/// The scanners Windows knows (WIA drivers, USB or network), driven without the Windows dialog, which is hard to use
/// with a screen reader. Settings suited to OCR: 300 dpi in grayscale, when the scanner allows them.
/// </summary>
public sealed class WindowsScanners : IScanners
{
    private const uint Dpi = 300;

    public async Task<IReadOnlyList<ScannerInfo>> FindAsync(CancellationToken cancel)
    {
        var devices = await DeviceInformation.FindAllAsync(ImageScanner.GetDeviceSelector()).AsTask(cancel);
        var found = new List<ScannerInfo>();
        foreach (var device in devices)
        {
            try
            {
                var scanner = await ImageScanner.FromIdAsync(device.Id).AsTask(cancel);
                if (scanner is null)
                    continue;
                bool flatbed = scanner.IsScanSourceSupported(ImageScannerScanSource.Flatbed);
                bool feeder = scanner.IsScanSourceSupported(ImageScannerScanSource.Feeder);
                if (flatbed || feeder)
                    found.Add(new ScannerInfo(device.Id, device.Name, flatbed, feeder));
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // A device that is switched off or busy is simply not offered.
            }
        }
        return found;
    }

    public async Task<IReadOnlyList<string>> ScanAsync(ScannerInfo info, ScanSource source, string folder, CancellationToken cancel)
    {
        var scanner = await ImageScanner.FromIdAsync(info.Id).AsTask(cancel)
            ?? throw new InvalidOperationException($"Lo scanner {info.Name} non risponde.");
        ImageScannerScanSource kind;
        if (source == ScanSource.Feeder)
        {
            kind = ImageScannerScanSource.Feeder;
            var feeder = scanner.FeederConfiguration;
            Configure(feeder);
            feeder.MaxNumberOfPages = 0; // all the pages in the feeder
            if (feeder.CanScanDuplex)
                feeder.Duplex = false;
        }
        else
        {
            kind = ImageScannerScanSource.Flatbed;
            Configure(scanner.FlatbedConfiguration);
        }

        Directory.CreateDirectory(folder);
        var target = await StorageFolder.GetFolderFromPathAsync(Path.GetFullPath(folder));
        var result = await scanner.ScanFilesToFolderAsync(kind, target).AsTask(cancel);
        return [.. result.ScannedFiles.Select(f => f.Path)];
    }

    private static void Configure(IImageScannerSourceConfiguration configuration)
    {
        if (configuration.IsColorModeSupported(ImageScannerColorMode.Grayscale))
            configuration.ColorMode = ImageScannerColorMode.Grayscale;
        if (configuration.IsFormatSupported(ImageScannerFormat.Png))
            configuration.Format = ImageScannerFormat.Png;
        var min = configuration.MinResolution;
        var max = configuration.MaxResolution;
        float x = Math.Clamp(Dpi, min.DpiX, max.DpiX), y = Math.Clamp(Dpi, min.DpiY, max.DpiY);
        configuration.DesiredResolution = new ImageScannerResolution { DpiX = x, DpiY = y };
    }
}
