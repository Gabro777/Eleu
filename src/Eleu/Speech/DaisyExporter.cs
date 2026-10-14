using System.Globalization;
using System.Net;
using System.Text;
using Eleu.Documents;
using NAudio.MediaFoundation;
using NAudio.Wave;

namespace Eleu.Speech;

/// <summary>A piece to read and synchronize: a heading, a paragraph or a sentence of a long paragraph.</summary>
public sealed record DaisyUnit(string Id, string Text, int HeadingLevel);

/// <summary>A section of the book: starts with a heading (or with the start of the document) and has its own audio file.</summary>
public sealed record DaisySection(int Number, string Title, int Level, List<DaisyUnit> Units)
{
    public string SmilFile => $"s{Number:D3}.smil";

    public string AudioFile => $"a{Number:D3}.mp3";
}

/// <summary>Where a unit is in the audio of its section.</summary>
public readonly record struct UnitTime(TimeSpan Start, TimeSpan End);

/// <summary>
/// DAISY 2.02 "full audio and full text" book: the audiobook format for blind people. DAISY players
/// (programs and devices) go from heading to heading through the navigation (ncc.html), read the text
/// (content.html) and play the synchronized audio, paragraph by paragraph, thanks to the SMIL files.
/// One section per heading, of any level; one MP3 per section.
/// </summary>
public sealed class DaisyExporter(VoiceCatalog catalog, Action<string, string>? encoding = null)
{
    private static readonly TimeSpan PauseAfterHeading = TimeSpan.FromMilliseconds(700);
    private static readonly TimeSpan PauseBetweenUnits = TimeSpan.FromMilliseconds(400);

    private readonly Action<string, string> _encoding = encoding ?? ((wav, mp3) =>
    {
        MediaFoundationApi.Startup();
        AudiobookExporter.EncodeMp3(wav, mp3);
    });

    public static List<DaisySection> Sections(DocumentContent content, string documentTitle)
    {
        var sections = new List<DaisySection>();
        DaisySection? current = null;
        int units = 0;
        foreach (var paragraph in content.Paragraphs)
        {
            var text = paragraph.Text.Replace('\f', ' ').Trim();
            if (text.Length == 0)
                continue;
            if (paragraph.HeadingLevel > 0 || current is null)
            {
                bool heading = paragraph.HeadingLevel > 0;
                current = new DaisySection(sections.Count + 1, heading ? text : documentTitle, heading ? paragraph.HeadingLevel : 1, []);
                sections.Add(current);
                if (heading)
                {
                    current.Units.Add(new DaisyUnit($"u{++units}", text, paragraph.HeadingLevel));
                    continue;
                }
            }
            // Long paragraphs are split into sentences: the listener goes back a little, not a whole page.
            foreach (var passage in Passages.FromText(text, 0, text.Length))
                current.Units.Add(new DaisyUnit($"u{++units}", passage.Text, 0));
        }
        return sections;
    }

    public async Task<int> ExportAsync(DocumentContent content, string folder, AvailableVoice voice, VoiceParameters parameters,
        IProgress<ExportProgress>? progress, CancellationToken cancel)
    {
        var metadata = content.Metadata;
        var heading = metadata.Title.Length > 0 ? metadata.Title : Path.GetFileName(folder);
        var sections = Sections(content, heading);
        if (sections.Count == 0)
            return 0;
        Directory.CreateDirectory(folder);

        var times = new Dictionary<string, UnitTime>();
        var durations = new List<TimeSpan>();
        for (int s = 0; s < sections.Count; s++)
        {
            cancel.ThrowIfCancellationRequested();
            progress?.Report(new ExportProgress(s + 1, sections.Count, sections[s].Title));
            var temporary = Path.Combine(Path.GetTempPath(), $"eleu-daisy-{Guid.NewGuid():N}.wav");
            try
            {
                durations.Add(await WriteWavAsync(sections[s], temporary, voice, parameters, times, cancel));
                await Task.Run(() => _encoding(temporary, Path.Combine(folder, sections[s].AudioFile)), cancel);
            }
            finally
            {
                File.Delete(temporary);
            }
        }

        var elapsed = TimeSpan.Zero;
        for (int s = 0; s < sections.Count; s++)
        {
            await File.WriteAllTextAsync(Path.Combine(folder, sections[s].SmilFile), Smil(sections[s], times, durations[s], elapsed, heading), new UTF8Encoding(false), cancel);
            elapsed += durations[s];
        }
        await File.WriteAllTextAsync(Path.Combine(folder, "content.html"), Content(sections, heading), new UTF8Encoding(false), cancel);
        int file = sections.Count * 2 + 2;
        long byte_ = Directory.EnumerateFiles(folder).Sum(f => new FileInfo(f).Length);
        await File.WriteAllTextAsync(Path.Combine(folder, "ncc.html"),
            Ncc(sections, metadata with { Title = heading }, elapsed, file, byte_, voice.Name), new UTF8Encoding(false), cancel);
        return file;
    }

