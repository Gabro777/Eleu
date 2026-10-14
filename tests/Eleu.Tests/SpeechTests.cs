using Eleu.Documents;
using Eleu.Speech;
using NAudio.Wave;

namespace Eleu.Tests;

public class PassagesTests
{
    [Fact]
    public void OnePassagePerParagraph_SkippingBlankLines()
    {
        const string text = "Primo paragrafo.\n\n  Secondo.\nTerzo";
        var passages = Passages.FromText(text, 0, text.Length);
        Assert.Equal([new Passage("Primo paragrafo.", 0), new Passage("Secondo.", 20), new Passage("Terzo", 29)], passages);
        Assert.All(passages, b => Assert.Equal(b.Text, text.Substring(b.Start, b.Text.Length)));
    }

    [Fact]
    public void FromCursor_OrFromSelection()
    {
        const string text = "uno due tre\nquattro";
        Assert.Equal([new Passage("due tre", 4), new Passage("quattro", 12)], Passages.FromText(text, 4, text.Length));
        Assert.Equal([new Passage("due", 4)], Passages.FromText(text, 4, 7));
    }

    [Fact]
    public void LongParagraph_SplitIntoSentencesWithCorrectPositions()
    {
        var sentence = "Questa è una frase di prova abbastanza lunga da riempire spazio. ";
        var text = string.Concat(Enumerable.Repeat(sentence, 15)).TrimEnd();
        var passages = Passages.FromText(text, 0, text.Length);

        Assert.True(passages.Count > 1);
        Assert.All(passages, b => Assert.True(b.Text.Length <= Passages.MaxLength));
        Assert.All(passages, b => Assert.EndsWith(".", b.Text));
        Assert.All(passages, b => Assert.Equal(b.Text, text.Substring(b.Start, b.Text.Length)));
        Assert.Equal(text.Replace(" ", ""), string.Concat(passages.Select(b => b.Text)).Replace(" ", ""));
    }

    [Fact]
    public void Chapters_OnLevel1Headings_WithIntroduction()
    {
        var content = Documents(("Premessa breve.", 0), ("Primo", 1), ("Testo uno.", 0), ("Sotto", 2), ("Testo due.", 0), ("Secondo", 1), ("Fine.", 0));
        var chapters = Passages.Chapters(content, "Libro");

        Assert.Equal(["Introduzione", "Primo", "Secondo"], chapters.Select(c => c.Title));
        Assert.Equal(["Primo", "Testo uno.", "Sotto", "Testo due."], chapters[1].Paragraphs);
        Assert.False(chapters[0].StartsWithHeading);
        Assert.True(chapters[1].StartsWithHeading);
    }

    [Fact]
    public void Chapters_OnlyLevel2Headings_SplitsOnThose()
    {
        var content = Documents(("A", 2), ("testo", 0), ("B", 2), ("altro", 0));
        Assert.Equal(["A", "B"], Passages.Chapters(content, "Libro").Select(c => c.Title));
    }

    [Fact]
    public void Chapters_NoHeadings_OneNamedAfterTheDocument()
    {
        var chapters = Passages.Chapters(Documents(("uno", 0), ("", 0), ("due", 0)), "Lettera");
        Assert.Equal("Lettera", Assert.Single(chapters).Title);
        Assert.Equal(["uno", "due"], chapters[0].Paragraphs);
    }

    [Theory]
    [InlineData(3, 12, "Il ritorno", "03 - Il ritorno.mp3")]
    [InlineData(7, 150, "Domanda: perché?", "007 - Domanda perché.mp3")]
    [InlineData(1, 1, "a/b\\c*d", "01 - a b c d.mp3")]
    [InlineData(1, 1, "...", "01 - Capitolo.mp3")]
    public void FileName_NumberedAndWithoutForbiddenCharacters(int number, int total, string heading, string expected)
    {
        Assert.Equal(expected, Passages.FileName(number, total, heading));
    }

    [Theory]
    [InlineData(100, 0)]
    [InlineData(300, 10)]
    [InlineData(50, -6)]
    [InlineData(1000, 10)]
    public void SapiRate_FromThePercentage(int percentage, int expected)
    {
        Assert.Equal(expected, SapiEngine.SapiRate(percentage));
    }

    [Fact]
    public void PositionAt_FindsTheCurrentWord()
    {
        var audio = new SynthesizedAudio([], new WaveFormat(22050, 16, 1),
            [new WordBoundary(0, TimeSpan.Zero), new WordBoundary(5, TimeSpan.FromSeconds(0.4)), new WordBoundary(11, TimeSpan.FromSeconds(0.9))]);
        Assert.Equal(0, audio.PositionAt(TimeSpan.FromSeconds(0.2)));
        Assert.Equal(5, audio.PositionAt(TimeSpan.FromSeconds(0.5)));
        Assert.Equal(11, audio.PositionAt(TimeSpan.FromSeconds(3)));
    }

