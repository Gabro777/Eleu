using Eleu.Speech;

namespace Eleu.Tests;

public class PronunciationsTests
{
    private static readonly Pronunciation[] Dictionary = [new("NVDA", "enne vu di a"), new("ONU", "o enne u"), new("ONUS", "onus")];

    [Fact]
    public void Apply_WholeWordsIgnoringCase()
    {
        var (text, _) = Pronunciations.Apply("Uso nvda e l'ONU, non ONUSTO né ONUS.", Dictionary);
        Assert.Equal("Uso enne vu di a e l'o enne u, non ONUSTO né onus.", text);
    }

    [Fact]
    public void Source_MapsBackToTheWrittenText()
    {
        const string written = "Apri NVDA ora";
        var (spoken, source) = Pronunciations.Apply(written, Dictionary);
        int now = spoken.IndexOf("ora");
        Assert.Equal(written.IndexOf("ora"), source[now]);
        Assert.Equal(written.IndexOf("NVDA"), source[spoken.IndexOf("enne")]);
        Assert.Equal(written.Length, source[^1]);
    }

    [Fact]
    public void NoPronunciations_SameAndIdentitySource()
    {
        var (text, source) = Pronunciations.Apply("ciao", []);
        Assert.Equal("ciao", text);
        Assert.Equal([0, 1, 2, 3, 4], source);
    }
}
