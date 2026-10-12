using NAudio.MediaFoundation;
using NAudio.Wave;

namespace Eleu.Voce;

public sealed record AvanzamentoEsportazione(int Capitolo, int TotaleCapitoli, string Titolo);

/// <summary>
/// Crea l'audiolibro: un file MP3 per capitolo, con una breve pausa dopo il titolo e tra i paragrafi.
/// La codifica MP3 usa l'encoder incluso in Windows (Media Foundation), senza librerie esterne.
/// </summary>
public sealed class EsportatoreAudiolibro(CatalogoVoci catalogo)
{
    private static readonly WaveFormat FormatoMp3 = new(44100, 16, 1);
    private static readonly TimeSpan PausaDopoTitolo = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan PausaTraParagrafi = TimeSpan.FromMilliseconds(500);
    private const int BitrateDesiderato = 64_000;

    public static string[] NomiFile(IReadOnlyList<Capitolo> capitoli) =>
        capitoli.Select((c, i) => Brani.NomeFile(i + 1, capitoli.Count, c.Titolo)).ToArray();

    public async Task<IReadOnlyList<string>> EsportaAsync(
        IReadOnlyList<Capitolo> capitoli, string cartella, VoceDisponibile voce, ParametriVoce parametri,
        IProgress<AvanzamentoEsportazione>? avanzamento, CancellationToken annulla)
    {
        MediaFoundationApi.Startup();
        Directory.CreateDirectory(cartella);
        var nomi = NomiFile(capitoli);
        var creati = new List<string>();

        for (int i = 0; i < capitoli.Count; i++)
        {
            annulla.ThrowIfCancellationRequested();
            avanzamento?.Report(new AvanzamentoEsportazione(i + 1, capitoli.Count, capitoli[i].Titolo));

            var percorso = Path.Combine(cartella, nomi[i]);
            var temporaneo = Path.Combine(Path.GetTempPath(), $"eleu-{Guid.NewGuid():N}.wav");
            try
            {
                await ScriviWavAsync(capitoli[i], temporaneo, voce, parametri, annulla);
                await Task.Run(() => CodificaMp3(temporaneo, percorso), annulla);
            }
            finally
            {
                File.Delete(temporaneo);
            }
            creati.Add(percorso);
        }
        return creati;
    }

    private async Task ScriviWavAsync(Capitolo capitolo, string percorso, VoceDisponibile voce, ParametriVoce parametri, CancellationToken annulla)
    {
        WaveFileWriter? scrittore = null;
        try
        {
            for (int p = 0; p < capitolo.Paragrafi.Count; p++)
            {
                var audio = await catalogo.SintetizzaAsync(capitolo.Paragrafi[p], voce, parametri, annulla);
                scrittore ??= new WaveFileWriter(percorso, audio.Formato);
                scrittore.Write(audio.Pcm, 0, audio.Pcm.Length);
                var pausa = p == 0 && capitolo.IniziaConTitolo ? PausaDopoTitolo : PausaTraParagrafi;
                var silenzio = new byte[(int)(audio.Formato.AverageBytesPerSecond * pausa.TotalSeconds) / audio.Formato.BlockAlign * audio.Formato.BlockAlign];
                scrittore.Write(silenzio, 0, silenzio.Length);
            }
        }
        finally
        {
            scrittore?.Dispose();
        }
    }

    internal static void CodificaMp3(string wav, string mp3)
    {
        using var lettore = new WaveFileReader(wav);
        // L'encoder MP3 di Windows accetta solo alcune frequenze: si porta tutto a 44,1 kHz mono.
        using var ricampionato = new MediaFoundationResampler(lettore, FormatoMp3);
        var bitrate = MediaFoundationEncoder.GetEncodeBitrates(AudioSubtypes.MFAudioFormat_MP3, FormatoMp3.SampleRate, FormatoMp3.Channels)
            .OrderBy(b => Math.Abs(b - BitrateDesiderato))
            .FirstOrDefault(BitrateDesiderato);
        // Media Foundation sceglie il formato dall'estensione: il file temporaneo deve finire in .mp3.
        var temporaneo = Path.ChangeExtension(mp3, ".eleu-tmp.mp3");
        MediaFoundationEncoder.EncodeToMp3(ricampionato, temporaneo, bitrate);
        File.Move(temporaneo, mp3, overwrite: true);
    }
}
