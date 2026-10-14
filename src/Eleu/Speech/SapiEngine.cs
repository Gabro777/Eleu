using System.Speech.AudioFormat;
using System.Speech.Synthesis;
using NAudio.Wave;

namespace Eleu.Speech;

/// <summary>
/// SAPI 5 voices: the classic Windows ones and those installed separately
/// (many blind users use purchased SAPI voices, like Loquendo or Vocalizer).
/// </summary>
public sealed class SapiEngine : ISpeechEngine
{
    private static readonly WaveFormat AudioFormat = new(22050, 16, 1);

    public EngineKind Kind => EngineKind.Sapi;

    public IReadOnlyList<AvailableVoice> Voices()
    {
        using var synthesizer = new SpeechSynthesizer();
        return synthesizer.GetInstalledVoices()
            .Where(v => v.Enabled)
            .Select(v => new AvailableVoice($"sapi:{v.VoiceInfo.Name}", v.VoiceInfo.Name, v.VoiceInfo.Culture.Name, EngineKind.Sapi))
            .ToList();
    }

    public Task<SynthesizedAudio> SynthesizeAsync(string text, AvailableVoice voice, VoiceParameters parameters, CancellationToken cancel) =>
        // SAPI is synchronous: it is made to work off the UI thread.
        Task.Run(() =>
        {
            cancel.ThrowIfCancellationRequested();
            using var synthesizer = new SpeechSynthesizer();
            synthesizer.SelectVoice(voice.Name);
            synthesizer.Rate = SapiRate(parameters.Rate);
            synthesizer.Volume = Math.Clamp(parameters.Volume, 0, 100);

            var words = new List<WordBoundary>();
            synthesizer.SpeakProgress += (_, e) => words.Add(new WordBoundary(e.CharacterPosition, e.AudioPosition));

            using var pcm = new MemoryStream();
            synthesizer.SetOutputToAudioStream(pcm,
                new SpeechAudioFormatInfo(AudioFormat.SampleRate, AudioBitsPerSample.Sixteen, AudioChannel.Mono));
            synthesizer.Speak(text);
            synthesizer.SetOutputToNull();
            return new SynthesizedAudio(pcm.ToArray(), AudioFormat, words);
        }, cancel);

    /// <summary>
    /// SAPI uses a scale from -10 to 10 in which every 10 steps the rate roughly triples:
    /// Eleu's percentage (100 = normal) is converted to that scale.
    /// </summary>
    public static int SapiRate(int percentage)
    {
        var multiplier = Math.Clamp(percentage, 1, 1000) / 100.0;
        return (int)Math.Clamp(Math.Round(10 * Math.Log(multiplier, 3)), -10, 10);
    }
}
