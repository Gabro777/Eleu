using Eleu.Braille;

namespace Eleu;

// Scrittura Braille a 6 tasti (Strumenti > Scrittura Braille a 6 tasti): F D S J K L sono i punti 1-6.
public sealed partial class FinestraPrincipale
{
    private TastieraBraille? _tastieraBraille;

    private void CambiaScritturaBraille()
    {
        if (_tastieraBraille is null)
        {
            _tastieraBraille = new TastieraBraille(TraduttoreBraille.Traduci);
            _annunciatore.Annuncia("Scrittura Braille attiva: F D S J K L sono i punti da 1 a 6, la barra spaziatrice è lo spazio");
        }
        else
        {
            _tastieraBraille = null;
            _annunciatore.Annuncia("Scrittura Braille disattivata");
        }
        AggiornaBarraDiStato();
    }

    /// <summary>
    /// In scrittura Braille i tasti dei punti non scrivono la loro lettera: si raccolgono finché non si lasciano.
    /// Gli altri tasti (spazio, frecce, Invio, cancella, scorciatoie con Ctrl o Alt) restano quelli di sempre.
    /// </summary>
    private void PreparaScritturaBraille()
    {
        _testo.KeyDown += (_, e) =>
        {
            if (_tastieraBraille is null)
                return;
            if (e.Modifiers != Keys.None || !TastieraBraille.TastoDiPunto(e.KeyCode))
            {
                // Un altro tasto (spazio, frecce, Invio...) chiude un numero o una sequenza a metà.
                if (e.KeyCode is not (Keys.ShiftKey or Keys.ControlKey or Keys.Menu))
                    _tastieraBraille.Azzera();
                return;
            }
            _tastieraBraille.Premi(e.KeyCode);
            e.SuppressKeyPress = true;
            e.Handled = true;
        };
        _testo.KeyUp += (_, e) =>
        {
            if (_tastieraBraille is null || !TastieraBraille.TastoDiPunto(e.KeyCode))
                return;
            e.Handled = true;
            if (_tastieraBraille.Rilascia(e.KeyCode) is not string scritto)
                return;
            _testo.SelectedText = scritto;
            // Lo screen reader non sa cosa è stato scritto (ha sentito solo i tasti): lo si dice.
            _annunciatore.Annuncia(scritto switch
            {
                " " => "spazio",
                _ when scritto.All(c => c >= TraduttoreBraille.CellaVuota && c <= TraduttoreBraille.CellaVuota + 0xFF) => "cella senza carattere",
                _ => Correzione.Parlato.Di(scritto),
            });
        };
    }
}
