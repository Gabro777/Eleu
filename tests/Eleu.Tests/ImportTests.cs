using System.Drawing;
using System.Drawing.Imaging;
using Eleu.Proofing;
using Eleu.Import;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace Eleu.Tests;

public class TextCleanupTests
{
    [Fact]
    public void RejoinsHyphenatedWordsAndJoinsLines()
    {
        IReadOnlyList<string> page = ["Il gatto cammina sul tet-", "to della casa e guarda", "il cielo.", "", "Nuovo paragrafo qui."];
        Assert.Equal(["Il gatto cammina sul tetto della casa e guarda il cielo.", "Nuovo paragrafo qui."],
            TextCleanup.Paragraphs([page]));
    }

    [Fact]
    public void RemovePageNumbersAndRepeatedHeaders()
    {
        IReadOnlyList<string> Page(int n, string text) => ["Rivista di cucina - numero 7", text, $"- {n} -"];
        var paragraphs = TextCleanup.Paragraphs([
            Page(1, "La pasta si cuoce in acqua salata e"),
            Page(2, "si scola quando è al dente, poi si condisce."),
            Page(3, "Fine della ricetta."),
        ]);
        Assert.Equal(["La pasta si cuoce in acqua salata e si scola quando è al dente, poi si condisce. Fine della ricetta."], paragraphs);
    }

    [Fact]
    public void LongLineEndingWithPeriod_DoesNotCloseTheParagraph()
    {
        IReadOnlyList<string> page = ["Questa riga è lunga e finisce proprio con un punto.", "Questa invece continua lo stesso paragrafo e va", "avanti."];
        Assert.Single(TextCleanup.Paragraphs([page]));
    }

    [Theory]
    [InlineData("12")]
    [InlineData("Pag. 3")]
    [InlineData("pagina 4 di 20")]
    [InlineData("5/20")]
    public void RecognizedPageNumbers(string number)
    {
        IReadOnlyList<string> page = ["Testo della pagina che non va toccato.", number];
        Assert.Equal(["Testo della pagina che non va toccato."], TextCleanup.Paragraphs([page]));
    }
}

public class LikelyErrorsTests
{
    private static readonly HashSet<string> Dictionary = ["come", "casa", "stai", "il", "maggio", "una", "frase"];

    private static List<LikelyError> Find(string text) => LikelyErrors.Find(
        text,
        t => System.Text.RegularExpressions.Regex.Matches(t, @"\p{L}+")
            .Where(m => !Dictionary.Contains(m.Value.ToLowerInvariant()))
            .Select(m => (m.Index, m.Length)),
        p => p == "frse" ? ["frase"] : [],
        p => Dictionary.Contains(p));

    [Fact]
    public void DigitsInsteadOfLetters_WithSuggestion()
    {
        var error = Assert.Single(Find("c0me stai"));
        Assert.Equal(new LikelyError(0, 4, "c0me", "come"), error);
    }

    [Fact]
    public void OcrStrayCharacters()
    {
        Assert.Equal("casa", Assert.Single(Find("ca|sa")).Suggestion);
    }

    [Fact]
    public void OrdinalsAndNumbersAreNotErrors()
    {
        Assert.Empty(Find("il 1° maggio 2026"));
    }

    [Fact]
    public void WordsNotInDictionary_InOrder()
    {
        var errors = Find("una frse c0me");
        Assert.Equal(["frse", "c0me"], errors.Select(e => e.Word));
        Assert.Null(errors[0].Suggestion); // asked of the dictionary only when stopping on that error
        Assert.Equal("come", errors[1].Suggestion);
    }

    [Theory]
    [InlineData("dlvano", new[] { "davano" }, "divano")]        // generated with the l→i swap and found in the dictionary
    [InlineData("rnare", new[] { "rare", "mare" }, "mare")]     // among the suggestions, the one with an rn→m swap
    [InlineData("Clue", new[] { "Clue", "Due" }, "Due")]
    [InlineData("xyz", new[] { "casa", "cosa" }, "casa")]       // no swap: the dictionary's first
    public void BestSuggestion_PrefersTypicalOcrSwaps(string word, string[] suggestions, string expected)
    {
        Assert.Equal(expected, LikelyErrors.BestSuggestion(word, suggestions, p => p == "divano"));
    }

    [Fact]
    public void TwoCapitalIsAtSentenceStart_AreTheArticleIl()
    {
        var error = Assert.Single(Find("II gatto dorme. II cane abbaia."), e => e.Start == 0);
        Assert.Equal("Il", error.Suggestion);
        Assert.Contains(Find("II gatto dorme. II cane abbaia."), e => e.Start == 16 && e.Suggestion == "Il");
        Assert.DoesNotContain(Find("Enrico II è nato"), e => e.Suggestion == "Il");
    }

