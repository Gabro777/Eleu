using static Eleu.Tests.Support;

namespace Eleu.Tests;

public class MenuTests
{
    /// <summary>
    /// Keyboard users open the menus with Alt+letter: two items with the same letter in the same menu
    /// cannot be reached directly, and two commands with the same shortcut fight over the key.
    /// </summary>
    [Fact]
    public void Menu_NoDuplicateAccessKeysOrShortcuts()
    {
        var problems = new List<string>();
        RunSta(() =>
        {
            using var window = new MainWindow();
            var shortcuts = new Dictionary<Keys, string>();
            Check(window.MainMenuStrip!.Items, "barra dei menu");

            void Check(ToolStripItemCollection items, string menu)
            {
                var letters = new Dictionary<char, string>();
                foreach (var item in items.OfType<ToolStripMenuItem>())
                {
                    var text = item.Text ?? "";
                    int e = text.IndexOf('&');
                    if (e >= 0 && e + 1 < text.Length)
                    {
                        char letter = char.ToLowerInvariant(text[e + 1]);
                        if (!letters.TryAdd(letter, text))
                            problems.Add($"{menu}: la lettera '{letter}' è sia in \"{letters[letter]}\" sia in \"{text}\"");
                    }
                    else if (item.Enabled)
                    {
                        problems.Add($"{menu}: \"{text}\" non ha una lettera di scelta rapida");
                    }

                    if (item.ShortcutKeys != Keys.None && !shortcuts.TryAdd(item.ShortcutKeys, text))
                        problems.Add($"la scorciatoia {item.ShortcutKeys} è sia in \"{shortcuts[item.ShortcutKeys]}\" sia in \"{text}\"");

                    Check(item.DropDownItems, text.Replace("&", ""));
                }
            }
        });
        Assert.Empty(problems);
    }

    /// <summary>The document has the context menu (Applications key), with the basic commands and all different letters.</summary>
    [Fact]
    public void ContextMenu_BasicCommandsWithoutDuplicateAccessKeys()
    {
        List<string> items = [];
        RunSta(() =>
        {
            using var window = new MainWindow();
            var documents = window.Controls.OfType<Editor.DocumentTextBox>().Single();
            var menu = documents.ContextMenuStrip!;
            menu.Show(Point.Empty);
            items = [.. menu.Items.OfType<ToolStripMenuItem>().Select(v => v.Text!)];
            menu.Close();
        });
        Assert.Contains("&Copia", items);
        Assert.Contains("&Incolla", items);
        var letters = items.Where(v => v.Contains('&')).Select(v => char.ToLowerInvariant(v[v.IndexOf('&') + 1])).ToList();
        Assert.Equal(letters.Count, letters.Distinct().Count());
    }

    [Fact]
    public void Dialogs_NoDuplicateAccessKeys()
    {
        var problems = new List<string>();
        RunSta(() =>
        {
            foreach (var window in AllWindows())
            {
                var letters = new Dictionary<char, string>();
                foreach (var control in All(window))
                {
                    var text = control.Text ?? "";
                    int e = text.IndexOf('&');
                    if (e < 0 || e + 1 >= text.Length || control is ComboBox or TextBox or NumericUpDown)
                        continue;
                    char letter = char.ToLowerInvariant(text[e + 1]);
                    if (!letters.TryAdd(letter, text))
                        problems.Add($"{window.Text}: la lettera '{letter}' è sia in \"{letters[letter]}\" sia in \"{text}\"");
                }
                window.Dispose();
            }
        });
        Assert.Empty(problems);
    }

