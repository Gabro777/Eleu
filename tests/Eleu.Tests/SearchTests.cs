using Eleu.Documents;

namespace Eleu.Tests;

public class SearchTests
{
    private static readonly SearchOptions Normal = new();

    [Fact]
    public void ForwardFindsTheFirstAfterTheCursor()
    {
        Assert.Equal(8, Search.Find("gatto e cane gatto", "cane", 3, true, Normal));
        Assert.Equal(13, Search.Find("gatto e cane gatto", "gatto", 1, true, Normal));
    }

    [Fact]
    public void BackwardFindsTheLastBeforeTheCursor()
    {
        const string text = "gatto e cane gatto";
        Assert.Equal(0, Search.Find(text, "gatto", 13, false, Normal));
        Assert.Equal(13, Search.Find(text, "gatto", text.Length, false, Normal));
        Assert.Equal(-1, Search.Find(text, "gatto", 0, false, Normal));
    }

    [Fact]
    public void CaseIgnoredUnlessRequested()
    {
        Assert.Equal(0, Search.Find("Città", "CITTÀ", 0, true, Normal));
        Assert.Equal(-1, Search.Find("Città", "CITTÀ", 0, true, new SearchOptions(MatchCase: true)));
    }

    [Fact]
    public void WholeWord()
    {
        var options = new SearchOptions(WholeWord: true);
        Assert.Equal(10, Search.Find("caneide e cane", "cane", 0, true, options));
        Assert.Equal(0, Search.Find("caneide e cane", "cane", 0, true, Normal));
        Assert.Equal(-1, Search.Find("caneide", "cane", 7, false, options));
    }

    [Fact]
    public void FindAll_WithoutOverlaps()
    {
        Assert.Equal([0, 2], Search.FindAll("aaaa", "aa", Normal));
        Assert.Empty(Search.FindAll("abc", "", Normal));
    }

    [Fact]
    public void Matches_HonorsTheOptions()
    {
        Assert.True(Search.Matches("Roma", "roma", Normal));
        Assert.False(Search.Matches("Roma", "roma", new SearchOptions(MatchCase: true)));
    }
}

public class WildcardSearchTests
{
    private static readonly SearchOptions Wildcards = new(Wildcards: true);

    private static string? Found(string text, string find, SearchOptions options, int from = 0, bool forward = true) =>
        Search.FindMatch(text, find, from, forward, options) is var (start, length) ? text.Substring(start, length) : null;

    [Fact]
    public void QuestionMark_OneCharacter() => Assert.Equal("gatto", Found("il gatto", "g?tto", Wildcards));

    [Fact]
    public void TrailingAsterisk_CompletesTheWord() => Assert.Equal("casale", Found("una casale grande", "cas*", Wildcards));

    [Fact]
    public void MiddleAsterisk_AsFewAsPossible() => Assert.Equal("cane e il gatto", Found("il cane e il gatto e il gatto", "c*gatto", Wildcards));

    [Fact]
    public void Hash_OneDigit() => Assert.Equal("2026", Found("anno 2026", "20##", Wildcards));

    [Fact]
    public void OtherCharactersAreNormal() => Assert.Equal("(a+b)", Found("x (a+b) y", "(a+b)", Wildcards));

    [Fact]
    public void WholeWordAndBackward()
    {
        var options = Wildcards with { WholeWord = true };
        Assert.Equal("gatta", Found("gattara gatta", "gatt?", options));
        Assert.Equal("gatto", Found("gatto gatta", "gatt?", Wildcards, from: 6, forward: false));
    }

    [Fact]
    public void AllAndMatches()
    {
        Assert.Equal([(0, 4), (5, 6)], Search.FindAllMatches("casa casale", "cas*", Wildcards));
        Assert.True(Search.Matches("Casale", "cas*", Wildcards));
        Assert.False(Search.Matches("casale!", "cas*", Wildcards));
    }
}
