using NAudio.Wave;

namespace Eleu.Voce;

/// <summary>
/// Legge ad alta voce una sequenza di brani. Mentre suona un brano prepara già il successivo,
/// così tra un paragrafo e l'altro non ci sono attese. Sa in ogni momento quale parola si sta sentendo.
/// </summary>
public sealed class LettoreVocale : IDisposable
{
    private readonly Func<string, CancellationToken, Task<AudioSintetizzato>> _sintetizza;
    private CancellationTokenSource? _annulla;
    private WaveOut? _uscita;
    private Brano? _branoCorrente;
    private AudioSintetizzato? _audioCorrente;

    public LettoreVocale(Func<string, CancellationToken, Task<AudioSintetizzato>> sintetizza) => _sintetizza = sintetizza;

    /// <summary>Lettura arrivata alla fine (non chiamato se fermata).</summary>
    public event EventHandler? Terminato;

    /// <summary>Errore della sintesi o dell'audio: la lettura si interrompe.</summary>
    public event EventHandler<Exception>? Errore;

    public bool InLettura => _annulla is not null;

    public bool InPausa => _uscita?.PlaybackState == PlaybackState.Paused;

    /// <summary>Posizione nel documento della parola che si sta sentendo (o dell'ultima sentita).</summary>
    public int? PosizioneCorrente
    {
        get
        {
            if (_branoCorrente is null || _audioCorrente is null || _uscita is null)
                return _branoCorrente?.Inizio;
            var byteSuonati = _uscita.GetPosition();
            var istante = TimeSpan.FromSeconds((double)byteSuonati / _uscita.OutputWaveFormat.AverageBytesPerSecond);
            return _branoCorrente.Inizio + _audioCorrente.PosizioneAl(istante);
        }
    }

    public void Avvia(IReadOnlyList<Brano> brani)
    {
        Ferma();
        if (brani.Count == 0)
            return;
        _annulla = new CancellationTokenSource();
        _ = LeggiAsync(brani, _annulla.Token);
    }

    public void Pausa() => _uscita?.Pause();

    public void Riprendi()
    {
        if (InPausa)
            _uscita!.Play();
    }

    public void Ferma()
    {
        _annulla?.Cancel();
        _annulla = null;
        _uscita?.Stop();
    }

    private async Task LeggiAsync(IReadOnlyList<Brano> brani, CancellationToken annulla)
    {
        try
        {
            Task<AudioSintetizzato> prossimo = _sintetizza(brani[0].Testo, annulla);
            for (int i = 0; i < brani.Count; i++)
            {
                var audio = await prossimo;
                annulla.ThrowIfCancellationRequested();
                if (i + 1 < brani.Count)
                    prossimo = _sintetizza(brani[i + 1].Testo, annulla);

                _branoCorrente = brani[i];
                _audioCorrente = audio;
                await SuonaAsync(audio, annulla);
            }
            _annulla = null;
            Terminato?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException)
        {
            // Lettura fermata dall'utente.
        }
        catch (Exception e)
        {
            _annulla = null;
            Errore?.Invoke(this, e);
        }
    }

    private async Task SuonaAsync(AudioSintetizzato audio, CancellationToken annulla)
    {
        var finito = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var uscita = new WaveOut();
        using var sorgente = new RawSourceWaveStream(audio.Pcm, 0, audio.Pcm.Length, audio.Formato);
        uscita.PlaybackStopped += (_, _) => finito.TrySetResult();
        uscita.Init(sorgente);
        var vecchia = _uscita;
        _uscita = uscita;
        vecchia?.Dispose();

        using (annulla.Register(() => uscita.Stop()))
        {
            uscita.Play();
            await finito.Task;
        }
        annulla.ThrowIfCancellationRequested();
    }

    public void Dispose()
    {
        Ferma();
        _uscita?.Dispose();
        _uscita = null;
    }
}