    /// <summary>
    /// The screen reader reads a name for every control reachable with Tab, and every window closes with Esc
    /// (a Cancel or Close button as CancelButton).
    /// </summary>
    [Fact]
    public void Dialogs_EveryControlHasANameAndEscCloses()
    {
        var problems = new List<string>();
        RunSta(() =>
        {
            foreach (var window in AllWindows())
            {
                window.ShowInTaskbar = false;
                window.StartPosition = FormStartPosition.Manual;
                window.Location = new Point(-5000, -5000);
                window.Show();
                Application.DoEvents();
                foreach (var control in All(window).Where(c => c.Visible && c.CanSelect && c.TabStop))
                {
                    var name = control.AccessibilityObject.Name;
                    if (string.IsNullOrWhiteSpace(name))
                        problems.Add($"{window.Text}: {control.GetType().Name} \"{control.Text}\" senza nome");
                }
                if (window.CancelButton is null)
                    problems.Add($"{window.Text}: Esc non la chiude");
                window.Close();
                window.Dispose();
            }
        });
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    private static Form[] AllWindows() =>
    [
        new UI.CharacterDialog(new Editor.CharacterFormat(), ["Arial", "Segoe UI"]),
        new UI.ParagraphDialog(new Editor.ParagraphFormat()),
        new UI.FindDialog(true, "", "", new Documents.SearchOptions()),
        new UI.VoiceDialog([], null, new Speech.VoiceParameters()),
        new UI.BrailleDialog([], null, new Braille.PageFormat(), _ => 1),
        new UI.PageDialog(new Documents.PageLayout()),
        new UI.PrintOptionsDialog(["Stampante"], null, 3),
        new UI.GoToDialog(0, 1),
        new HeadingLevelDialog(1),
        new NameDialog("Aggiungi segnalibro", "&Nome:", ""),
        new FootnoteDialog("Aggiungi nota", ""),
        new TableDialog(),
        new ColumnWidthDialog(1, 4),
        new ImageDialog("Aggiungi immagine", Documents.Images.FromData(ImagesTests.Bitmap(System.Drawing.Imaging.ImageFormat.Png), "")),
        new UI.SpellingDialog(),
        new UI.DictionaryDialog(["Eleu"]),
        new UI.CleanupDialog(new Proofing.CleanupOptions(), true),
        new UI.ReplacementsDialog([new("a", "b")], true),
        new UI.PronunciationsDialog([new("NVDA", "enne vu di a")]),
        new UI.AboutDialog("1.0.0"),
        new PropertiesDialog(new Documents.Metadata("Titolo", "Autore"), null, "10 parole"),
        new UI.ListDialog("Segnalibri", ["Nome", "Riga"], [new(["inizio", "1"], 0)]),
        new UI.TextDialog("Scorciatoie da tastiera", "Ctrl+N: nuovo"),
        new UI.OptionsDialog(new Settings()),
        new UI.SymbolsDialog(),
        new UI.ScanDialog([new("a", "Scanner A", true, false), new("b", "Scanner B", true, true)]),
        new UI.UpdateDialog(Updates.FromJson("""{"tag_name":"v9.0.0","body":"- novità","assets":[{"name":"a.msi","browser_download_url":"x"}]}""")!, new Version(1, 0, 0), installable: true),
        new UI.TactileGraphicsDialog(new System.Drawing.Bitmap(50, 50), "Prova", ["Stampante"], null, 32, 27, _ => null),
    ];

    /// <summary>
    /// Bold with no selection changes neither text nor selection: the Bold button must still light up right away,
    /// not only when the next character is typed.
    /// </summary>
    [Fact]
    public void Bold_WithoutSelection_ButtonLitRightAway()
    {
        bool before = true, after = false;
        RunSta(() =>
        {
            using var window = new MainWindow();
            window.Show();
            var bar = All(window).OfType<UI.ButtonBar>().Single();
            bar.Visible = true;
            var text = All(window).OfType<Editor.DocumentTextBox>().Single();
            text.Focus();
            text.SelectedText = "Ciao ";
            Application.DoEvents();
            var button = bar.Items.OfType<ToolStripButton>().Single(b => b.Text == "Grassetto");
            before = button.Checked;

            var item = Items(window.MainMenuStrip!.Items).Single(i => i.Text == "&Grassetto");
            item.PerformClick();
            for (int i = 0; i < 5; i++)
                Application.DoEvents();
            after = button.Checked;
        });
        Assert.False(before);
        Assert.True(after);

        static IEnumerable<ToolStripMenuItem> Items(ToolStripItemCollection items) =>
            items.OfType<ToolStripMenuItem>().SelectMany(i => Items(i.DropDownItems).Prepend(i));
    }

    private static IEnumerable<Control> All(Control root) =>
        root.Controls.Cast<Control>().SelectMany(c => All(c).Prepend(c));
}
