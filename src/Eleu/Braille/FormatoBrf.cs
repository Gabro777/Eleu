using System.Text;

namespace Eleu.Braille;

/// <summary>
/// File BRF: il formato di testo Braille accettato da quasi tutte le stampanti Braille
/// e dai programmi di stampa. Ogni cella diventa un carattere ASCII (codifica "North American
/// ASCII Braille"), le righe finiscono con CR LF e le pagine con un salto pagina.
/// </summary>
public static class FormatoBrf
{
    // Carattere ASCII per ciascuna delle 64 celle a 6 punti, in ordine di valore dei punti (punto 1 = bit 0).
    public const string TabellaAscii = @" A1B'K2L@CIF/MSP""E3H9O6R^DJG>NTQ,*5<-U8V.%[$+X!&;:4\0Z7(_?W]#Y)=";

    public static char InAscii(char cella) =>
        cella >= TraduttoreBraille.CellaVuota && cella - TraduttoreBraille.CellaVuota < TabellaAscii.Length ? TabellaAscii[cella - TraduttoreBraille.CellaVuota] : ' ';

    public static string Testo(IReadOnlyList<List<string>> pagine)
    {
        var brf = new StringBuilder();
        foreach (var pagina in pagine)
        {
            foreach (var riga in pagina)
            {
                foreach (char cella in riga)
                    brf.Append(InAscii(cella));
                brf.Append("\r\n");
            }
            brf.Append('\f');
        }
        return brf.ToString();
    }

    public static byte[] Byte(IReadOnlyList<List<string>> pagine) => Encoding.ASCII.GetBytes(Testo(pagine));
}
