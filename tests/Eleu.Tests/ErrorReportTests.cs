namespace Eleu.Tests;

/// <summary>The report of an error, to send from far away: what happened, inside what, and where in Eleu.</summary>
public class ErrorReportTests
{
    private static Exception Thrown(Exception e)
    {
        try
        {
            throw e;
        }
        catch (Exception caught)
        {
            return caught;
        }
    }

    [Fact]
    public void Text_KindMessageStackAndCause()
    {
        var error = Thrown(new InvalidOperationException("stampa non riuscita", Thrown(new ArgumentException("foglio sbagliato"))));
        var text = ErrorReport.Text(error, "Stampa Braille su Juliet 120", new DateTime(2026, 11, 18, 10, 30, 0));
        Assert.Contains("2026-11-18 10:30:00", text);
        Assert.Contains("Operazione: Stampa Braille su Juliet 120", text);
        Assert.Contains("System.InvalidOperationException", text);
        Assert.Contains("stampa non riuscita", text);
        Assert.Contains("Causato da:", text);
        Assert.Contains("System.ArgumentException", text);
        Assert.Contains("foglio sbagliato", text);
        Assert.Contains(nameof(Thrown), text); // the stack
        Assert.Contains("Windows:", text);
    }

    [Fact]
    public void Text_WithoutStackOrOperation()
    {
        var text = ErrorReport.Text(new IOException("porta occupata"), null, DateTime.Now);
        Assert.DoesNotContain("Operazione:", text);
        Assert.Contains("System.IO.IOException", text);
        Assert.Contains("porta occupata", text);
    }

    [Fact]
    public void Save_WritesTheFile_AndNeverFails()
    {
        var folder = Path.Combine(Path.GetTempPath(), "EleuErrorReport" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(folder, "errore.txt");
        try
        {
            ErrorReport.Save(Thrown(new NullReferenceException("niente")), "prova", path);
            Assert.Contains("NullReferenceException", File.ReadAllText(path));
            // A path that cannot be written: no error from the report itself.
            ErrorReport.Save(new IOException(), null, Path.Combine(path, "dentro un file", "errore.txt"));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
