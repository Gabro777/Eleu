using Eleu.UI;

namespace Eleu;

// Shortcuts the user can change (Strumenti > Cambia le scorciatoie da tastiera). The defaults are the ones written in the menus; the
// changes are saved by command, as its menu path, and applied to every open window.
public sealed partial class MainWindow
{
    /// <summary>A menu command with its default keys (and how the menu writes them).</summary>
    private sealed record MenuCommand(ToolStripMenuItem Item, Keys Default, string? DefaultText);

    private readonly Dictionary<string, MenuCommand> _commands = [];

    /// <summary>Combinations Eleu handles by itself, outside the menus: they cannot be given to a command.</summary>
    public static readonly IReadOnlyDictionary<Keys, string> ReservedKeys = new Dictionary<Keys, string>
    {
        [Keys.Control | Keys.F] = "apre Trova",
        [Keys.Control | Keys.H] = "apre Sostituisci",
        [Keys.Control | Keys.G] = "apre Vai a",
        [Keys.Control | Keys.D] = "apre la finestra Carattere",
        [Keys.Control | Keys.Tab] = "porta alla barra dei pulsanti",
        [Keys.Shift | Keys.F10] = "apre il menu contestuale",
    };

    /// <summary>The menu path of an item, as the command's name: "Formato > Carattere > Grassetto".</summary>
    private static string CommandPath(IEnumerable<ToolStripMenuItem> path) =>
        string.Join(" > ", path.Select(i => i.Text!.Replace("&", "").Replace("...", "").Trim()));

    /// <summary>Records every command of the menus with its default keys, then applies the user's changes.</summary>
    private void RegisterCommands()
    {
        void Walk(ToolStripItemCollection items, List<ToolStripMenuItem> path)
        {
            foreach (var item in items.OfType<ToolStripMenuItem>())
            {
                path.Add(item);
                if (item.DropDownItems.Count > 0)
                    Walk(item.DropDownItems, path);
                else
                    _commands.TryAdd(CommandPath(path), new MenuCommand(item, item.ShortcutKeys, item.ShortcutKeyDisplayString));
                path.RemoveAt(path.Count - 1);
            }
        }
        Walk(_menu.Items, []);
        ApplyShortcuts();
    }

    /// <summary>The keys of every command: the user's change if there is one, otherwise the default.</summary>
    private void ApplyShortcuts()
    {
        foreach (var (path, command) in _commands)
        {
            if (_settings.Shortcuts.TryGetValue(path, out int saved) && (saved == 0 || KeyNames.IsValidShortcut((Keys)saved)))
            {
                command.Item.ShortcutKeys = (Keys)saved;
                command.Item.ShortcutKeyDisplayString = saved == 0 ? null : KeyNames.Italian((Keys)saved);
            }
            else
            {
                command.Item.ShortcutKeys = command.Default;
                command.Item.ShortcutKeyDisplayString = command.DefaultText;
            }
        }
        UpdateBarShortcuts();
    }

    /// <summary>The buttons of the bar say the shortcut of their command (in the tooltip and to the screen reader).</summary>
    private void UpdateBarShortcuts()
    {
        var byName = _commands.Values
            .GroupBy(c => Plain(c.Item.Text))
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        foreach (var button in _bar.Items.OfType<ToolStripButton>())
        {
            if (!byName.TryGetValue(Plain(button.Text), out var command))
                continue;
            var keys = command.Item.ShortcutKeys;
            var text = keys == Keys.None ? null : command.Item.ShortcutKeyDisplayString ?? KeyNames.Italian(keys);
            button.AccessibleDescription = text;
            button.ToolTipText = text is null ? button.Text : $"{button.Text} ({text})";
        }

        static string Plain(string? text) => (text ?? "").Replace("&", "").Replace("...", "").Trim();
    }

    /// <summary>Strumenti > Cambia le scorciatoie da tastiera.</summary>
    private void CustomizeShortcuts()
    {
        var commands = _commands.Select(c => new ShortcutCommand(c.Key, c.Value.Default, c.Value.Item.ShortcutKeys)).ToList();
        IReadOnlyDictionary<string, Keys> result;
        using (var window = new ShortcutsDialog(commands, ReservedKeys))
        {
            if (window.ShowDialog(this) != DialogResult.OK)
                return;
            result = window.Result;
        }

        // Only what differs from the default is saved: the defaults can change in later versions.
        _settings.Shortcuts = result
            .Where(r => _commands.TryGetValue(r.Key, out var c) && r.Value != c.Default)
            .ToDictionary(r => r.Key, r => (int)r.Value);
        _settings.Save();
        foreach (var window in EleuContext.Windows.Append(this).Distinct())
            window.ApplyShortcuts();
        int changed = _settings.Shortcuts.Count;
        AnnounceShortly(changed == 0 ? "Scorciatoie predefinite" : changed == 1 ? "1 scorciatoia cambiata" : $"{changed} scorciatoie cambiate");
    }
}
