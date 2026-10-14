namespace Eleu.Tests;

public sealed class SettingsTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "eleu-test-" + Guid.NewGuid());

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, true);
    }

    [Fact]
    public void RecentFiles_OnTopNoDuplicatesAtMostFive()
    {
        var settings = new Settings();
        for (int i = 1; i <= 7; i++)
            settings.AddRecent($@"C:\doc{i}.docx");
        settings.AddRecent(@"c:\DOC5.docx");

        Assert.Equal([@"c:\DOC5.docx", @"C:\doc7.docx", @"C:\doc6.docx", @"C:\doc4.docx", @"C:\doc3.docx"], settings.RecentFiles);
    }

    [Fact]
    public void SaveAndLoad()
    {
        var path = Path.Combine(_folder, "impostazioni.json");
        var settings = new Settings { Zoom = 1.5f };
        settings.AddRecent(@"C:\lettera.docx");
        settings.Save(path);

        var loaded = Settings.Load(path);
        Assert.Equal(1.5f, loaded.Zoom);
        Assert.Equal([@"C:\lettera.docx"], loaded.RecentFiles);
    }

    [Fact]
    public void MissingOrCorruptFile_GivesDefaults()
    {
        Assert.Equal(1f, Settings.Load(Path.Combine(_folder, "manca.json")).Zoom);

        Directory.CreateDirectory(_folder);
        var corrupt = Path.Combine(_folder, "corrotto.json");
        File.WriteAllText(corrupt, "{ non è json");
        Assert.Equal(1f, Settings.Load(corrupt).Zoom);
    }

    [Fact]
    public void Recovery_SaveReadDelete()
    {
        using var recovery = new Recovery(_folder);
        Assert.False(recovery.Exists);

        recovery.Save(@"{\rtf1 ciao}", @"C:\lettera.docx");
        Assert.True(recovery.Exists);
        Assert.Equal((@"{\rtf1 ciao}", @"C:\lettera.docx"), recovery.Read());

        recovery.Delete();
        Assert.False(recovery.Exists);
    }

    [Fact]
    public void Recovery_OnlyFromClosedWindows()
    {
        var opened = new Recovery(_folder);
        opened.Save("testo di prova", null);
        // A second Eleu must not offer the document of a window that is still open.
        Assert.Null(Recovery.Abandoned(_folder));

        opened.Dispose(); // as if Eleu closed badly: the copy stays, the lock does not
        using var abandoned = Recovery.Abandoned(_folder);
        Assert.NotNull(abandoned);
        Assert.Equal(("testo di prova", (string?)null), abandoned.Read());

        abandoned.Delete();
        Assert.Null(Recovery.Abandoned(_folder));
    }
}