    /// <summary>The audio of the section: every unit in sequence, with a pause after; returns the total duration.</summary>
    private async Task<TimeSpan> WriteWavAsync(DaisySection section, string path, AvailableVoice voice, VoiceParameters parameters,
        Dictionary<string, UnitTime> times, CancellationToken cancel)
    {
        WaveFileWriter? writer = null;
        var now = TimeSpan.Zero;
        try
        {
            foreach (var units in section.Units)
            {
                var audio = await catalog.SynthesizeAsync(units.Text, voice, parameters, cancel);
                writer ??= new WaveFileWriter(path, audio.Format);
                writer.Write(audio.Pcm, 0, audio.Pcm.Length);
                times[units.Id] = new UnitTime(now, now + audio.Duration);
                var pause = units.HeadingLevel > 0 ? PauseAfterHeading : PauseBetweenUnits;
                int silenceBytes = (int)(audio.Format.AverageBytesPerSecond * pause.TotalSeconds) / audio.Format.BlockAlign * audio.Format.BlockAlign;
                writer.Write(new byte[silenceBytes], 0, silenceBytes);
                now += audio.Duration + TimeSpan.FromSeconds((double)silenceBytes / audio.Format.AverageBytesPerSecond);
            }
        }
        finally
        {
            writer?.Dispose();
        }
        return now;
    }

    private static string Npt(TimeSpan t) => string.Create(CultureInfo.InvariantCulture, $"npt={t.TotalSeconds:0.000}s");

