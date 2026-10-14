namespace Eleu;

// Several open documents (Finestre menu): each document has its own window; from here you switch between them.
public sealed partial class MainWindow
{
    /// <summary>Brings this window to the front and says where you are.</summary>
    public void ActivateAndAnnounce(string? prefix = null)
    {
        if (WindowState == FormWindowState.Minimized)
            WindowState = _settings.Maximized ? FormWindowState.Maximized : FormWindowState.Normal;
        Activate();
        _text.Focus();
        var windows = EleuContext.Windows;
        int number = windows.ToList().IndexOf(this) + 1;
        var where = windows.Count > 1 ? $"{DocumentName}, finestra {number} di {windows.Count}" : DocumentName;
        AnnounceShortly(prefix is null ? where : $"{prefix}. {where}");
    }

    private void NearbyWindow(int step)
    {
        var windows = EleuContext.Windows;
        if (windows.Count < 2)
        {
            _announcer.Announce("C'è una sola finestra aperta");
            return;
        }
        int here = windows.ToList().IndexOf(this);
        windows[(here + step + windows.Count) % windows.Count].ActivateAndAnnounce();
    }

    /// <summary>The Finestre menu is filled when it opens: the fixed items and one numbered document per line.</summary>
    private void UpdateWindowsMenu(ToolStripMenuItem menu, ToolStripItem[] fixedOnes)
    {
        menu.DropDownItems.Clear();
        menu.DropDownItems.AddRange(fixedOnes);
        var windows = EleuContext.Windows;
        if (windows.Count == 0)
            return;
        menu.DropDownItems.Add(new ToolStripSeparator());
        for (int i = 0; i < windows.Count; i++)
        {
            var window = windows[i];
            var item = new ToolStripMenuItem($"{(i < 9 ? $"&{i + 1} " : "")}{window.DocumentName}{(window._text.Modified ? " (modificato)" : "")}",
                null, (_, _) => BeginInvoke(() => window.ActivateAndAnnounce()))
            {
                Checked = window == this,
            };
            menu.DropDownItems.Add(item);
        }
    }

    private ToolStripMenuItem CreateWindowsMenu()
    {
        ToolStripItem[] fixedOnes =
        [
            MenuItem("&Nuova finestra", Keys.None, () => EleuContext.Open()),
            MenuItem("Finestra &successiva", Keys.Control | Keys.Alt | Keys.F6, () => NearbyWindow(+1), "Ctrl+Alt+F6"),
            MenuItem("Finestra &precedente", Keys.Control | Keys.Alt | Keys.Shift | Keys.F6, () => NearbyWindow(-1), "Ctrl+Alt+Maiusc+F6"),
            MenuItem("&Chiudi documento", Keys.Control | Keys.F4, Close, "Ctrl+F4"),
        ];
        var menu = new ToolStripMenuItem("Fi&nestre");
        menu.DropDownItems.AddRange(fixedOnes);
        menu.DropDownOpening += (_, _) => UpdateWindowsMenu(menu, fixedOnes);
        return menu;
    }
}
