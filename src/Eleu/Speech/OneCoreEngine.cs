using NAudio.Wave;
using Windows.Media.Core;
using Windows.Media.SpeechSynthesis;

namespace Eleu.Speech;

/// <summary>The modern voices built into Windows 10 and 11 (for Italian: Elsa and Cosimo).</summary>
public sealed class OneCoreEngine : ISpeechEngine
{
    public EngineKind Kind => EngineKind.OneCore;

    public IReadOnlyList<AvailableVoice> Voices()
    {
        try
        {
            return SpeechSynthesizer.AllVoices
                .Select(v => new AvailableVoice($"onecore:{v.Id}", v.DisplayName, v.Language, EngineKind.OneCore))
                .ToList();
        }
        catch (Exception e) when (e is System.Runtime.InteropServices.COMException or TypeLoadException)
        {
            // On systems without OneCore voices (e.g. some server editions) only SAPI ones are used.
            return [];
        }
    }

    public async Task<SynthesizedAudio> SynthesizeAsync(string text, AvailableVoice voice, VoiceParameters parameters, CancellationToken cancel)
    {
        var idWindows = voice.Id["onecore:".Length..];
        using var synthesizer = new SpeechSynthesizer
        {
            Voice = SpeechSynthesizer.AllVoices.First(v => v.Id == idWindows),
        };
        synthesizer.Options.SpeakingRate = Math.Clamp(parameters.Rate / 100.0, 0.5, 6.0);
        synthesizer.Options.AudioVolume = Math.Clamp(parameters.Volume / 100.0, 0.0, 1.0);
        synthesizer.Options.IncludeWordBoundaryMetadata = true;

        using var stream = await synthesizer.SynthesizeTextToStreamAsync(text).AsTask(cancel);

        var words = stream.TimedMetadataTracks
            .SelectMany(trace => trace.Cues)
            .OfType<SpeechCue>()
            .Where(c => c.StartPositionInInput is not null)
            .Select(c => new WordBoundary(c.StartPositionInInput!.Value, c.StartTime))
            .OrderBy(c => c.Start)
            .ToList();

        using var wav = new MemoryStream();
        await stream.AsStreamForRead().CopyToAsync(wav, cancel);
        wav.Position = 0;
        using var reader = new WaveFileReader(wav);
        using var pcm = new MemoryStream();
        await reader.CopyToAsync(pcm, cancel);
        return new SynthesizedAudio(pcm.ToArray(), reader.WaveFormat, words);
    }
}
