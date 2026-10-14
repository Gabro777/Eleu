namespace Eleu.Speech;

/// <summary>All the available voices, from both engines, and synthesis with the chosen one.</summary>
public sealed class VoiceCatalog(params ISpeechEngine[] engines)
{
    private IReadOnlyList<AvailableVoice>? _items;

    public static VoiceCatalog Default() => new(new OneCoreEngine(), new SapiEngine());

    public IReadOnlyList<AvailableVoice> Voices => _items ??= engines.SelectMany(m => m.Voices()).ToList();

    /// <summary>
    /// The voice saved in the settings if it still exists, otherwise the first Italian one
    /// (preferring OneCore ones, more natural), otherwise the first available.
    /// </summary>
    public AvailableVoice? Choose(string? preferredId) =>
        Voices.FirstOrDefault(v => v.Id == preferredId)
        ?? Voices.Where(v => v.Language.StartsWith("it", StringComparison.OrdinalIgnoreCase)).OrderBy(v => v.Engine).FirstOrDefault()
        ?? Voices.FirstOrDefault();

    /// <summary>The pronunciation dictionary, applied to everything that is read (reading and audiobooks).</summary>
    public IReadOnlyList<Pronunciation> Pronunciations { get; set; } = [];

    public async Task<SynthesizedAudio> SynthesizeAsync(string text, AvailableVoice voice, VoiceParameters parameters, CancellationToken cancel)
    {
        var (spoken, source) = Speech.Pronunciations.Apply(text, Pronunciations);
        var audio = await engines.First(m => m.Kind == voice.Engine).SynthesizeAsync(spoken, voice, parameters, cancel);
        if (ReferenceEquals(spoken, text) || spoken == text)
            return audio;
        // Words are mapped back to positions in the written text, so the cursor stops where it should.
        return audio with { Words = audio.Words.Select(p => p with { Position = source[Math.Clamp(p.Position, 0, source.Length - 1)] }).ToList() };
    }
}
