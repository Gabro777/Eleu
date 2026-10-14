using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Eleu.Documents;
using Eleu.Speech;
using NAudio.Wave;

namespace Eleu.Tests;

public class DaisyTests
{
    /// <summary>A fake voice: silence 40 milliseconds long per character.</summary>
    private sealed class FakeEngine : ISpeechEngine
    {
        public EngineKind Kind => EngineKind.Sapi;

        public IReadOnlyList<AvailableVoice> Voices() => [new("finta", "Voce finta", "it-IT", EngineKind.Sapi)];

        public Task<SynthesizedAudio> SynthesizeAsync(string text, AvailableVoice voice, VoiceParameters parameters, CancellationToken cancel)
        {
            var format = new WaveFormat(22050, 16, 1);
            int audioBytes = (int)(format.AverageBytesPerSecond * 0.04 * text.Length) / 2 * 2;
            return Task.FromResult(new SynthesizedAudio(new byte[audioBytes], format, []));
        }
    }

    private static DocumentContent Sample()
    {
        var c = new DocumentContent { Metadata = new Metadata("Ricette", "Anna") };
        void P(string text, int heading = 0)
        {
            var p = new Paragraph { HeadingLevel = heading };
            p.Add(new Segment(text));
            c.Paragraphs.Add(p);
        }
        P("Introduzione breve.");
        P("Primi", 1);
        P("Servono uova e farina.");
        P("Pasta fresca", 2);
        P(string.Join(" ", Enumerable.Repeat("Si impasta con calma.", 30))); // long: becomes several sentences
        return c;
    }

    [Fact]
    public void Sections_OnePerHeadingAndLongParagraphsSplit()
    {
        var sections = DaisyExporter.Sections(Sample(), "Ricette");
        Assert.Equal(["Ricette", "Primi", "Pasta fresca"], sections.Select(s => s.Title));
        Assert.Equal([1, 1, 2], sections.Select(s => s.Level));
        Assert.True(sections[2].Units.Count > 2, "il paragrafo lungo deve diventare più unità");
        Assert.Equal(1, sections[1].Units[0].HeadingLevel); // the heading is the first unit of its section
    }

    [Fact]
    public async Task Export_LinkedFilesAndConsistentTimes()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"eleu-daisy-test-{Guid.NewGuid():N}");
        try
        {
            var catalog = new VoiceCatalog(new FakeEngine());
            var exporter = new DaisyExporter(catalog, (wav, mp3) => File.Copy(wav, mp3));
            int file = await exporter.ExportAsync(Sample(), folder, catalog.Voices[0], new VoiceParameters(), null, CancellationToken.None);

            var names = Directory.GetFiles(folder).Select(Path.GetFileName).Order().ToList();
            Assert.Equal(file, names.Count);
            Assert.Equal(["a001.mp3", "a002.mp3", "a003.mp3", "content.html", "ncc.html", "s001.smil", "s002.smil", "s003.smil"], names);

            // All files are well-formed XML, and the links point to ids that exist.
            var ncc = XDocument.Load(Path.Combine(folder, "ncc.html"));
            var content = XDocument.Load(Path.Combine(folder, "content.html"));
            var textId = content.Descendants().Select(e => (string?)e.Attribute("id")).OfType<string>().ToHashSet();
            Assert.Equal("Daisy 2.02", Meta(ncc, "dc:format"));
            Assert.Equal("3", Meta(ncc, "ncc:tocItems"));
            Assert.Equal("audioFullText", Meta(ncc, "ncc:multimediaType"));
            Assert.Equal("title", ncc.Descendants().First(e => e.Name.LocalName == "h1").Attribute("class")?.Value);

            var total = TimeSpan.Zero;
            foreach (var smilFile in names.Where(n => n!.EndsWith(".smil")))
            {
                var smilText = File.ReadAllText(Path.Combine(folder, smilFile!));
                var smil = XDocument.Parse(smilText.Replace("<!DOCTYPE smil PUBLIC \"-//W3C//DTD SMIL 1.0//EN\" \"http://www.w3.org/TR/REC-smil/SMIL10.dtd\">", ""));
                double waitEnd = 0;
                foreach (var audio in smil.Descendants("audio"))
                {
                    double start = Seconds(audio.Attribute("clip-begin")!.Value), end = Seconds(audio.Attribute("clip-end")!.Value);
                    Assert.True(start >= waitEnd - 0.001 && end > start, $"{smilFile}: clip {start}-{end}");
                    waitEnd = end;
                }
                foreach (var text in smil.Descendants("text"))
                    Assert.Contains(text.Attribute("src")!.Value.Split('#')[1], textId);
                var duration = TimeSpan.FromSeconds(Seconds("npt=" + smil.Descendants("seq").First().Attribute("dur")!.Value));
                // The last clip ends before the end of the section (the pause comes after).
                Assert.True(waitEnd <= duration.TotalSeconds);
                total += duration;
            }
            Assert.Equal($"{(int)total.TotalHours:00}:{total.Minutes:00}:{total.Seconds:00}", Meta(ncc, "ncc:totalTime"));
        }
        finally
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, true);
        }

        static string? Meta(XDocument ncc, string name) =>
            ncc.Descendants().FirstOrDefault(e => e.Name.LocalName == "meta" && (string?)e.Attribute("name") == name)?.Attribute("content")?.Value;

        static double Seconds(string npt) =>
            double.Parse(Regex.Match(npt, @"npt=([\d.]+)s").Groups[1].Value, CultureInfo.InvariantCulture);
    }
}
