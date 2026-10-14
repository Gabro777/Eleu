using System.Media;

namespace Eleu.Accessibility;

/// <summary>Eleu's sound cues: short, different from each other and recognizable without listening to words.</summary>
public enum Cue
{
    Heading,
    Bookmark,
    Footnote,
    PageBreak,
    Comment,
    MisspelledWord,
    EndOfReading,
    Saved,
}

/// <summary>
/// Plays the cues, generated on the fly as simple notes (no audio files to install).
/// The volume is the cue's relative to the computer's other sounds: the Windows volume is never touched.
/// </summary>
public static class Sounds
{
    private const int SampleRate = 22050;

    private static readonly Dictionary<(Cue, int), byte[]> _created = [];
    private static SoundPlayer? _inProgress; // kept alive while playing: Windows reads the data from its memory

    /// <summary>The notes of each cue: frequency in hertz and duration in milliseconds (0 Hz = pause).</summary>
    public static IReadOnlyList<(double Hz, int Ms)> Tones(Cue cue) => cue switch
    {
        Cue.Heading => [(659, 55), (988, 75)],                 // two rising notes
        Cue.Bookmark => [(1760, 45)],                       // a high tinkle
        Cue.Footnote => [(1319, 35), (0, 30), (1319, 35)],        // two equal taps
        Cue.PageBreak => [(196, 110)],               // a low knock
        Cue.Comment => [(1047, 40), (0, 25), (784, 60)],     // a high note and a lower one, detached
        Cue.MisspelledWord => [(233, 90)],                   // a low buzz
        Cue.EndOfReading => [(784, 70), (523, 110)],           // two falling notes
        Cue.Saved => [(523, 50), (659, 50), (784, 80)],     // three rising notes
        _ => [],
    };

    /// <summary>Plays the cue (right away, cutting off the previous one). Volume from 0 to 100.</summary>
    public static void Play(Cue cue, int volume)
    {
        if (volume <= 0)
            return;
        try
        {
            if (!_created.TryGetValue((cue, volume), out var wav))
                _created[(cue, volume)] = wav = Wav(Tones(cue), volume, buzz: cue == Cue.MisspelledWord);
            var reader = new SoundPlayer(new MemoryStream(wav));
            reader.Play();
            _inProgress?.Dispose();
            _inProgress = reader;
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or TimeoutException)
        {
            // Without a sound card the cues are simply not heard.
        }
    }

    /// <summary>A WAV file (16-bit mono PCM) with the given notes, each with a soft attack and tail so it does not click.</summary>
    public static byte[] Wav(IReadOnlyList<(double Hz, int Ms)> footnotes, int volume, bool buzz = false)
    {
        var samples = new List<short>();
        double amplitude = Math.Clamp(volume, 0, 100) / 100.0 * 0.45 * short.MaxValue;
        foreach (var (hz, ms) in footnotes)
        {
            int n = SampleRate * ms / 1000;
            int attack = Math.Min(n / 4, SampleRate * 4 / 1000);
            for (int i = 0; i < n; i++)
            {
                double envelope = i < attack ? (double)i / attack : Math.Exp(-3.0 * (i - attack) / Math.Max(1, n - attack));
                double t = (double)i / SampleRate;
                double wave = hz <= 0 ? 0 : Math.Sin(2 * Math.PI * hz * t) + (buzz ? 0.4 * Math.Sin(6 * Math.PI * hz * t) : 0);
                samples.Add((short)(amplitude * envelope * wave / (buzz ? 1.4 : 1)));
            }
        }

        using var stream = new MemoryStream();
        using var w = new BinaryWriter(stream);
        int data = samples.Count * 2;
        w.Write("RIFF"u8); w.Write(36 + data); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1);
        w.Write(SampleRate); w.Write(SampleRate * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8); w.Write(data);
        foreach (var c in samples)
            w.Write(c);
        w.Flush();
        return stream.ToArray();
    }
}
