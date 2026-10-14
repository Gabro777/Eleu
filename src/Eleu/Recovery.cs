namespace Eleu;

/// <summary>
/// Backup copy of the unsaved document, written periodically.
/// If Eleu closes abnormally, recovery is offered at the next start.
/// It never touches the user's original file.
/// Each Eleu window has its own copy and keeps it "locked" while open: a second Eleu
/// offers to recover only the copies of windows that no longer exist.
/// </summary>
public sealed class Recovery(string folder, string? id = null) : IDisposable
{
    public static string DefaultFolder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Eleu");

    private const string Prefix = "ripristino";

    private readonly string _id = id ?? Guid.NewGuid().ToString("N")[..8];
    private FileStream? _block;

    // The empty id is the single copy of earlier Eleu versions ("ripristino.rtf").
    private string FileName(string extension) =>
        Path.Combine(folder, _id.Length > 0 ? $"{Prefix}-{_id}.{extension}" : $"{Prefix}.{extension}");

    private string RtfFile => FileName("rtf");

    private string PathFile => FileName("txt");

    private string LockFile => FileName("lock");

    public bool Exists => File.Exists(RtfFile);

    /// <summary>The copy left by an Eleu that is no longer open, if there is one.</summary>
    public static Recovery? Abandoned(string folder)
    {
        if (!Directory.Exists(folder))
            return null;
        foreach (var rtf in Directory.EnumerateFiles(folder, $"{Prefix}*.rtf"))
        {
            var name = Path.GetFileNameWithoutExtension(rtf);
            var id = name.Length > Prefix.Length + 1 ? name[(Prefix.Length + 1)..] : "";
            var candidate = new Recovery(folder, id);
            if (!candidate.InUse())
                return candidate;
        }
        return null;
    }

    /// <summary>True if the window that wrote the copy is still open (it holds the lock).</summary>
    private bool InUse()
    {
        try
        {
            using var test = new FileStream(LockFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return false;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    public void Save(string rtf, string? originalPath)
    {
        try
        {
            Directory.CreateDirectory(folder);
            // The lock disappears by itself when the window closes, even if Eleu closes badly.
            _block ??= new FileStream(LockFile, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
            File.WriteAllText(RtfFile, rtf, System.Text.Encoding.Latin1);
            File.WriteAllText(PathFile, originalPath ?? "");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Recovery is a safety net: an error here must not disturb the person writing.
        }
    }

    public (string Rtf, string? OriginalPath) Read()
    {
        var rtf = File.ReadAllText(RtfFile, System.Text.Encoding.Latin1);
        var path = File.Exists(PathFile) ? File.ReadAllText(PathFile).Trim() : "";
        return (rtf, path.Length > 0 ? path : null);
    }

    public void Delete()
    {
        try
        {
            File.Delete(RtfFile);
            File.Delete(PathFile);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    public void Dispose()
    {
        _block?.Dispose();
        _block = null;
    }
}
