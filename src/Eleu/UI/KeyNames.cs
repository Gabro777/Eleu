namespace Eleu.UI;

/// <summary>Key combinations written the Italian way, as in the menus and the guide: "Ctrl+Maiusc+F5", "Ctrl+Alt+Freccia su".</summary>
public static class KeyNames
{
    public static string Italian(Keys keys)
    {
        if (keys == Keys.None)
            return "nessuno";
        var parts = new List<string>();
        if (keys.HasFlag(Keys.Control))
            parts.Add("Ctrl");
        if (keys.HasFlag(Keys.Alt))
            parts.Add("Alt");
        if (keys.HasFlag(Keys.Shift))
            parts.Add("Maiusc");
        parts.Add(Key(keys & Keys.KeyCode));
        return string.Join("+", parts);
    }

    private static string Key(Keys key) => key switch
    {
        >= Keys.A and <= Keys.Z => key.ToString(),
        >= Keys.D0 and <= Keys.D9 => ((int)(key - Keys.D0)).ToString(),
        >= Keys.NumPad0 and <= Keys.NumPad9 => $"Tastierino {(int)(key - Keys.NumPad0)}",
        >= Keys.F1 and <= Keys.F24 => key.ToString(),
        Keys.Enter => "Invio",
        Keys.Escape => "Esc",
        Keys.Space => "Spazio",
        Keys.Tab => "Tab",
        Keys.Back => "Backspace",
        Keys.Delete => "Canc",
        Keys.Insert => "Ins",
        Keys.Home => "Inizio",
        Keys.End => "Fine",
        Keys.PageUp => "Pagina su",
        Keys.PageDown => "Pagina giù",
        Keys.Up => "Freccia su",
        Keys.Down => "Freccia giù",
        Keys.Left => "Freccia sinistra",
        Keys.Right => "Freccia destra",
        Keys.Add or Keys.Oemplus => "più",
        Keys.Subtract or Keys.OemMinus => "meno",
        Keys.Multiply => "per",
        Keys.Divide => "diviso",
        Keys.Oemcomma => "virgola",
        Keys.OemPeriod => "punto",
        Keys.Apps => "Applicazioni",
        _ => key.ToString(),
    };

    /// <summary>Whether the combination can be a menu shortcut: Windows Forms refuses, for example, a letter alone.</summary>
    public static bool IsValidShortcut(Keys keys)
    {
        var key = keys & Keys.KeyCode;
        if (key is Keys.None or Keys.ControlKey or Keys.ShiftKey or Keys.Menu)
            return false;
        // Without Ctrl or Alt only the function keys: a letter or Invio would stop typing.
        if (!keys.HasFlag(Keys.Control) && !keys.HasFlag(Keys.Alt) && key is not (>= Keys.F1 and <= Keys.F24))
            return false;
        try
        {
            using var item = new ToolStripMenuItem { ShortcutKeys = keys };
            return true;
        }
        catch (System.ComponentModel.InvalidEnumArgumentException)
        {
            return false;
        }
    }
}
