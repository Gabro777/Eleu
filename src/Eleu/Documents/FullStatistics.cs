using System.Globalization;

namespace Eleu.Documents;

/// <summary>
/// Full text statistics (Strumenti > Informazioni > Statistiche complete) and the Gulpease readability
/// index, designed for Italian: 89 + (300 × sentences − 10 × letters) / words. The higher it is, the easier
/// the text; below 80 it is hard for people with primary schooling, below 60 for middle school, below 40 for high school.
/// </summary>
public sealed record FullStatistics(
    int Characters, int CharactersWithoutSpaces, int Letters, int Digits, int Spaces, int OtherMarks,
    int Words, int Sentences, int Paragraphs, double? Gulpease)
{
    public static FullStatistics Compute(string text)
    {
        int characters = 0, letters = 0, digits = 0, spaces = 0, others = 0, words = 0, sentences = 0;
        bool inWord = false, openSentence = false;
        foreach (char c in text)
        {
            if (c is '\n' or '\r' or '\f' || TextStructure.IsTechnical(c) || Placeholder(c))
            {
                inWord = false;
                continue;
            }
            characters++;
            if (char.IsLetter(c))
                letters++;
            else if (char.IsDigit(c))
                digits++;
            else if (char.IsWhiteSpace(c))
                spaces++;
            else
                others++;

            if (char.IsLetterOrDigit(c))
            {
                if (!inWord)
                    words++;
                inWord = true;
                openSentence = true;
            }
            else if (c is '\'' or '’' && inWord)
            {
                inWord = false; // "l'acqua" is two words
            }
            else
            {
                inWord = false;
                if (c is '.' or '!' or '?' && openSentence)
                {
                    sentences++;
                    openSentence = false; // "..." or "?!" end a single sentence
                }
            }
        }
        if (openSentence)
            sentences++; // the last sentence without a period
        int paragraphs = TextStructure.Paragraphs(text).Count(p => text[p.Start..p.End].Any(char.IsLetterOrDigit));
        double? gulpease = words > 0 ? Math.Clamp(89 + (300.0 * sentences - 10.0 * letters) / words, 0, 100) : null;
        return new FullStatistics(characters, characters - spaces, letters, digits, spaces, others, words, sentences, paragraphs, gulpease);
    }

    private static bool Placeholder(char c) => c >= 0xE000 && c <= 0xF8FF; // bookmarks, footnotes and images

    public string Rating => Gulpease switch
    {
        null => "testo vuoto",
        >= 80 => "molto facile",
        >= 60 => "facile",
        >= 40 => "difficile",
        _ => "molto difficile",
    };

    public IReadOnlyList<string> Lines()
    {
        var it = CultureInfo.GetCultureInfo("it-IT");
        string Media(double @value) => @value.ToString("0.#", it);
        var rows = new List<string>
        {
            $"Parole: {Words}",
            $"Frasi: {Sentences}",
            $"Paragrafi: {Paragraphs}",
            $"Caratteri, spazi compresi: {Characters}",
            $"Caratteri senza spazi: {CharactersWithoutSpaces}",
            $"Lettere: {Letters}",
            $"Cifre: {Digits}",
            $"Spazi: {Spaces}",
            $"Punteggiatura e simboli: {OtherMarks}",
        };
        if (Words > 0)
        {
            rows.Add($"Lettere per parola, in media: {Media((double)Letters / Words)}");
            rows.Add($"Parole per frase, in media: {Media((double)Words / Math.Max(1, Sentences))}");
        }
        if (Gulpease is double index)
            rows.Add($"Indice di leggibilità Gulpease: {index.ToString("0", it)} su 100, {Rating}");
        return rows;
    }
}
