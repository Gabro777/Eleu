using Eleu.Accessibilita;
using static Eleu.Tests.Supporto;

namespace Eleu.Tests;

public class SuoniTests
{
    [Fact]
    public void Wav_IntestazioneEDurataGiuste()
    {
        var wav = Suoni.Wav([(440, 100), (0, 50)], 60);
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(wav, 0, 4));
        Assert.Equal("WAVE", System.Text.Encoding.ASCII.GetString(wav, 8, 4));
        int dati = BitConverter.ToInt32(wav, 40);
        Assert.Equal((2205 + 1102) * 2, dati); // 100 + 50 ms a 22050 campioni al secondo, 16 bit
        Assert.Equal(44 + dati, wav.Length);
    }

    [Fact]
    public void Wav_VolumeZeroESilenzio_VolumePienoNo()
    {
        static short Massimo(byte[] wav) =>
            Enumerable.Range(0, (wav.Length - 44) / 2).Max(i => Math.Abs(BitConverter.ToInt16(wav, 44 + i * 2)));
        Assert.Equal(0, Massimo(Suoni.Wav([(440, 50)], 0)));
        Assert.True(Massimo(Suoni.Wav([(440, 50)], 100)) > 10000);
        // Mai a fondo scala: il segnale non deve gracchiare.
        Assert.True(Massimo(Suoni.Wav([(233, 90)], 100, ronzio: true)) < short.MaxValue);
    }

    [Fact]
    public void Segnali_TuttiDiversiTraLoro()
    {
        var note = Enum.GetValues<Segnale>().Select(s => string.Join(",", Suoni.Note(s))).ToList();
        Assert.All(note, n => Assert.NotEqual("", n));
        Assert.Equal(note.Count, note.Distinct().Count());
    }

    [Fact]
    public void Opzioni_RiportaLeSceltePerOgniCampo()
    {
        var impostazioni = new Impostazioni { SegnaleElementi = ModoSegnale.VoceESuono, VolumeSuoni = 30, RiapriUltimo = true, MinutiRipristino = 5, AnnunciaCelle = false };
        EseguiSta(() =>
        {
            using var finestra = new Interfaccia.FinestraOpzioni(impostazioni);
            finestra.Applica();
        });
        Assert.Equal(ModoSegnale.VoceESuono, impostazioni.SegnaleElementi);
        Assert.Equal(30, impostazioni.VolumeSuoni);
        Assert.True(impostazioni.RiapriUltimo);
        Assert.Equal(5, impostazioni.MinutiRipristino);
        Assert.False(impostazioni.AnnunciaCelle);
        Assert.True(impostazioni.AnnunciaImmagini);
    }
}
