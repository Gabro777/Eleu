using Eleu.Proofing;
using Eleu.Documents;

namespace Eleu.Tests;

public class CleanupTests
{
    private static string Clean(string text, CleanupOptions options) => Cleanup.Apply(text, Cleanup.Edits(text, options));

    [Fact]
    public void DoubleAndEdgeSpaces() =>
        Assert.Equal("Uno due\nTre", Clean("  Uno   due  \nTre", new CleanupOptions()));

    [Fact]
    public void RepeatedBlankLines_OneRemains() =>
        Assert.Equal("Uno\n\nDue", Clean("Uno\n\n\n\nDue", new CleanupOptions()));

    [Fact]
    public void Hyphenation_WordRejoined() =>
        Assert.Equal("ricostruire la casa", Clean("rico-\nstruire la casa", new CleanupOptions()));

    [Fact]
    public void BrokenParagraphs_OnlyIfRequested()
    {
        const string text = "La casa era\ngrande. Poi\nArrivò lui.";
        Assert.Equal(text, Clean(text, new CleanupOptions()));
        Assert.Equal("La casa era grande. Poi\nArrivò lui.", Clean(text, new CleanupOptions(BrokenParagraphs: true)));
    }

    [Fact]
    public void TablesUntouched()
    {
        var text = string.Concat("a  b\n", TextStructure.RowStart, "\n", "x  y", TextStructure.CellEnd, TextStructure.RowEnd, "\n", "c  d");
        var cleaned = Clean(text, new CleanupOptions());
        Assert.StartsWith("a b\n", cleaned);
        Assert.Contains("x  y", cleaned);
        Assert.EndsWith("c d", cleaned);
    }
}

public class MultipleReplacementsTests
{
    [Fact]
    public void AllAtOnce_WithoutChaining()
    {
        const string text = "il gatto e il cane";
        var edits = MultipleReplacements.Edits(text,
            [new("gatto", "cane"), new("cane", "topo")], new SearchOptions(WholeWord: true));
        // "gatto" becomes "cane" but that new "cane" does not become "topo": the search is in the starting text.
        Assert.Equal("il cane e il topo", Cleanup.Apply(text, edits));
    }
}

public class FullStatisticsTests
{
    [Fact]
    public void Count_WithSentencesAndApostrophes()
    {
        var s = FullStatistics.Compute("L'acqua è fresca. Bevo 2 bicchieri!\nPoi basta");
        Assert.Equal(9, s.Words); // L acqua è fresca Bevo 2 bicchieri Poi basta
        Assert.Equal(3, s.Sentences);
        Assert.Equal(2, s.Paragraphs);
        Assert.Equal(1, s.Digits);
        Assert.Equal(s.Characters - s.Spaces, s.CharactersWithoutSpaces);
        Assert.NotNull(s.Gulpease);
    }

    [Fact]
    public void Gulpease_ShortSentencesEasy_LongSentencesHard()
    {
        var easy = FullStatistics.Compute("Il gatto dorme. Il cane abbaia. Io leggo.");
        var hard = FullStatistics.Compute(
            "L'implementazione dell'infrastruttura amministrativa regionale presuppone un'articolata concertazione interistituzionale.");
        Assert.True(easy.Gulpease > 80, $"facile: {easy.Gulpease}");
        Assert.True(hard.Gulpease < 40, $"difficile: {hard.Gulpease}");
        Assert.Equal("molto facile", easy.Rating);
    }
}

public class ComparisonTests
{
    [Fact]
    public void Differences_ChangedAddedRemoved()
    {
        string[] here = ["Titolo", "Il gatto dorme", "Fine", "Solo qui"];
        string[] other = ["Titolo", "Nuovo in cima al corpo", "Il cane dorme", "Fine"];
        var differences = Comparison.Differences(here, other);
        Assert.Equal(3, differences.Count);
        Assert.Contains(differences, d => d.Kind == DifferenceKind.Changed && d.Here == 1 && d.OtherText == "Il cane dorme"
            || d.Kind == DifferenceKind.Changed && d.Here == 1);
        Assert.Contains(differences, d => d.Kind == DifferenceKind.OnlyHere && d.TextHere == "Solo qui");
        Assert.Contains(differences, d => d.Kind == DifferenceKind.OnlyInOther || d.Kind == DifferenceKind.Changed && d.OtherText.StartsWith("Nuovo"));
    }

    [Fact]
    public void Equal_NoDifference_EvenWithDifferentSpaces() =>
        Assert.Empty(Comparison.Differences(["Uno  due", "tre"], ["Uno due", "tre "]));

    [Fact]
    public void ChangedWords_SaysWhatChanges() =>
        Assert.Equal("Qui: «gatto»; nell'altro: «cane».", Comparison.ChangedWords("Il gatto dorme", "Il cane dorme"));
}
