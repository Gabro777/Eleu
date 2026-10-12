using System.Speech.AudioFormat;
using System.Speech.Synthesis;
using NAudio.Wave;

namespace Eleu.Voce;

/// <summary>
/// Le voci SAPI 5: quelle classiche di Windows e quelle installate a parte
/// (molti utenti ciechi usano voci SAPI acquistate, come Loquendo o Vocalizer).
/// </summary>
public sealed class MotoreSapi : IMotoreSintesi
{
    private static readonly WaveFormat FormatoAudio = new(22050, 16, 1);

    public TipoMotore Tipo => TipoMotore.Sapi;

    public IReadOnlyList<VoceDisponibile> Voci()
    {
        using var sintetizzatore = new SpeechSynthesizer();
        return sintetizzatore.GetInstalledVoices()
            .Where(v => v.Enabled)
            .Select(v => new VoceDisponibile($"sapi:{v.VoiceInfo.Name}", v.VoiceInfo.Name, v.VoiceInfo.Culture.Name, TipoMotore.Sapi))
            .ToList();
    }

    public Task<AudioSintetizzato> SintetizzaAsync(string testo, VoceDisponibile voce, ParametriVoce parametri, CancellationToken annulla) =>
        // SAPI è sincrono: lo si fa lavorare fuori dal thread dell'interfaccia.
        Task.Run(() =>
        {
            annulla.ThrowIfCancellationRequested();
            using var sintetizzatore = new SpeechSynthesizer();
            sintetizzatore.SelectVoice(voce.Nome);
            sintetizzatore.Rate = VelocitaSapi(parametri.Velocita);
            sintetizzatore.Volume = Math.Clamp(parametri.Volume, 0, 100);

            var parole = new List<ConfineParola>();
            sintetizzatore.SpeakProgress += (_, e) => parole.Add(new ConfineParola(e.CharacterPosition, e.AudioPosition));

            using var pcm = new MemoryStream();
            sintetizzatore.SetOutputToAudioStream(pcm,
                new SpeechAudioFormatInfo(FormatoAudio.SampleRate, AudioBitsPerSample.Sixteen, AudioChannel.Mono));
            sintetizzatore.Speak(testo);
            sintetizzatore.SetOutputToNull();
            return new AudioSintetizzato(pcm.ToArray(), FormatoAudio, parole);
        }, annulla);

    /// <summary>
    /// SAPI usa una scala da -10 a 10 in cui ogni 10 passi la velocità triplica circa:
    /// si converte la percentuale di Eleu (100 = normale) su quella scala.
    /// </summary>
    public static int VelocitaSapi(int percentuale)
    {
        var moltiplicatore = Math.Clamp(percentuale, 1, 1000) / 100.0;
        return (int)Math.Clamp(Math.Round(10 * Math.Log(moltiplicatore, 3)), -10, 10);
    }
}
