namespace Eleu;

static class Program
{
    [STAThread]
    static void Main(string[] arguments)
    {
        ApplicationConfiguration.Initialize();
        // An unexpected error says what happened in Italian and keeps a report the user can send, instead of the
        // Windows Forms window with "Continua / Esci"; one on another thread can only be written down before Eleu closes.
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => ErrorReport.ShowUnexpected(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception error)
                ErrorReport.Save(error, "Errore fuori dalla finestra principale");
        };
        Application.Run(new EleuContext(arguments.FirstOrDefault()));
    }
}

/// <summary>
/// Eleu stays open as long as there is at least one window: each document has its own, as in Word, so Alt+Tab and
/// screen readers treat them as normal windows. Settings are the same for all of them.
/// </summary>
public sealed class EleuContext : ApplicationContext
{
    private static EleuContext? _current;
    private readonly List<MainWindow> _windows = [];

    public EleuContext(string? file)
    {
        _current = this;
        // Opened without a file: if the Options ask for it, the last document used is resumed.
        var settings = Settings.Shared;
        if (file is null && settings.ReopenLast && settings.RecentFiles.FirstOrDefault() is string last && File.Exists(last))
            file = last;
        Add(new MainWindow(file));
    }

    /// <summary>The open windows, in the order they were opened.</summary>
    public static IReadOnlyList<MainWindow> Windows => _current?._windows ?? [];

    /// <summary>Eleu is running as a program (and not, for example, inside the automated tests).</summary>
    public static bool Active => _current is not null;

    /// <summary>Opens another window, empty or with a file.</summary>
    public static MainWindow Open(string? file = null, bool guide = false)
    {
        var window = new MainWindow(file, guide);
        if (_current is null)
            window.Show(); // in tests, without a context
        else
            _current.Add(window);
        return window;
    }

    private void Add(MainWindow window)
    {
        _windows.Add(window);
        window.FormClosed += (_, _) =>
        {
            _windows.Remove(window);
            if (_windows.Count == 0)
                ExitThread();
        };
        window.Show();
    }
}
