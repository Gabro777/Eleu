using System.Text;
using Eleu.Documents;

namespace Eleu.Tests;

public class TextFileTests
{
    [Fact]
    public void Utf8WithBom()
    {
        var (text, encoding) = TextFile.Read([0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("perché")]);
        Assert.Equal("perché", text);
        Assert.Equal(3, encoding.GetPreamble().Length);
    }

    [Fact]
    public void Utf8WithoutBom()
    {
        var (text, encoding) = TextFile.Read(Encoding.UTF8.GetBytes("città"));
        Assert.Equal("città", text);
        Assert.Empty(encoding.GetPreamble());
        Assert.Equal(65001, encoding.CodePage);
    }

    [Fact]
    public void WindowsAnsi()
    {
        // "caffè" in Windows-1252: è is byte 0xE8, which is not valid UTF-8 on its own.
        var (text, encoding) = TextFile.Read([.. "caff"u8, 0xE8]);
        Assert.Equal("caffè", text);
        Assert.Equal(1252, encoding.CodePage);
    }

    [Fact]
    public void Utf16()
    {
        var (text, _) = TextFile.Read([.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes("ciao")]);
        Assert.Equal("ciao", text);
    }

    [Fact]
    public void LineBreaksNormalized_OnReadAndWrite()
    {
        var (text, encoding) = TextFile.Read("a\r\nb\rc\nd"u8.ToArray());
        Assert.Equal("a\nb\nc\nd", text);
        Assert.Equal("a\r\nb\r\nc\r\nd", Encoding.UTF8.GetString(TextFile.Write(text, encoding)));
    }

    [Fact]
    public void WritingKeepsTheEncoding()
    {
        var (_, encoding) = TextFile.Read([.. "caff"u8, 0xE8]);
        Assert.Equal([.. "caff"u8, 0xE8], TextFile.Write("caffè", encoding));
    }
}
