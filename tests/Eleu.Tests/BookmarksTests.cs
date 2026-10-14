using Eleu.Documents;
using static Eleu.Tests.Support;

namespace Eleu.Tests;

public class BookmarksTests
{
    // In the test RTFs "|" stands for the backslash, easier to read.
    private static string Rtf(string text) => text.Replace('|', '\\');

    [Fact]
    public void ToPlaceholders_BookmarksBecomeCharactersWithTheirName()
    {
        var names = new List<string>();
        var rtf = Bookmarks.ToPlaceholders(Rtf("{|rtf1 Uno {|*|bkmkstart Capitolo_2}{|*|bkmkend Capitolo_2}due {|*|bkmkstart Citt|'e0}fine}"), names);

        Assert.Equal(["Capitolo_2", "Città"], names);
        Assert.DoesNotContain("bkmk", rtf);
        Assert.Contains(Rtf("|u") + (short)Bookmarks.Placeholder(0), rtf);
    }

    [Fact]
    public void ThroughTheTextBox_DotsStayTheSame()
    {
        string? read = null, rewritten = null;
        RunSta(() =>
        {
            using var field = new RichTextBox();
            field.CreateControl();
            var names = new List<string>();
            field.Rtf = Bookmarks.ToPlaceholders(Rtf("{|rtf1 Uno {|*|bkmkstart qui}due|par tre}"), names);
            read = field.Text;
            rewritten = Bookmarks.FromPlaceholders(field.Rtf!, names);
        });

        Assert.Equal("Uno " + Bookmarks.Placeholder(0) + "due\ntre", read);
        Assert.Contains(Rtf("{|*|bkmkstart qui}{|*|bkmkend qui}due"), rewritten);
    }

    [Fact]
    public void Docx_BookmarksKept()
    {
        var content = new DocumentContent();
        content.Bookmarks.AddRange(["Capitolo 2", "Capitolo 2", "1 inizio"]);
        var p = new Paragraph();
        p.Add(new Segment($"Uno {Bookmarks.Placeholder(0)}due{Bookmarks.Placeholder(2)}"));
        content.Paragraphs.Add(p);
        var q = new Paragraph();
        q.Add(new Segment($"{Bookmarks.Placeholder(1)}tre"));
        content.Paragraphs.Add(q);

        using var stream = new MemoryStream();
        DocxFormat.Write(content, stream);
        stream.Position = 0;
        var errors = new DocumentFormat.OpenXml.Validation.OpenXmlValidator()
            .Validate(DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Open(stream, false));
        Assert.Empty(errors);

        stream.Position = 0;
        var reread = DocxFormat.Read(stream);
        // Names become valid for Word (unique, without spaces, starting with a letter) and come back readable.
        Assert.Equal(["Capitolo 2", "S 1 inizio", "Capitolo 2 2"], reread.Bookmarks);
        Assert.Equal($"Uno {Bookmarks.Placeholder(0)}due{Bookmarks.Placeholder(1)}", reread.Paragraphs[0].Text);
        Assert.Equal($"{Bookmarks.Placeholder(2)}tre", reread.Paragraphs[1].Text);
    }

    [Fact]
    public void WordName_Valid()
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Assert.Equal("Capitolo_2", Bookmarks.WordName("Capitolo 2", used));
        Assert.Equal("capitolo_2_2", Bookmarks.WordName("capitolo 2", used));
        Assert.Equal("S_3_maggio", Bookmarks.WordName("3 maggio", used));
        Assert.Equal(40, Bookmarks.WordName(new string('a', 60), used).Length);
    }
}

public class BookmarkAccentsTests
{
    [Fact]
    public void NameWithAccentsWrittenAndReadBack()
    {
        // The text box writes the placeholder as "backslash u N ?"; here it is simulated. "|" stands for the backslash.
        var bar = ((char)92).ToString();
        var rtf = ("{|rtf1 a|u" + (short)Bookmarks.Placeholder(0) + "?b}").Replace("|", bar);
        rtf = Bookmarks.FromPlaceholders(rtf, ["Città è"]);
        var names = new List<string>();
        Bookmarks.ToPlaceholders(rtf, names);
        Assert.Equal(["Città è"], names);
    }
}
