using NAudio.Wave;

namespace Eleu.Voce;

public enum TipoMotore { OneCore, Sapi }

/// <summary>Una voce installata in Windows. L'Id comprende il motore, così resta univoco.</summary>
public sealed record VoceDisponibile(string Id, string Nome, string Lingua, TipoMotore Motore)
{
    public string Descrizione => $"{Nome} ({Lingua}{(Motore == TipoMotore.Sapi ? ", SAPI" : "")})";
}

/// <summary>Velocità e volume in percentuale: 100 = normale.</summary>
public sealed record ParametriVoce(int Velocita = 100, int Volume = 100)
{
    public const int VelocitaMinima = 50;
    public const int VelocitaMassima = 300;
}

/// <summary>Inizio di una parola: posizione nel testo sintetizzato e istante nell'audio.</summary>
public readonly record struct ConfineParola(int Posizione, TimeSpan Inizio);

/// <summary>Audio PCM prodotto dalla sintesi, con i confini delle parole per sapere cosa si sta leggendo.</summary>
public sealed record AudioSintetizzato(byte[] Pcm, WaveFormat Formato, IReadOnlyList<ConfineParola> Parole)
{
    public TimeSpan Durata => TimeSpan.FromSeconds((double)Pcm.Length / Formato.AverageBytesPerSecond);

    /// <summary>Posizione nel testo della parola che si sente all'istante indicato.</summary>
    public int PosizioneAl(TimeSpan istante)
    {
        int posizione = 0;
        foreach (var parola in Parole)
        {
            if (parola.Inizio > istante)
                break;
            posizione = parola.Posizione;
        }
        return posizione;
    }
}

public interface IMotoreSintesi
{
    TipoMotore Tipo { get; }

    IReadOnlyList<VoceDisponibile> Voci();

    Task<AudioSintetizzato> SintetizzaAsync(string testo, VoceDisponibile voce, ParametriVoce parametri, CancellationToken annulla);
}
