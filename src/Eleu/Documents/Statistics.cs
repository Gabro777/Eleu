namespace Eleu.Documents;

public readonly record struct Statistics(int Words, int Characters, int Paragraphs)
{
    public static Statistics Compute(string text)
    {
        int words = 0;
        bool inWord = false;
        int characters = 0;
        foreach (char c in text)
        {
            // The page break and the characters that hold tables together are not characters to count.
            if (c is not ('\n' or '\r' or '\f') && !TextStructure.IsTechnical(c))
                characters++;

            if (char.IsWhiteSpace(c) || TextStructure.IsTechnical(c))
            {
                inWord = false;
            }
            else if (!inWord && char.IsLetterOrDigit(c))
            {
                words++;
                inWord = true;
            }
        }

        int paragraphs = TextStructure.Paragraphs(text).Count(p => !string.IsNullOrWhiteSpace(text[p.Start..p.End]));
        return new Statistics(words, characters, paragraphs);
    }

    public string Description() =>
        $"{Count(Words, "parola", "parole")}, {Count(Characters, "carattere", "caratteri")}, {Count(Paragraphs, "paragrafo", "paragrafi")}";

    private static string Count(int n, string singular, string plural) => $"{n} {(n == 1 ? singular : plural)}";
}
