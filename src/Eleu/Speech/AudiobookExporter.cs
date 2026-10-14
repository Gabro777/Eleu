using NAudio.MediaFoundation;
using NAudio.Wave;

namespace Eleu.Speech;

public sealed record ExportProgress(int Chapter, int TotalChapters, string Title);

/// <summary>
/// Creates the audiobook: one MP3 file per chapter, with a short pause after the heading and between paragraphs.
/// MP3 encoding uses the encoder built into Windows (Media Foundation), without external libraries.
/// </summary>
public sealed class AudiobookExporter(VoiceCatalog catalog)
{
    private static readonly WaveFormat Mp3Format = new(44100, 16, 1);
    private static readonly TimeSpan PauseAfterHeading = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan PauseBetweenParagraphs = TimeSpan.FromMilliseconds(500);
    private const int DesiredBitrate = 64_000;

    public static string[] FileNames(IReadOnlyList<Chapter> chapters) =>
        chapters.Select((c, i) => Passages.FileName(i + 1, chapters.Count, c.Title)).ToArray();

    public async Task<IReadOnlyList<string>> ExportAsync(
        IReadOnlyList<Chapter> chapters, string folder, AvailableVoice voice, VoiceParameters parameters,
        IProgress<ExportProgress>? progress, CancellationToken cancel)
    {
        MediaFoundationApi.Startup();
        Directory.CreateDirectory(folder);
        var names = FileNames(chapters);
        var created = new List<string>();

        for (int i = 0; i < chapters.Count; i++)
        {
            cancel.ThrowIfCancellationRequested();
            progress?.Report(new ExportProgress(i + 1, chapters.Count, chapters[i].Title));

            var path = Path.Combine(folder, names[i]);
            var temporary = Path.Combine(Path.GetTempPath(), $"eleu-{Guid.NewGuid():N}.wav");
            try
            {
                await WriteWavAsync(chapters[i], temporary, voice, parameters, cancel);
                await Task.Run(() => EncodeMp3(temporary, path), cancel);
            }
            finally
            {
                File.Delete(temporary);
            }
            created.Add(path);
        }
        return created;
    }

    private async Task WriteWavAsync(Chapter chapter, string path, AvailableVoice voice, VoiceParameters parameters, CancellationToken cancel)
    {
        WaveFileWriter? writer = null;
        try
        {
            for (int p = 0; p < chapter.Paragraphs.Count; p++)
            {
                var audio = await catalog.SynthesizeAsync(chapter.Paragraphs[p], voice, parameters, cancel);
                writer ??= new WaveFileWriter(path, audio.Format);
                writer.Write(audio.Pcm, 0, audio.Pcm.Length);
                var pause = p == 0 && chapter.StartsWithHeading ? PauseAfterHeading : PauseBetweenParagraphs;
                var silence = new byte[(int)(audio.Format.AverageBytesPerSecond * pause.TotalSeconds) / audio.Format.BlockAlign * audio.Format.BlockAlign];
                writer.Write(silence, 0, silence.Length);
            }
        }
        finally
        {
            writer?.Dispose();
        }
    }

    internal static void EncodeMp3(string wav, string mp3)
    {
        using var reader = new WaveFileReader(wav);
        // The Windows MP3 encoder accepts only some sample rates: everything is converted to 44.1 kHz mono.
        using var resampled = new MediaFoundationResampler(reader, Mp3Format);
        var bitrate = MediaFoundationEncoder.GetEncodeBitrates(AudioSubtypes.MFAudioFormat_MP3, Mp3Format.SampleRate, Mp3Format.Channels)
            .OrderBy(b => Math.Abs(b - DesiredBitrate))
            .FirstOrDefault(DesiredBitrate);
        // Media Foundation picks the format from the extension: the temporary file must end in .mp3.
        var temporary = Path.ChangeExtension(mp3, ".eleu-tmp.mp3");
        MediaFoundationEncoder.EncodeToMp3(resampled, temporary, bitrate);
        File.Move(temporary, mp3, overwrite: true);
    }
}
