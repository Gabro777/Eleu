using NAudio.Wave;

namespace Eleu.Speech;

/// <summary>
/// Reads a sequence of passages aloud. While a passage plays it already prepares the next one,
/// so there are no waits between paragraphs. It knows at every moment which word is being heard.
/// </summary>
public sealed class SpeechReader : IDisposable
{
    private readonly Func<string, CancellationToken, Task<SynthesizedAudio>> _synthesize;
    private CancellationTokenSource? _cancel;
    private WaveOut? _output;
    private Passage? _currentPassage;
    private SynthesizedAudio? _currentAudio;

    public SpeechReader(Func<string, CancellationToken, Task<SynthesizedAudio>> synthesize) => _synthesize = synthesize;

    /// <summary>Reading reached the end (not raised if stopped).</summary>
    public event EventHandler? Finished;

    /// <summary>Synthesis or audio error: reading stops.</summary>
    public event EventHandler<Exception>? Error;

    public bool IsReading => _cancel is not null;

    public bool IsPaused => _output?.PlaybackState == PlaybackState.Paused;

    /// <summary>Position in the document of the word being heard (or of the last one heard).</summary>
    public int? CurrentPosition
    {
        get
        {
            if (_currentPassage is null || _currentAudio is null || _output is null)
                return _currentPassage?.Start;
            var playedBytes = _output.GetPosition();
            var instant = TimeSpan.FromSeconds((double)playedBytes / _output.OutputWaveFormat.AverageBytesPerSecond);
            return _currentPassage.Start + _currentAudio.PositionAt(instant);
        }
    }

    public void Start(IReadOnlyList<Passage> passages)
    {
        Stop();
        if (passages.Count == 0)
            return;
        _cancel = new CancellationTokenSource();
        _ = ReadAsync(passages, _cancel.Token);
    }

    public void Pause() => _output?.Pause();

    public void Resume()
    {
        if (IsPaused)
            _output!.Play();
    }

    public void Stop()
    {
        _cancel?.Cancel();
        _cancel = null;
        _output?.Stop();
    }

    private async Task ReadAsync(IReadOnlyList<Passage> passages, CancellationToken cancel)
    {
        try
        {
            Task<SynthesizedAudio> next = _synthesize(passages[0].Text, cancel);
            for (int i = 0; i < passages.Count; i++)
            {
                var audio = await next;
                cancel.ThrowIfCancellationRequested();
                if (i + 1 < passages.Count)
                    next = _synthesize(passages[i + 1].Text, cancel);

                _currentPassage = passages[i];
                _currentAudio = audio;
                await PlayAsync(audio, cancel);
            }
            _cancel = null;
            Finished?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException)
        {
            // Reading stopped by the user.
        }
        catch (Exception e)
        {
            _cancel = null;
            Error?.Invoke(this, e);
        }
    }

    private async Task PlayAsync(SynthesizedAudio audio, CancellationToken cancel)
    {
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var output = new WaveOut();
        using var source = new RawSourceWaveStream(audio.Pcm, 0, audio.Pcm.Length, audio.Format);
        output.PlaybackStopped += (_, _) => finished.TrySetResult();
        output.Init(source);
        var old = _output;
        _output = output;
        old?.Dispose();

        using (cancel.Register(() => output.Stop()))
        {
            output.Play();
            await finished.Task;
        }
        cancel.ThrowIfCancellationRequested();
    }

    public void Dispose()
    {
        Stop();
        _output?.Dispose();
        _output = null;
    }
}