    [Fact]
    public void Next_ForwardAndBackward()
    {
        var errors = Find("una frse c0me");
        Assert.Equal("frse", LikelyErrors.Next(errors, 0, forward: true)!.Word);
        Assert.Equal("c0me", LikelyErrors.Next(errors, 5, forward: true)!.Word);
        Assert.Null(LikelyErrors.Next(errors, 14, forward: true));
        Assert.Equal("frse", LikelyErrors.Next(errors, 9, forward: false)!.Word);
    }
}

/// <summary>Tests with the real Windows components. If a machine does not have them (e.g. without Italian), there is nothing to verify.</summary>
public class RealImportTests
{
    [Fact]
    public void ItalianSpellChecker()
    {
        var speller = SpellChecker.Create("it-IT");
        if (speller is null)
            return;

        var unknown = speller.UnknownWords("Questa è una frse sbaglaita.").ToList();
        Assert.Equal([(13, 4), (18, 9)], unknown);
        Assert.Contains("frase", speller.Suggestions("frse"));
        Assert.True(speller.IsCorrect("perché"));
    }

    [Fact]
    public async Task PdfWithText_ReadAndCleaned()
    {
        var path = TemporaryFile(".pdf");
        try
        {
            var builder = new PdfDocumentBuilder();
            var character = builder.AddStandard14Font(Standard14Font.Helvetica);
            string[][] pages =
            [
                ["Notiziario del circolo", "Questo testo di prova serve a controllare che le parole divi-", "se a fine riga vengano ricucite correttamente.", "1"],
                ["Notiziario del circolo", "Seconda pagina con un altro paragrafo di prova abbastanza lungo.", "2"],
            ];
            foreach (var rows in pages)
            {
                var page = builder.AddPage(595, 842);
                for (int i = 0; i < rows.Length; i++)
                    page.AddText(rows[i], 11, new PdfPoint(60, 780 - i * 20), character);
            }
            File.WriteAllBytes(path, builder.Build());

            var result = await Importer.ImportAsync(path, null, CancellationToken.None);

            Assert.False(result.WithOcr);
            Assert.Equal(2, result.Pages);
            var text = string.Join("\n", result.Paragraphs);
            Assert.Contains("parole divise a fine riga vengano ricucite", text);
            Assert.DoesNotContain("Notiziario", text);
            Assert.Contains("Seconda pagina", text);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ImageAndScannedPdf_ReadWithOcr()
    {
        if (!Windows.Media.Ocr.OcrEngine.AvailableRecognizerLanguages.Any())
            return;

        var png = TemporaryFile(".png");
        var pdf = TemporaryFile(".pdf");
        try
        {
            using (var image = new Bitmap(1600, 400))
            using (var g = Graphics.FromImage(image))
            using (var character = new Font("Arial", 40))
            {
                g.Clear(Color.White);
                g.DrawString("Il gatto dorme sul divano.", character, Brushes.Black, 40, 60);
                g.DrawString("Domani piove in montagna.", character, Brushes.Black, 40, 200);
                image.Save(png, ImageFormat.Png);
            }

            var fromImage = await Importer.ImportAsync(png, null, CancellationToken.None);
            Assert.True(fromImage.WithOcr);
            Assert.Contains("gatto dorme sul divano", string.Join(" ", fromImage.Paragraphs));

            // A PDF containing only the image, like a scan: no text, it must go through OCR.
            var builder = new PdfDocumentBuilder();
            builder.AddPage(595, 842).AddPng(File.ReadAllBytes(png), new PdfRectangle(new PdfPoint(40, 600), new PdfPoint(555, 729)));
            File.WriteAllBytes(pdf, builder.Build());

            var progressReports = new List<ImportProgress>();
            var fromPdf = await Importer.ImportAsync(pdf, new SyncProgress(progressReports.Add), CancellationToken.None);
            Assert.True(fromPdf.WithOcr);
            Assert.Equal([new ImportProgress(1, 1, true)], progressReports);
            Assert.Contains("piove in montagna", string.Join(" ", fromPdf.Paragraphs));
        }
        finally
        {
            File.Delete(png);
            File.Delete(pdf);
        }
    }

    private static string TemporaryFile(string extension) => Path.Combine(Path.GetTempPath(), $"eleu-test-{Guid.NewGuid():N}{extension}");

    private sealed class SyncProgress(Action<ImportProgress> action) : IProgress<ImportProgress>
    {
        public void Report(ImportProgress @value) => action(@value);
    }
}
