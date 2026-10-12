using Eleu.Braille;

namespace Eleu.Tests;

public class BrailleTests
{
    /// <summary>Celle scritte come numeri dei punti separati da trattini; 0 = cella vuota. Es. "46-14" = maiuscola, c.</summary>
    private static string Punti(string celle) =>
        string.Concat(celle.Split('-').Select(c => (char)(0x2800 + c.Where(char.IsAsciiDigit).Sum(p => p == '0' ? 0 : 1 << (p - '1')))));

    [Fact]
    public void Punti_NotazioneDiProva()
    {
        Assert.Equal((char)0x2801, Punti("1")[0]);
        Assert.Equal((char)0x283F, Punti("123456")[0]);
        Assert.Equal(TraduttoreBraille.CellaVuota, Punti("0")[0]);
    }

    [Fact]
    public void Traduce_MaiuscoleELettereAccentate()
    {
        // ⠨ maiuscola, c i t t à, spazio, è, spazio, b e l l a
        Assert.Equal(Punti("46-14-24-2345-2345-12356-0-2346-0-12-15-123-123-1"), TraduttoreBraille.Traduci("Città è bella"));
    }

    [Fact]
    public void Traduce_NumeriEPunteggiatura()
    {
        // h o, spazio, segnanumero 1 2, spazio, k g punto
        Assert.Equal(Punti("125-135-0-3456-1-12-0-13-1245-256"), TraduttoreBraille.Traduci("ho 12 kg."));
    }

    [Fact]
    public void TestoLungo_TradottoTutto()
    {
        var testo = string.Concat(Enumerable.Repeat("casa ", 3000)).TrimEnd();
        var braille = TraduttoreBraille.Traduci(testo);
        Assert.Equal(testo.Length, braille.Length);
        Assert.Equal(Punti("14-1-234-1"), braille[^4..]);
    }

    [Fact]
    public void Impagina_VaACapoTraLeParoleConRientro()
    {
        var celle = Punti("1-1-1-0-12-12-12-0-14-14-14-0-145-145-145");
        var pagine = Impaginatore.Impagina([new ParagrafoBraille(celle)], new FormatoPagina(CellePerRiga: 9, RighePerPagina: 20));

        var righe = Assert.Single(pagine);
        Assert.Equal([Punti("0-0-1-1-1-0-12-12-12"), Punti("14-14-14-0-145-145-145")], righe);
        Assert.All(righe, r => Assert.True(r.Length <= 9));
    }

    [Fact]
    public void Impagina_TitoloSenzaRientroConRigaVuotaPrima()
    {
        var pagine = Impaginatore.Impagina(
            [new ParagrafoBraille(Punti("1")), new ParagrafoBraille(Punti("12"), Titolo: true), new ParagrafoBraille(Punti("14"))],
            new FormatoPagina(20, 20));
        Assert.Equal([Punti("0-0-1"), "", Punti("12"), Punti("0-0-14")], pagine[0]);
    }

    [Fact]
    public void Impagina_DivideInPagineESpezzaLeParoleTroppoLunghe()
    {
        var parolaLunga = Punti(string.Join("-", Enumerable.Repeat("1", 25)));
        var pagine = Impaginatore.Impagina([new ParagrafoBraille(parolaLunga)], new FormatoPagina(CellePerRiga: 10, RighePerPagina: 2));

        Assert.Equal(2, pagine.Count);
        Assert.Equal([10, 10], pagine[0].Select(r => r.Length));
        Assert.Equal([7], pagine[1].Select(r => r.Length)); // 2 di rientro + 8 + 10 + 7 = 25 celle
    }

    [Fact]
    public void Brf_TabellaAsciiStandard()
    {
        Assert.Equal(64, FormatoBrf.TabellaAscii.Length);
        Assert.Equal(' ', FormatoBrf.InAscii(Punti("0")[0]));
        Assert.Equal('A', FormatoBrf.InAscii(Punti("1")[0]));
        Assert.Equal('B', FormatoBrf.InAscii(Punti("12")[0]));
        Assert.Equal('L', FormatoBrf.InAscii(Punti("123")[0]));
        Assert.Equal('#', FormatoBrf.InAscii(Punti("3456")[0]));
        Assert.Equal('.', FormatoBrf.InAscii(Punti("46")[0]));
        Assert.Equal('4', FormatoBrf.InAscii(Punti("256")[0]));
        Assert.Equal('\\', FormatoBrf.InAscii(Punti("1256")[0]));
        Assert.Equal('=', FormatoBrf.InAscii(Punti("123456")[0]));
    }

    [Fact]
    public void Brf_RigheEPagine()
    {
        var pagine = new List<List<string>> { new() { Punti("1-12"), Punti("14") }, new() { Punti("145") } };
        Assert.Equal("AB\r\nC\r\n\fD\r\n\f", FormatoBrf.Testo(pagine));
    }
}
