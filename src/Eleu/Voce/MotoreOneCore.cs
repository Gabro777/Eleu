using NAudio.Wave;
using Windows.Media.Core;
using Windows.Media.SpeechSynthesis;

namespace Eleu.Voce;

/// <summary>Le voci moderne incluse in Windows 10 e 11 (per l'italiano: Elsa e Cosimo).</summary>
public sealed class MotoreOneCore : IMotoreSintesi
{
    public TipoMotore Tipo => TipoMotore.OneCore;

    public IReadOnlyList<VoceDisponibile> Voci()
    {
        try
        {
            return SpeechSynthesizer.AllVoices
                .Select(v => new VoceDisponibile($"onecore:{v.Id}", v.DisplayName, v.Language, TipoMotore.OneCore))
                .ToList();
        }
        catch (Exception e) when (e is System.Runtime.InteropServices.COMException or TypeLoadException)
        {
            // Su sistemi senza le voci OneCore (es. alcune edizioni server) si usano solo quelle SAPI.
            return [];
        }
    }

    public async Task<AudioSintetizzato> SintetizzaAsync(string testo, VoceDisponibile voce, ParametriVoce parametri, CancellationToken annulla)
    {
        var idWindows = voce.Id["onecore:".Length..];
        using var sintetizzatore = new SpeechSynthesizer
        {
            Voice = SpeechSynthesizer.AllVoices.First(v => v.Id == idWindows),
        };
        sintetizzatore.Options.SpeakingRate = Math.Clamp(parametri.Velocita / 100.0, 0.5, 6.0);
        sintetizzatore.Options.AudioVolume = Math.Clamp(parametri.Volume / 100.0, 0.0, 1.0);
        sintetizzatore.Options.IncludeWordBoundaryMetadata = true;

        using var flusso = await sintetizzatore.SynthesizeTextToStreamAsync(testo).AsTask(annulla);

        var parole = flusso.TimedMetadataTracks
            .SelectMany(traccia => traccia.Cues)
            .OfType<SpeechCue>()
            .Where(c => c.StartPositionInInput is not null)
            .Select(c => new ConfineParola(c.StartPositionInInput!.Value, c.StartTime))
            .OrderBy(c => c.Inizio)
            .ToList();

        using var wav = new MemoryStream();
        await flusso.AsStreamForRead().CopyToAsync(wav, annulla);
        wav.Position = 0;
        using var lettore = new WaveFileReader(wav);
        using var pcm = new MemoryStream();
        await lettore.CopyToAsync(pcm, annulla);
        return new AudioSintetizzato(pcm.ToArray(), lettore.WaveFormat, parole);
    }
}
