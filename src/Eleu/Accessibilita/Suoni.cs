using System.Media;

namespace Eleu.Accessibilita;

/// <summary>I segnali sonori di Eleu: brevi, diversi tra loro e riconoscibili senza dover ascoltare parole.</summary>
public enum Segnale
{
    Titolo,
    Segnalibro,
    Nota,
    InterruzionePagina,
    Commento,
    ParolaSbagliata,
    FineLettura,
    Salvato,
}

/// <summary>
/// Suona i segnali, creati al momento come note semplici (nessun file audio da installare).
/// Il volume è quello del segnale rispetto agli altri suoni del computer: il volume di Windows non si tocca.
/// </summary>
public static class Suoni
{
    private const int Campionamento = 22050;

    private static readonly Dictionary<(Segnale, int), byte[]> _creati = [];
    private static SoundPlayer? _incorso; // tenuto in vita finché suona: Windows legge i dati dalla sua memoria

    /// <summary>Le note di ogni segnale: frequenza in hertz e durata in millisecondi (0 Hz = pausa).</summary>
    public static IReadOnlyList<(double Hz, int Ms)> Note(Segnale segnale) => segnale switch
    {
        Segnale.Titolo => [(659, 55), (988, 75)],                 // due note che salgono
        Segnale.Segnalibro => [(1760, 45)],                       // un tintinnio acuto
        Segnale.Nota => [(1319, 35), (0, 30), (1319, 35)],        // due tocchi uguali
        Segnale.InterruzionePagina => [(196, 110)],               // un colpo basso
        Segnale.Commento => [(1047, 40), (0, 25), (784, 60)],     // una nota alta e una più bassa, staccate
        Segnale.ParolaSbagliata => [(233, 90)],                   // un ronzio grave
        Segnale.FineLettura => [(784, 70), (523, 110)],           // due note che scendono
        Segnale.Salvato => [(523, 50), (659, 50), (784, 80)],     // tre note che salgono
        _ => [],
    };

    /// <summary>Suona il segnale (subito, interrompendo quello precedente). Volume da 0 a 100.</summary>
    public static void Suona(Segnale segnale, int volume)
    {
        if (volume <= 0)
            return;
        try
        {
            if (!_creati.TryGetValue((segnale, volume), out var wav))
                _creati[(segnale, volume)] = wav = Wav(Note(segnale), volume, ronzio: segnale == Segnale.ParolaSbagliata);
            var lettore = new SoundPlayer(new MemoryStream(wav));
            lettore.Play();
            _incorso?.Dispose();
            _incorso = lettore;
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or TimeoutException)
        {
            // Senza scheda audio i segnali semplicemente non si sentono.
        }
    }

    /// <summary>Un file WAV (PCM 16 bit mono) con le note date, ognuna con attacco e coda morbidi per non schioccare.</summary>
    public static byte[] Wav(IReadOnlyList<(double Hz, int Ms)> note, int volume, bool ronzio = false)
    {
        var campioni = new List<short>();
        double ampiezza = Math.Clamp(volume, 0, 100) / 100.0 * 0.45 * short.MaxValue;
        foreach (var (hz, ms) in note)
        {
            int n = Campionamento * ms / 1000;
            int attacco = Math.Min(n / 4, Campionamento * 4 / 1000);
            for (int i = 0; i < n; i++)
            {
                double inviluppo = i < attacco ? (double)i / attacco : Math.Exp(-3.0 * (i - attacco) / Math.Max(1, n - attacco));
                double t = (double)i / Campionamento;
                double onda = hz <= 0 ? 0 : Math.Sin(2 * Math.PI * hz * t) + (ronzio ? 0.4 * Math.Sin(6 * Math.PI * hz * t) : 0);
                campioni.Add((short)(ampiezza * inviluppo * onda / (ronzio ? 1.4 : 1)));
            }
        }

        using var flusso = new MemoryStream();
        using var w = new BinaryWriter(flusso);
        int dati = campioni.Count * 2;
        w.Write("RIFF"u8); w.Write(36 + dati); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1);
        w.Write(Campionamento); w.Write(Campionamento * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8); w.Write(dati);
        foreach (var c in campioni)
            w.Write(c);
        w.Flush();
        return flusso.ToArray();
    }
}
