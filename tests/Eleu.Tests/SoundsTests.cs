using Eleu.Accessibility;
using static Eleu.Tests.Support;

namespace Eleu.Tests;

public class SoundsTests
{
    [Fact]
    public void Wav_RightHeaderAndDuration()
    {
        var wav = Sounds.Wav([(440, 100), (0, 50)], 60);
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(wav, 0, 4));
        Assert.Equal("WAVE", System.Text.Encoding.ASCII.GetString(wav, 8, 4));
        int data = BitConverter.ToInt32(wav, 40);
        Assert.Equal((2205 + 1102) * 2, data); // 100 + 50 ms at 22050 samples per second, 16 bits
        Assert.Equal(44 + data, wav.Length);
    }

    [Fact]
    public void Wav_ZeroVolumeIsSilence_FullVolumeIsNot()
    {
        static short Max(byte[] wav) =>
            Enumerable.Range(0, (wav.Length - 44) / 2).Max(i => Math.Abs(BitConverter.ToInt16(wav, 44 + i * 2)));
        Assert.Equal(0, Max(Sounds.Wav([(440, 50)], 0)));
        Assert.True(Max(Sounds.Wav([(440, 50)], 100)) > 10000);
        // Never at full scale: the cue must not crackle.
        Assert.True(Max(Sounds.Wav([(233, 90)], 100, buzz: true)) < short.MaxValue);
    }

    [Fact]
    public void Cues_AllDifferentFromEachOther()
    {
        var footnotes = Enum.GetValues<Cue>().Select(s => string.Join(",", Sounds.Tones(s))).ToList();
        Assert.All(footnotes, n => Assert.NotEqual("", n));
        Assert.Equal(footnotes.Count, footnotes.Distinct().Count());
    }

    [Fact]
    public void Options_ReportsTheChoicesForEachField()
    {
        var settings = new Settings { ElementCue = CueMode.SpeechAndSound, SoundVolume = 30, ReopenLast = true, RecoveryMinutes = 5, AnnounceCells = false };
        RunSta(() =>
        {
            using var window = new UI.OptionsDialog(settings);
            window.Apply();
        });
        Assert.Equal(CueMode.SpeechAndSound, settings.ElementCue);
        Assert.Equal(30, settings.SoundVolume);
        Assert.True(settings.ReopenLast);
        Assert.Equal(5, settings.RecoveryMinutes);
        Assert.False(settings.AnnounceCells);
        Assert.True(settings.AnnounceImages);
    }
}
