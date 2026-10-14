using NAudio.Wave;

namespace Eleu.Speech;

public enum EngineKind { OneCore, Sapi }

/// <summary>A voice installed in Windows. The Id includes the engine, so it stays unique.</summary>
public sealed record AvailableVoice(string Id, string Name, string Language, EngineKind Engine)
{
    public string Description => $"{Name} ({Language}{(Engine == EngineKind.Sapi ? ", SAPI" : "")})";
}

/// <summary>Rate and volume as a percentage: 100 = normal.</summary>
public sealed record VoiceParameters(int Rate = 100, int Volume = 100)
{
    public const int MinRate = 50;
    public const int MaxRate = 300;
}

/// <summary>Start of a word: position in the synthesized text and instant in the audio.</summary>
public readonly record struct WordBoundary(int Position, TimeSpan Start);

/// <summary>PCM audio produced by synthesis, with word boundaries to know what is being read.</summary>
public sealed record SynthesizedAudio(byte[] Pcm, WaveFormat Format, IReadOnlyList<WordBoundary> Words)
{
    public TimeSpan Duration => TimeSpan.FromSeconds((double)Pcm.Length / Format.AverageBytesPerSecond);

    /// <summary>Position in the text of the word heard at the given instant.</summary>
    public int PositionAt(TimeSpan instant)
    {
        int position = 0;
        foreach (var word in Words)
        {
            if (word.Start > instant)
                break;
            position = word.Position;
        }
        return position;
    }
}

public interface ISpeechEngine
{
    EngineKind Kind { get; }

    IReadOnlyList<AvailableVoice> Voices();

    Task<SynthesizedAudio> SynthesizeAsync(string text, AvailableVoice voice, VoiceParameters parameters, CancellationToken cancel);
}
