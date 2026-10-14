namespace Eleu.Braille;

/// <summary>
/// Braille typing from the keyboard, as with a Braille writer: the F D S keys are dots 1 2 3, J K L dots
/// 4 5 6. The cell's keys are pressed together and, when all are released, the cell becomes the matching
/// character in Italian Braille. Signs made of several cells (for example the capital sign before the
/// letter) are typed cell by cell: the keyboard waits until the sequence is complete.
/// </summary>
public sealed class BrailleKeyboard
{
    private static readonly Dictionary<Keys, int> Points = new()
    {
        [Keys.F] = 1, [Keys.D] = 2, [Keys.S] = 3, [Keys.J] = 4, [Keys.K] = 5, [Keys.L] = 6,
    };

    private readonly Dictionary<string, string> _characters = [];
    private readonly HashSet<string> _prefixes = [];
    private readonly HashSet<Keys> _pressed = [];
    private int _cell;
    private string _sequence = "";

    // Numbers: the number sign, then the cells from "a" to "j" are digits until something else comes.
    private readonly string? _numberSign;
    private readonly Dictionary<string, string> _digits = [];
    private bool _numbers, _justMarked;

    /// <param name="translate">The Braille translation of text (liblouis), from which cell → character is derived.</param>
    public BrailleKeyboard(Func<string, string> translate)
    {
        // Lowercase letters and signs first: if two characters have the same cells the most common wins.
        var candidates = "abcdefghijklmnopqrstuvwxyzàèéìòùç.,;:!?'\"()-/*+=<>@#%&_0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZÀÈÉÌÒÙ";
        foreach (var c in candidates)
        {
            string cells;
            try
            {
                cells = translate(c.ToString());
            }
            catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or InvalidOperationException)
            {
                return;
            }
            if (cells.Length == 0 || _characters.ContainsKey(cells))
                continue;
            _characters[cells] = c.ToString();
            for (int i = 1; i < cells.Length; i++)
                _prefixes.Add(cells[..i]);
        }
        var one = translate("1");
        if (one.Length == 2)
        {
            _numberSign = one[..1];
            for (char digit = '0'; digit <= '9'; digit++)
                if (translate(digit.ToString()) is { Length: 2 } cells && cells[..1] == _numberSign)
                    _digits[cells[1..]] = digit.ToString();
        }
    }

    public static bool DotKey(Keys key) => Points.ContainsKey(key);

    /// <summary>A dot key pressed: its dot is added to the current cell.</summary>
    public void Press(Keys key)
    {
        if (Points.TryGetValue(key, out int point))
        {
            _pressed.Add(key);
            _cell |= 1 << (point - 1);
        }
    }

    /// <summary>
    /// A dot key released: when all are released, the cell is done. Returns the text to type
    /// (the character, or the Braille cells if they match nothing) or null if there is nothing yet.
    /// </summary>
    public string? Release(Keys key)
    {
        _pressed.Remove(key);
        if (_pressed.Count > 0 || _cell == 0)
            return null;
        var cell = ((char)(BrailleTranslator.EmptyCell + _cell)).ToString();
        _cell = 0;

        if (_numbers)
        {
            if (_digits.TryGetValue(cell, out var digit))
            {
                _justMarked = false;
                return digit;
            }
            // The number is over; if the sign was not followed by digits it was a "#".
            var before = _justMarked ? _characters.GetValueOrDefault(_numberSign!, "") : "";
            _numbers = _justMarked = false;
            return before + (Cell(cell) ?? "");
        }
        if (cell == _numberSign && _sequence.Length == 0)
        {
            _numbers = _justMarked = true;
            return null;
        }
        return Cell(cell);
    }

    /// <summary>A cell outside numbers: a character, the start of a sequence, or the cell itself.</summary>
    private string? Cell(string cell)
    {
        _sequence += cell;
        if (_characters.TryGetValue(_sequence, out var character))
        {
            _sequence = "";
            return character;
        }
        if (_prefixes.Contains(_sequence))
            return null; // for example the capital sign: wait for the letter
        // No character: the Braille cell is typed as it is, so nothing is lost.
        var cells = _sequence;
        _sequence = "";
        return cells;
    }

    /// <summary>What is being typed (a half-done sequence), to cancel with Esc or by switching mode.</summary>
    public void Reset()
    {
        _pressed.Clear();
        _cell = 0;
        _sequence = "";
        _numbers = _justMarked = false;
    }

    /// <summary>Whether there is a half-done sequence (for example after the capital sign).</summary>
    public bool Pending => _sequence.Length > 0;
}
