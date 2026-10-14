using System.Text;

namespace Eleu.Proofing;

public static class Spoken
{
    /// <summary>
    /// A piece of text to read with the screen reader, with spaces and punctuation spelled out in words
    /// ("l' a" becomes "l apostrofo spazio a", " ," becomes "spazio virgola"): with normal settings the
    /// screen reader reads neither spaces nor many signs. The space between two words stays a space.
    /// </summary>
    public static string Of(string text)
    {
        if (text == " ")
            return "uno spazio";
        var parts = new List<string>();
        var word = new StringBuilder();
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (char.IsLetterOrDigit(c))
            {
                word.Append(c);
                continue;
            }
            if (word.Length > 0)
            {
                parts.Add(word.ToString());
                word.Clear();
            }
            bool betweenTwoWords = c == ' ' && i > 0 && i < text.Length - 1
                && char.IsLetterOrDigit(text[i - 1]) && char.IsLetterOrDigit(text[i + 1]);
            if (!betweenTwoWords)
                parts.Add(NameOf(c));
        }
        if (word.Length > 0)
            parts.Add(word.ToString());
        return string.Join(" ", parts);
    }

    private static string NameOf(char c) => c switch
    {
        ' ' or '\t' => "spazio",
        ',' => "virgola",
        '.' => "punto",
        ';' => "punto e virgola",
        ':' => "due punti",
        '!' => "punto esclamativo",
        '?' => "punto interrogativo",
        '(' => "parentesi aperta",
        ')' => "parentesi chiusa",
        '\'' or '’' => "apostrofo",
        '-' => "trattino",
        '"' or '«' or '»' or '“' or '”' => "virgolette",
        _ => c.ToString(),
    };
}
