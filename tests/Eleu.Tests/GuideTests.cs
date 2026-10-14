using System.Text.RegularExpressions;
using Eleu.Documents;
using static Eleu.Tests.Support;

namespace Eleu.Tests;

public class GuideTests
{
    private static string GuidePath()
    {
        // From the test folder go up to the project.
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            var guide = Path.Combine(folder.FullName, "src", "Eleu", "Guida", "Guida.html");
            if (File.Exists(guide))
                return guide;
        }
        throw new FileNotFoundException("Guida.html non trovata");
    }

    [Fact]
    public void Guide_OpensWithHeadings()
    {
        var guide = HtmlFormat.Read(File.ReadAllText(GuidePath()));
        Assert.Equal("Guida di Eleu", guide.Metadata.Title);
        var headings = guide.Paragraphs.Where(p => p.HeadingLevel > 0).ToList();
        Assert.True(headings.Count >= 10, $"solo {headings.Count} titoli");
        Assert.Equal(1, headings[0].HeadingLevel);
    }

    /// <summary>Every shortcut with Ctrl, Alt or an F key written in the guide really exists in Eleu.</summary>
    [Fact]
    public void Guide_CitedShortcutsExist()
    {
        var text = string.Join("\n", HtmlFormat.Read(File.ReadAllText(GuidePath())).Paragraphs.Select(p => p.Text));
        var cited = Regex.Matches(text, @"\b(?:(?:Ctrl|Alt|Maiusc)\+)+(?:F\d+|[A-Z0-9]|Invio|Tab)\b|\bF\d{1,2}\b")
            .Select(m => m.Value).Distinct().ToList();
        Assert.NotEmpty(cited);

        var footnotes = new HashSet<string>();
        RunSta(() =>
        {
            using var window = new MainWindow();
            void Collect(ToolStripItemCollection items)
            {
                foreach (var item in items.OfType<ToolStripMenuItem>())
                {
                    if (item.ShortcutKeys != Keys.None)
                        footnotes.Add(Name(item.ShortcutKeys));
                    if (item.ShortcutKeyDisplayString is string shown)
                        foreach (var part in shown.Split(" oppure "))
                            footnotes.Add(part.Trim());
                    Collect(item.DropDownItems);
                }
            }
            Collect(window.MainMenuStrip!.Items);
        });
        // Keys outside the menus: the synonyms (Ctrl+F, Ctrl+H, Ctrl+G, Ctrl+D), Ctrl+Tab for the bar, Shift+F10 for the context menu.
        footnotes.UnionWith(["Ctrl+F", "Ctrl+H", "Ctrl+G", "Ctrl+D", "Ctrl+Tab", "Alt+Tab", "Maiusc+F10", "Alt+F", "Alt+M", "Alt+V",
            "Alt+I", "Alt+O", "Alt+S", "Alt+C", "Alt+N", "Alt+A"]);
        var missing = cited.Where(c => !footnotes.Contains(c)).ToList();
        Assert.True(missing.Count == 0, "Nella guida ma non in Eleu: " + string.Join(", ", missing));

        static string Name(Keys keys)
        {
            var parts = new List<string>();
            if (keys.HasFlag(Keys.Control)) parts.Add("Ctrl");
            if (keys.HasFlag(Keys.Alt)) parts.Add("Alt");
            if (keys.HasFlag(Keys.Shift)) parts.Add("Maiusc");
            var key = keys & Keys.KeyCode;
            parts.Add(key switch
            {
                Keys.Enter => "Invio",
                >= Keys.D0 and <= Keys.D9 => ((int)(key - Keys.D0)).ToString(),
                _ => key.ToString(),
            });
            return string.Join("+", parts);
        }
    }
}
