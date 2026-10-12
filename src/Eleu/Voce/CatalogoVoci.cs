namespace Eleu.Voce;

/// <summary>Tutte le voci disponibili, dai due motori, e la sintesi con quella scelta.</summary>
public sealed class CatalogoVoci(params IMotoreSintesi[] motori)
{
    private IReadOnlyList<VoceDisponibile>? _voci;

    public static CatalogoVoci Predefinito() => new(new MotoreOneCore(), new MotoreSapi());

    public IReadOnlyList<VoceDisponibile> Voci => _voci ??= motori.SelectMany(m => m.Voci()).ToList();

    /// <summary>
    /// La voce salvata nelle impostazioni se c'è ancora, altrimenti la prima italiana
    /// (preferendo le OneCore, più naturali), altrimenti la prima disponibile.
    /// </summary>
    public VoceDisponibile? Scegli(string? idPreferito) =>
        Voci.FirstOrDefault(v => v.Id == idPreferito)
        ?? Voci.Where(v => v.Lingua.StartsWith("it", StringComparison.OrdinalIgnoreCase)).OrderBy(v => v.Motore).FirstOrDefault()
        ?? Voci.FirstOrDefault();

    /// <summary>Il dizionario delle pronunce, applicato a tutto ciò che si legge (lettura e audiolibri).</summary>
    public IReadOnlyList<Pronuncia> Pronunce { get; set; } = [];

    public async Task<AudioSintetizzato> SintetizzaAsync(string testo, VoceDisponibile voce, ParametriVoce parametri, CancellationToken annulla)
    {
        var (detto, origine) = Voce.Pronunce.Applica(testo, Pronunce);
        var audio = await motori.First(m => m.Tipo == voce.Motore).SintetizzaAsync(detto, voce, parametri, annulla);
        if (ReferenceEquals(detto, testo) || detto == testo)
            return audio;
        // Le parole si riportano alle posizioni del testo scritto, così il cursore si ferma dove deve.
        return audio with { Parole = audio.Parole.Select(p => p with { Posizione = origine[Math.Clamp(p.Posizione, 0, origine.Length - 1)] }).ToList() };
    }
}
