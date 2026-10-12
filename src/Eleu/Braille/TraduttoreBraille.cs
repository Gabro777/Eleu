using System.Runtime.InteropServices;
using System.Text;

namespace Eleu.Braille;

/// <summary>
/// Traduce il testo in Braille italiano a 6 punti con liblouis (tabella it-it-comp6),
/// la libreria standard usata anche da NVDA e JAWS. Restituisce celle Unicode (U+2800–U+283F).
/// </summary>
public static class TraduttoreBraille
{
    public const char CellaVuota = (char)0x2800;

    // Modalità di liblouis: uscita come punti (dotsIO) in caratteri Unicode Braille (ucBrl),
    // senza le sequenze di escape per i caratteri sconosciuti (noUndefined).
    private const int DotsIO = 4;
    private const int UcBrl = 64;
    private const int NoUndefined = 128;

    private static readonly Lock Blocco = new(); // liblouis non è thread-safe
    private static readonly string Tabella =
        PercorsoBreve(Path.Combine(AppContext.BaseDirectory, "liblouis", "tables", "it-it-comp6.utb"));

    public static string Traduci(string testo)
    {
        if (testo.Length == 0)
            return "";

        // In questa build di liblouis i caratteri sono a 32 bit (UTF-32).
        var ingresso = MemoryMarshal.Cast<byte, uint>(Encoding.UTF32.GetBytes(testo)).ToArray();
        int capacita = ingresso.Length * 4 + 16;

        lock (Blocco)
        {
            while (true)
            {
                var uscita = new uint[capacita];
                int lunghezzaIngresso = ingresso.Length;
                int lunghezzaUscita = uscita.Length;
                int esito = lou_translateString(Tabella, ingresso, ref lunghezzaIngresso, uscita, ref lunghezzaUscita,
                    IntPtr.Zero, IntPtr.Zero, DotsIO | UcBrl | NoUndefined);
                if (esito == 0)
                    throw new InvalidOperationException($"liblouis non è riuscito a tradurre il testo (tabella: {Tabella}).");

                // Se il buffer d'uscita non bastava, liblouis si ferma prima della fine: si riprova più grande.
                if (lunghezzaIngresso >= ingresso.Length)
                {
                    var celle = new StringBuilder(lunghezzaUscita);
                    for (int i = 0; i < lunghezzaUscita; i++)
                        celle.Append(InCella(uscita[i]));
                    return celle.ToString();
                }
                if (capacita > ingresso.Length * 64)
                    throw new InvalidOperationException("liblouis non ha tradotto tutto il testo.");
                capacita *= 2;
            }
        }
    }

    /// <summary>Riporta ogni valore nell'intervallo delle celle a 6 punti; il resto diventa una cella vuota.</summary>
    private static char InCella(uint valore)
    {
        if (valore is >= 0x2800 and <= 0x28FF)
            return (char)(0x2800 | (valore & 0x3F));
        if (valore is >= 0x8000 and <= 0x80FF) // punti senza Unicode: 0x8000 | punti
            return (char)(0x2800 | (valore & 0x3F));
        return CellaVuota;
    }

    /// <summary>
    /// liblouis apre le tabelle con la codifica ANSI di Windows: se Eleu è in una cartella con lettere
    /// accentate (es. C:\Users\Niccolò) il percorso non verrebbe trovato. Il percorso breve 8.3 è sempre ASCII.
    /// </summary>
    private static string PercorsoBreve(string percorso)
    {
        var breve = new StringBuilder(1024);
        return GetShortPathName(percorso, breve, breve.Capacity) > 0 ? breve.ToString() : percorso;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetShortPathNameW")]
    private static extern int GetShortPathName(string lungo, StringBuilder breve, int capacita);

    [DllImport("liblouis", CharSet = CharSet.Ansi)]
    private static extern int lou_translateString(string tabelle, uint[] ingresso, ref int lunghezzaIngresso,
        [Out] uint[] uscita, ref int lunghezzaUscita, IntPtr tipoCarattere, IntPtr spaziatura, int modalita);
}
