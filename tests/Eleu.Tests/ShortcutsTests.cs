using System.Reflection;
using Eleu.UI;
using static Eleu.Tests.Support;

namespace Eleu.Tests;

/// <summary>Shortcuts the user can change: Italian names, valid combinations, and a change seen in menu and bar.</summary>
public class ShortcutsTests
{
    [Theory]
    [InlineData(Keys.Control | Keys.Shift | Keys.F5, "Ctrl+Maiusc+F5")]
    [InlineData(Keys.Control | Keys.Alt | Keys.Up, "Ctrl+Alt+Freccia su")]
    [InlineData(Keys.Control | Keys.Enter, "Ctrl+Invio")]
    [InlineData(Keys.Control | Keys.Alt | Keys.D0, "Ctrl+Alt+0")]
    [InlineData(Keys.F11, "F11")]
    [InlineData(Keys.None, "nessuno")]
    public void Italian_Names(Keys keys, string expected) => Assert.Equal(expected, KeyNames.Italian(keys));

    [Theory]
    [InlineData(Keys.Control | Keys.B, true)]
    [InlineData(Keys.Control | Keys.Alt | Keys.Shift | Keys.F6, true)]
    [InlineData(Keys.F9, true)]
    [InlineData(Keys.Shift | Keys.F5, true)]
    [InlineData(Keys.B, false)]
    [InlineData(Keys.Shift | Keys.B, false)]
    [InlineData(Keys.Enter, false)]
    [InlineData(Keys.Control | Keys.ControlKey, false)]
    public void ValidShortcuts(Keys keys, bool valid) => Assert.Equal(valid, KeyNames.IsValidShortcut(keys));

    [Fact]
    public void Capture_WaitsForTheKey_EscGivesUp()
    {
        Keys pressed = Keys.None;
        DialogResult afterModifier = DialogResult.None, afterKey = DialogResult.None, afterEsc = DialogResult.None;
        RunSta(() =>
        {
            var process = typeof(KeyCaptureDialog).GetMethod("ProcessCmdKey", BindingFlags.NonPublic | BindingFlags.Instance)!;
            using (var capture = new KeyCaptureDialog("Formato > Grassetto"))
            {
                var message = new Message();
                process.Invoke(capture, [message, Keys.Control | Keys.ControlKey]);
                afterModifier = capture.DialogResult;
                process.Invoke(capture, [message, Keys.Control | Keys.Alt | Keys.B]);
                afterKey = capture.DialogResult;
                pressed = capture.Pressed;
            }
            using (var capture = new KeyCaptureDialog("Formato > Grassetto"))
            {
                process.Invoke(capture, [new Message(), Keys.Escape]);
                afterEsc = capture.DialogResult;
            }
        });
        Assert.Equal(DialogResult.None, afterModifier);
        Assert.Equal(DialogResult.OK, afterKey);
        Assert.Equal(Keys.Control | Keys.Alt | Keys.B, pressed);
        Assert.Equal(DialogResult.Cancel, afterEsc);
    }

    [Fact]
    public void ChangedShortcut_InTheMenuAndOnTheBar_TheOthersUnchanged()
    {
        Keys bold = Keys.None, save = Keys.None;
        string? shown = null, onButton = null;
        RunSta(() =>
        {
            var settings = Settings.Shared;
            var saved = settings.Shortcuts;
            try
            {
                string? path = null;
                using (var probe = new MainWindow())
                    path = Paths(probe.MainMenuStrip!.Items, "").First(p => p.Item.Text == "&Grassetto").Path;
                settings.Shortcuts = new() { [path] = (int)(Keys.Control | Keys.Alt | Keys.B) };

                using var window = new MainWindow();
                var items = Paths(window.MainMenuStrip!.Items, "").ToList();
                var item = items.First(p => p.Item.Text == "&Grassetto").Item;
                bold = item.ShortcutKeys;
                shown = item.ShortcutKeyDisplayString;
                save = items.First(p => p.Item.Text == "Sa&lva").Item.ShortcutKeys;
                var bar = AllControls(window).OfType<ButtonBar>().Single();
                onButton = bar.Items.OfType<ToolStripButton>().Single(b => b.Text == "Grassetto").AccessibleDescription;
            }
            finally
            {
                settings.Shortcuts = saved;
            }
        });
        Assert.Equal(Keys.Control | Keys.Alt | Keys.B, bold);
        Assert.Equal("Ctrl+Alt+B", shown);
        Assert.Equal("Ctrl+Alt+B", onButton);
        Assert.Equal(Keys.Control | Keys.S, save);
    }

    /// <summary>Every menu command with its path, written as Eleu names commands.</summary>
    private static IEnumerable<(string Path, ToolStripMenuItem Item)> Paths(ToolStripItemCollection items, string prefix)
    {
        foreach (var item in items.OfType<ToolStripMenuItem>())
        {
            var path = prefix + item.Text!.Replace("&", "").Replace("...", "").Trim();
            if (item.DropDownItems.Count > 0)
                foreach (var inner in Paths(item.DropDownItems, path + " > "))
                    yield return inner;
            else
                yield return (path, item);
        }
    }

    private static IEnumerable<Control> AllControls(Control root) =>
        root.Controls.Cast<Control>().SelectMany(c => AllControls(c).Prepend(c));
}
