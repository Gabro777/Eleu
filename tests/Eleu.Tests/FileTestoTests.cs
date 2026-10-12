using System.Text;
using Eleu.Documento;

namespace Eleu.Tests;

public class FileTestoTests
{
    [Fact]
    public void Utf8ConBom()
    {
        var (testo, codifica) = FileTesto.Leggi([0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("perché")]);
        Assert.Equal("perché", testo);
        Assert.Equal(3, codifica.GetPreamble().Length);
    }

    [Fact]
    public void Utf8SenzaBom()
    {
        var (testo, codifica) = FileTesto.Leggi(Encoding.UTF8.GetBytes("città"));
        Assert.Equal("città", testo);
        Assert.Empty(codifica.GetPreamble());
        Assert.Equal(65001, codifica.CodePage);
    }

    [Fact]
    public void AnsiDiWindows()
    {
        // "caffè" in Windows-1252: la è è il byte 0xE8, che non è UTF-8 valido da solo.
        var (testo, codifica) = FileTesto.Leggi([.. "caff"u8, 0xE8]);
        Assert.Equal("caffè", testo);
        Assert.Equal(1252, codifica.CodePage);
    }

    [Fact]
    public void Utf16()
    {
        var (testo, _) = FileTesto.Leggi([.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes("ciao")]);
        Assert.Equal("ciao", testo);
    }

    [Fact]
    public void AcapoNormalizzati_InLetturaEScrittura()
    {
        var (testo, codifica) = FileTesto.Leggi("a\r\nb\rc\nd"u8.ToArray());
        Assert.Equal("a\nb\nc\nd", testo);
        Assert.Equal("a\r\nb\r\nc\r\nd", Encoding.UTF8.GetString(FileTesto.Scrivi(testo, codifica)));
    }

    [Fact]
    public void ScritturaConservaLaCodifica()
    {
        var (_, codifica) = FileTesto.Leggi([.. "caff"u8, 0xE8]);
        Assert.Equal([.. "caff"u8, 0xE8], FileTesto.Scrivi("caffè", codifica));
    }
}