    private static string Time(TimeSpan t, bool milliseconds = false) =>
        milliseconds ? $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}.{t.Milliseconds:000}" : $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}";

    private static string X(string text) => WebUtility.HtmlEncode(text);

    public static string Smil(DaisySection section, IReadOnlyDictionary<string, UnitTime> times, TimeSpan duration, TimeSpan elapsed, string heading)
    {
        var s = new StringBuilder();
        s.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        s.AppendLine("<!DOCTYPE smil PUBLIC \"-//W3C//DTD SMIL 1.0//EN\" \"http://www.w3.org/TR/REC-smil/SMIL10.dtd\">");
        s.AppendLine("<smil>");
        s.AppendLine("<head>");
        s.AppendLine("<meta name=\"dc:format\" content=\"Daisy 2.02\" />");
        s.AppendLine($"<meta name=\"dc:title\" content=\"{X(heading)}\" />");
        s.AppendLine("<meta name=\"ncc:generator\" content=\"Eleu\" />");
        s.AppendLine($"<meta name=\"ncc:totalElapsedTime\" content=\"{Time(elapsed, true)}\" />");
        s.AppendLine($"<meta name=\"ncc:timeInThisSmil\" content=\"{Time(duration, true)}\" />");
        s.AppendLine("<layout><region id=\"txtView\" /></layout>");
        s.AppendLine("</head>");
        s.AppendLine("<body>");
        s.AppendLine(string.Create(CultureInfo.InvariantCulture, $"<seq dur=\"{duration.TotalSeconds:0.000}s\">"));
        foreach (var units in section.Units)
        {
            var time = times[units.Id];
            s.AppendLine($"<par endsync=\"last\" id=\"p_{units.Id}\">");
            s.AppendLine($"<text src=\"content.html#{units.Id}\" id=\"t_{units.Id}\" />");
            s.AppendLine($"<audio src=\"{section.AudioFile}\" clip-begin=\"{Npt(time.Start)}\" clip-end=\"{Npt(time.End)}\" id=\"a_{units.Id}\" />");
            s.AppendLine("</par>");
        }
        s.AppendLine("</seq>");
        s.AppendLine("</body>");
        s.AppendLine("</smil>");
        return s.ToString();
    }

    private const string Header =
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
        "<!DOCTYPE html PUBLIC \"-//W3C//DTD XHTML 1.0 Transitional//EN\" \"http://www.w3.org/TR/xhtml1/DTD/xhtml1-transitional.dtd\">\n" +
        "<html xmlns=\"http://www.w3.org/1999/xhtml\" lang=\"it\" xml:lang=\"it\">\n";

    public static string Content(IReadOnlyList<DaisySection> sections, string heading)
    {
        var h = new StringBuilder(Header);
        h.AppendLine("<head>");
        h.AppendLine("<meta http-equiv=\"Content-type\" content=\"text/html; charset=utf-8\" />");
        h.AppendLine($"<title>{X(heading)}</title>");
        h.AppendLine("</head>");
        h.AppendLine("<body>");
        foreach (var section in sections)
            foreach (var units in section.Units)
            {
                var tag = units.HeadingLevel > 0 ? $"h{Math.Min(6, units.HeadingLevel)}" : "p";
                h.AppendLine($"<{tag} id=\"{units.Id}\"><a href=\"{section.SmilFile}#t_{units.Id}\">{X(units.Text)}</a></{tag}>");
            }
        h.AppendLine("</body>");
        h.AppendLine("</html>");
        return h.ToString();
    }

    public static string Ncc(IReadOnlyList<DaisySection> sections, Metadata metadata, TimeSpan total, int file, long size, string narrator)
    {
        var h = new StringBuilder(Header);
        h.AppendLine("<head>");
        h.AppendLine("<meta http-equiv=\"Content-type\" content=\"text/html; charset=utf-8\" />");
        h.AppendLine($"<title>{X(metadata.Title)}</title>");
        void Meta(string name, string @value) => h.AppendLine($"<meta name=\"{name}\" content=\"{X(@value)}\" />");
        Meta("dc:title", metadata.Title);
        Meta("dc:creator", metadata.Author.Length > 0 ? metadata.Author : "Sconosciuto");
        Meta("dc:date", (metadata.Created ?? DateTime.Today).ToString("yyyy-MM-dd"));
        Meta("dc:format", "Daisy 2.02");
        Meta("dc:identifier", $"eleu-{Guid.NewGuid():N}");
        Meta("dc:language", "it");
        Meta("dc:publisher", metadata.Author.Length > 0 ? metadata.Author : "Eleu");
        Meta("ncc:charset", "utf-8");
        Meta("ncc:generator", "Eleu");
        Meta("ncc:depth", Math.Min(6, sections.Max(s => s.Level)).ToString(CultureInfo.InvariantCulture));
        Meta("ncc:files", file.ToString(CultureInfo.InvariantCulture));
        Meta("ncc:footnotes", "0");
        Meta("ncc:kByteSize", (size / 1024).ToString(CultureInfo.InvariantCulture));
        Meta("ncc:maxPageNormal", "0");
        Meta("ncc:multimediaType", "audioFullText");
        Meta("ncc:narrator", narrator);
        Meta("ncc:pageFront", "0");
        Meta("ncc:pageNormal", "0");
        Meta("ncc:pageSpecial", "0");
        Meta("ncc:prodNotes", "0");
        Meta("ncc:setInfo", "1 of 1");
        Meta("ncc:sidebars", "0");
        Meta("ncc:tocItems", sections.Count.ToString(CultureInfo.InvariantCulture));
        Meta("ncc:totalTime", Time(total));
        h.AppendLine("</head>");
        h.AppendLine("<body>");
        for (int i = 0; i < sections.Count; i++)
        {
            var section = sections[i];
            // The first heading is the book title (class="title"), as the standard wants.
            int level = i == 0 ? 1 : Math.Clamp(section.Level, 1, 6);
            var cssClass = i == 0 ? " class=\"title\"" : "";
            h.AppendLine($"<h{level}{cssClass} id=\"ncc{section.Number}\"><a href=\"{section.SmilFile}#p_{section.Units[0].Id}\">{X(section.Title)}</a></h{level}>");
        }
        h.AppendLine("</body>");
        h.AppendLine("</html>");
        return h.ToString();
    }
}
