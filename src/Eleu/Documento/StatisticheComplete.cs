using System.Globalization;

namespace Eleu.Documento;

/// <summary>
/// Statistiche complete del testo (Strumenti > Informazioni > Statistiche complete) e l'indice di leggibilità
/// Gulpease, pensato per l'italiano: 89 + (300 × frasi − 10 × lettere) / parole. Più è alto, più il testo è
/// facile; sotto 80 è difficile per chi ha la licenza elementare, sotto 60 per la media, sotto 40 per la superiore.
/// </summary>
public sealed record StatisticheComplete(
    int Caratteri, int CaratteriSenzaSpazi, int Lettere, int Cifre, int Spazi, int AltriSegni,
    int Parole, int Frasi, int Paragrafi, double? Gulpease)
{
    public static StatisticheComplete Calcola(string testo)
    {
        int caratteri = 0, lettere = 0, cifre = 0, spazi = 0, altri = 0, parole = 0, frasi = 0;
        bool inParola = false, fraseAperta = false;
        foreach (char c in testo)
        {
            if (c is '\n' or '\r' or '\f' || StrutturaTesto.Tecnico(c) || Segnaposto(c))
            {
                inParola = false;
                continue;
            }
            caratteri++;
            if (char.IsLetter(c))
                lettere++;
            else if (char.IsDigit(c))
                cifre++;
            else if (char.IsWhiteSpace(c))
                spazi++;
            else
                altri++;

            if (char.IsLetterOrDigit(c))
            {
                if (!inParola)
                    parole++;
                inParola = true;
                fraseAperta = true;
            }
            else if (c is '\'' or '’' && inParola)
            {
                inParola = false; // "l'acqua" sono due parole
            }
            else
            {
                inParola = false;
                if (c is '.' or '!' or '?' && fraseAperta)
                {
                    frasi++;
                    fraseAperta = false; // "..." o "?!" chiudono una frase sola
                }
            }
        }
        if (fraseAperta)
            frasi++; // l'ultima frase senza punto
        int paragrafi = StrutturaTesto.Paragrafi(testo).Count(p => testo[p.Inizio..p.Fine].Any(char.IsLetterOrDigit));
        double? gulpease = parole > 0 ? Math.Clamp(89 + (300.0 * frasi - 10.0 * lettere) / parole, 0, 100) : null;
        return new StatisticheComplete(caratteri, caratteri - spazi, lettere, cifre, spazi, altri, parole, frasi, paragrafi, gulpease);
    }

    private static bool Segnaposto(char c) => c >= 0xE000 && c <= 0xF8FF; // segnalibri, note e immagini

    public string Giudizio => Gulpease switch
    {
        null => "testo vuoto",
        >= 80 => "molto facile",
        >= 60 => "facile",
        >= 40 => "difficile",
        _ => "molto difficile",
    };

    public IReadOnlyList<string> Righe()
    {
        var it = CultureInfo.GetCultureInfo("it-IT");
        string Media(double valore) => valore.ToString("0.#", it);
        var righe = new List<string>
        {
            $"Parole: {Parole}",
            $"Frasi: {Frasi}",
            $"Paragrafi: {Paragrafi}",
            $"Caratteri, spazi compresi: {Caratteri}",
            $"Caratteri senza spazi: {CaratteriSenzaSpazi}",
            $"Lettere: {Lettere}",
            $"Cifre: {Cifre}",
            $"Spazi: {Spazi}",
            $"Punteggiatura e simboli: {AltriSegni}",
        };
        if (Parole > 0)
        {
            righe.Add($"Lettere per parola, in media: {Media((double)Lettere / Parole)}");
            righe.Add($"Parole per frase, in media: {Media((double)Parole / Math.Max(1, Frasi))}");
        }
        if (Gulpease is double indice)
            righe.Add($"Indice di leggibilità Gulpease: {indice.ToString("0", it)} su 100, {Giudizio}");
        return righe;
    }
}
