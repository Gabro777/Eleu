using Eleu.Braille;

namespace Eleu;

// Six-key Braille typing (Strumenti > Scrittura Braille a 6 tasti): F D S J K L are dots 1-6.
public sealed partial class MainWindow
{
    private BrailleKeyboard? _brailleKeyboard;

    private void ToggleBrailleTyping()
    {
        if (_brailleKeyboard is null)
        {
            _brailleKeyboard = new BrailleKeyboard(BrailleTranslator.Translate);
            _announcer.Announce("Scrittura Braille attiva: F D S J K L sono i punti da 1 a 6, la barra spaziatrice è lo spazio");
        }
        else
        {
            _brailleKeyboard = null;
            _announcer.Announce("Scrittura Braille disattivata");
        }
        UpdateStatusBar();
    }

    /// <summary>
    /// In Braille typing the dot keys do not type their letter: they are collected until released.
    /// Other keys (space, arrows, Enter, delete, shortcuts with Ctrl or Alt) stay as usual.
    /// </summary>
    private void PrepareBrailleTyping()
    {
        _text.KeyDown += (_, e) =>
        {
            if (_brailleKeyboard is null)
                return;
            if (e.Modifiers != Keys.None || !BrailleKeyboard.DotKey(e.KeyCode))
            {
                // Another key (space, arrows, Enter...) closes a half-done number or sequence.
                if (e.KeyCode is not (Keys.ShiftKey or Keys.ControlKey or Keys.Menu))
                    _brailleKeyboard.Reset();
                return;
            }
            _brailleKeyboard.Press(e.KeyCode);
            e.SuppressKeyPress = true;
            e.Handled = true;
        };
        _text.KeyUp += (_, e) =>
        {
            if (_brailleKeyboard is null || !BrailleKeyboard.DotKey(e.KeyCode))
                return;
            e.Handled = true;
            if (_brailleKeyboard.Release(e.KeyCode) is not string written)
                return;
            _text.SelectedText = written;
            // The screen reader does not know what was typed (it only heard the keys): so it is said.
            _announcer.Announce(written switch
            {
                " " => "spazio",
                _ when written.All(c => c >= BrailleTranslator.EmptyCell && c <= BrailleTranslator.EmptyCell + 0xFF) => "cella senza carattere",
                _ => Proofing.Spoken.Of(written),
            });
        };
    }
}
