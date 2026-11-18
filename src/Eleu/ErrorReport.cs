using System.Globalization;
using System.Text;

namespace Eleu;

/// <summary>
/// What went wrong, written to a text file the user can send: the kind of error, its message and where in Eleu it
/// happened (the stack), for every error and the ones inside it. Used by the handler of unexpected errors and by
/// printing, so a problem seen only on someone else's computer can be understood from far away.
/// </summary>
public static class ErrorReport
{
    /// <summary>The file with the last error.</summary>
    public static readonly string FilePath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Eleu", "errore.txt");

    /// <summary>Its name once copied to the desktop.</summary>
    public const string DesktopName = "Eleu - resoconto errore.txt";

    /// <summary>The report: Eleu and Windows versions, when, what Eleu was doing, then each error with its stack.</summary>
    /// <param name="doing">What Eleu was doing, in Italian (for example "Stampa Braille su Juliet 120"); null = not known.</param>
    public static string Text(Exception error, string? doing, DateTime when)
    {
        var text = new StringBuilder();
        text.AppendLine(CultureInfo.InvariantCulture, $"Errore in Eleu {typeof(ErrorReport).Assembly.GetName().Version?.ToString(3)} - {when:yyyy-MM-dd HH:mm:ss}");
        text.AppendLine(CultureInfo.InvariantCulture, $"Windows: {Environment.OSVersion.VersionString}, {(Environment.Is64BitProcess ? "64" : "32")} bit");
        if (doing is not null)
            text.AppendLine(CultureInfo.InvariantCulture, $"Operazione: {doing}");
        int depth = 0;
        for (var e = error; e is not null && depth < 10; e = e.InnerException, depth++)
        {
            text.AppendLine();
            text.AppendLine(depth == 0 ? "Errore:" : "Causato da:");
            text.AppendLine(CultureInfo.InvariantCulture, $"{e.GetType().FullName} (0x{e.HResult:X8}): {e.Message}");
            if (e.StackTrace is string stack)
                text.AppendLine(stack);
            if (e is AggregateException { InnerExceptions.Count: > 1 } many)
                foreach (var other in many.InnerExceptions.Skip(1))
                    text.AppendLine(CultureInfo.InvariantCulture, $"Anche: {other.GetType().FullName}: {other.Message}");
        }
        return text.ToString();
    }

    /// <summary>Writes the report; a report that cannot be written must not cause another error.</summary>
    public static void Save(Exception error, string? doing = null, string? path = null)
    {
        path ??= FilePath;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, Text(error, doing, DateTime.Now), Encoding.UTF8);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// For an error nobody expected (Eleu's handler for the whole program): saves the report and says what happened in
    /// plain words, offering to put the report on the desktop. Eleu stays open.
    /// </summary>
    public static void ShowUnexpected(Exception error)
    {
        // An error that repeats while the message is open (in drawing, say) is written down, not shown again.
        if (_showing)
        {
            Save(error);
            return;
        }
        _showing = true;
        try
        {
            Save(error);
            var owner = Form.ActiveForm;
            var answer = MessageBox.Show(owner,
                "Si è verificato un errore imprevisto: l'ultima operazione non è stata completata.\n\n" +
                $"{error.Message}\n\n" +
                "Eleu resta aperto, ma per sicurezza salva il documento.\n\n" +
                $"Vuoi mettere sul desktop il resoconto dell'errore («{DesktopName}»), da mandare a chi ti aiuta?",
                "Eleu", MessageBoxButtons.YesNo, MessageBoxIcon.Error, MessageBoxDefaultButton.Button1);
            if (answer == DialogResult.Yes)
            {
                CopyToDesktop();
                MessageBox.Show(owner, $"Il resoconto dell'errore è sul desktop: «{DesktopName}».", "Eleu",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // The handler of errors must never stop Eleu itself.
        }
        finally
        {
            _showing = false;
        }
    }

    private static bool _showing;

    /// <summary>Copies the last report to the desktop, if there is one; false if there is none.</summary>
    public static bool CopyToDesktop()
    {
        if (!File.Exists(FilePath))
            return false;
        File.Copy(FilePath, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), DesktopName), overwrite: true);
        return true;
    }
}