    private static DocumentContent Documents(params (string Text, int Level)[] paragraphs)
    {
        var content = new DocumentContent();
        foreach (var (text, level) in paragraphs)
        {
            var p = new Paragraph { HeadingLevel = level };
            p.Add(new Segment(text));
            content.Paragraphs.Add(p);
        }
        return content;
    }
}

/// <summary>
/// Tests with the real voices installed in Windows. If a machine has no voices for an engine,
/// that engine's test has nothing to verify and passes.
/// </summary>
public class RealSpeechTests
{
    [Fact]
    public Task Sapi_ProducesAudioAndWordBoundaries() => TestEngine(new SapiEngine());

    [Fact]
    public Task OneCore_ProducesAudioAndWordBoundaries() => TestEngine(new OneCoreEngine());

    private static async Task TestEngine(ISpeechEngine engine)
    {
        var voice = engine.Voices().FirstOrDefault();
        if (voice is null)
            return;

        var audio = await engine.SynthesizeAsync("Ciao mondo, prova.", voice, new VoiceParameters(), CancellationToken.None);

        Assert.True(audio.Duration > TimeSpan.FromMilliseconds(300), $"Audio troppo corto: {audio.Duration}");
        Assert.Contains(audio.Words, p => p.Position == 0);
        Assert.Contains(audio.Words, p => p.Position == 5);
        Assert.Equal(audio.Words.OrderBy(p => p.Start), audio.Words);
    }

    [Fact]
    public async Task Daisy_WithRealVoice_AudioMatchesTimings()
    {
        var catalog = VoiceCatalog.Default();
        if (catalog.Choose(null) is not AvailableVoice voice)
            return;

        var folder = Path.Combine(Path.GetTempPath(), "eleu-daisy-" + Guid.NewGuid());
        try
        {
            var content = new Documents.DocumentContent { Metadata = new Documents.Metadata("Prova") };
            foreach (var (text, heading) in new[] { ("Primo", 1), ("Il gatto dorme.", 0), ("Secondo", 1), ("Il cane abbaia.", 0) })
            {
                var p = new Documents.Paragraph { HeadingLevel = heading };
                p.Add(new Documents.Segment(text));
                content.Paragraphs.Add(p);
            }
            int file = await new DaisyExporter(catalog).ExportAsync(content, folder, voice, new VoiceParameters(), null, CancellationToken.None);

            Assert.Equal(6, file); // ncc, content, 2 smil, 2 mp3
            foreach (var mp3 in Directory.GetFiles(folder, "*.mp3"))
            {
                // The MP3 duration covers the end of the last piece listed in the SMIL of its section.
                using var reader = new NAudio.Wave.Mp3FileReader(mp3);
                var smil = File.ReadAllText(Path.Combine(folder, "s" + Path.GetFileNameWithoutExtension(mp3)[1..] + ".smil"));
                var end = System.Text.RegularExpressions.Regex.Matches(smil, "clip-end=\"npt=([0-9.]+)s\"").Select(m => double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)).Max();
                Assert.True(reader.TotalTime.TotalSeconds >= end - 0.1, $"{mp3}: {reader.TotalTime.TotalSeconds}s < {end}s");
                Assert.True(end > 0.3);
            }
        }
        finally
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, true);
        }
    }

    [Fact]
    public async Task Audiobook_OneMp3PerChapter()
    {
        var catalog = VoiceCatalog.Default();
        if (catalog.Choose(null) is not AvailableVoice voice)
            return;

        var folder = Path.Combine(Path.GetTempPath(), "eleu-audiolibro-" + Guid.NewGuid());
        try
        {
            var chapters = new List<Chapter>
            {
                new("Primo", ["Primo", "Uno."], StartsWithHeading: true),
                new("Secondo", ["Secondo", "Due."], StartsWithHeading: true),
            };
            var progressReports = new List<ExportProgress>();
            var created = await new AudiobookExporter(catalog).ExportAsync(
                chapters, folder, voice, new VoiceParameters(), new SyncProgress<ExportProgress>(progressReports.Add), CancellationToken.None);

            Assert.Equal(["01 - Primo.mp3", "02 - Secondo.mp3"], created.Select(Path.GetFileName));
            Assert.Equal([1, 2], progressReports.Select(a => a.Chapter));
            foreach (var file in created)
            {
                var byte0 = File.ReadAllBytes(file);
                Assert.True(byte0.Length > 2000, $"{file}: solo {byte0.Length} byte");
                bool id3 = byte0[0] == 'I' && byte0[1] == 'D' && byte0[2] == '3';
                bool mp3Frame = byte0[0] == 0xFF && (byte0[1] & 0xE0) == 0xE0;
                Assert.True(id3 || mp3Frame, $"{file} non sembra un MP3");
            }
            Assert.Empty(Directory.GetFiles(folder, "*.eleu-tmp*"));
        }
        finally
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, true);
        }
    }

    private sealed class SyncProgress<T>(Action<T> action) : IProgress<T>
    {
        public void Report(T @value) => action(@value);
    }
}
