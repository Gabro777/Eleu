using Eleu.Voce;

namespace Eleu.Tests;

public class PronunceTests
{
    private static readonly Pronuncia[] Dizionario = [new("NVDA", "enne vu di a"), new("ONU", "o enne u"), new("ONUS", "onus")];

    [Fact]
    public void Applica_ParoleIntereSenzaMaiuscole()
    {
        var (testo, _) = Pronunce.Applica("Uso nvda e l'ONU, non ONUSTO né ONUS.", Dizionario);
        Assert.Equal("Uso enne vu di a e l'o enne u, non ONUSTO né onus.", testo);
    }

    [Fact]
    public void Origine_RiportaAlTestoScritto()
    {
        const string scritto = "Apri NVDA ora";
        var (detto, origine) = Pronunce.Applica(scritto, Dizionario);
        int ora = detto.IndexOf("ora");
        Assert.Equal(scritto.IndexOf("ora"), origine[ora]);
        Assert.Equal(scritto.IndexOf("NVDA"), origine[detto.IndexOf("enne")]);
        Assert.Equal(scritto.Length, origine[^1]);
    }

    [Fact]
    public void SenzaPronunce_UgualeEOrigineIdentica()
    {
        var (testo, origine) = Pronunce.Applica("ciao", []);
        Assert.Equal("ciao", testo);
        Assert.Equal([0, 1, 2, 3, 4], origine);
    }
}
