namespace Eleu.Braille;

/// <summary>
/// Scrittura Braille dalla tastiera, come con una dattilobraille: i tasti F D S sono i punti 1 2 3, J K L i punti
/// 4 5 6. Si premono insieme i tasti della cella e, quando si lasciano tutti, la cella diventa il carattere che le
/// corrisponde nel Braille italiano. I segni fatti di più celle (per esempio il segno di maiuscola prima della
/// lettera) si scrivono cella dopo cella: la tastiera aspetta finché la sequenza non è completa.
/// </summary>
public sealed class TastieraBraille
{
    private static readonly Dictionary<Keys, int> Punti = new()
    {
        [Keys.F] = 1, [Keys.D] = 2, [Keys.S] = 3, [Keys.J] = 4, [Keys.K] = 5, [Keys.L] = 6,
    };

    private readonly Dictionary<string, string> _caratteri = [];
    private readonly HashSet<string> _prefissi = [];
    private readonly HashSet<Keys> _premuti = [];
    private int _cella;
    private string _sequenza = "";

    // Numeri: il segno di numero, poi le celle dalla "a" alla "j" valgono le cifre finché non arriva altro.
    private readonly string? _segnoNumero;
    private readonly Dictionary<string, string> _cifre = [];
    private bool _numeri, _appenaSegno;

    /// <param name="traduci">La traduzione in Braille del testo (liblouis), da cui si ricava cella → carattere.</param>
    public TastieraBraille(Func<string, string> traduci)
    {
        // Le minuscole e i segni per primi: se due caratteri hanno le stesse celle vince il più comune.
        var candidati = "abcdefghijklmnopqrstuvwxyzàèéìòùç.,;:!?'\"()-/*+=<>@#%&_0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZÀÈÉÌÒÙ";
        foreach (var c in candidati)
        {
            string celle;
            try
            {
                celle = traduci(c.ToString());
            }
            catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or InvalidOperationException)
            {
                return;
            }
            if (celle.Length == 0 || _caratteri.ContainsKey(celle))
                continue;
            _caratteri[celle] = c.ToString();
            for (int i = 1; i < celle.Length; i++)
                _prefissi.Add(celle[..i]);
        }
        var uno = traduci("1");
        if (uno.Length == 2)
        {
            _segnoNumero = uno[..1];
            for (char cifra = '0'; cifra <= '9'; cifra++)
                if (traduci(cifra.ToString()) is { Length: 2 } celle && celle[..1] == _segnoNumero)
                    _cifre[celle[1..]] = cifra.ToString();
        }
    }

    public static bool TastoDiPunto(Keys tasto) => Punti.ContainsKey(tasto);

    /// <summary>Un tasto di punto premuto: si aggiunge il suo punto alla cella in corso.</summary>
    public void Premi(Keys tasto)
    {
        if (Punti.TryGetValue(tasto, out int punto))
        {
            _premuti.Add(tasto);
            _cella |= 1 << (punto - 1);
        }
    }

    /// <summary>
    /// Un tasto di punto lasciato: quando sono lasciati tutti, la cella è fatta. Restituisce il testo da scrivere
    /// (il carattere, o le celle Braille se non corrispondono a nulla) oppure null se non c'è ancora niente.
    /// </summary>
    public string? Rilascia(Keys tasto)
    {
        _premuti.Remove(tasto);
        if (_premuti.Count > 0 || _cella == 0)
            return null;
        var cella = ((char)(TraduttoreBraille.CellaVuota + _cella)).ToString();
        _cella = 0;

        if (_numeri)
        {
            if (_cifre.TryGetValue(cella, out var cifra))
            {
                _appenaSegno = false;
                return cifra;
            }
            // Finito il numero; se il segno non era seguito da cifre era un "#".
            var prima = _appenaSegno ? _caratteri.GetValueOrDefault(_segnoNumero!, "") : "";
            _numeri = _appenaSegno = false;
            return prima + (Cella(cella) ?? "");
        }
        if (cella == _segnoNumero && _sequenza.Length == 0)
        {
            _numeri = _appenaSegno = true;
            return null;
        }
        return Cella(cella);
    }

    /// <summary>Una cella fuori dai numeri: un carattere, l'inizio di una sequenza, o la cella stessa.</summary>
    private string? Cella(string cella)
    {
        _sequenza += cella;
        if (_caratteri.TryGetValue(_sequenza, out var carattere))
        {
            _sequenza = "";
            return carattere;
        }
        if (_prefissi.Contains(_sequenza))
            return null; // per esempio il segno di maiuscola: si aspetta la lettera
        // Nessun carattere: si scrive la cella Braille com'è, così non si perde niente.
        var celle = _sequenza;
        _sequenza = "";
        return celle;
    }

    /// <summary>Ciò che si sta scrivendo (una sequenza a metà), da annullare con Esc o cambiando modo.</summary>
    public void Azzera()
    {
        _premuti.Clear();
        _cella = 0;
        _sequenza = "";
        _numeri = _appenaSegno = false;
    }

    /// <summary>Se c'è una sequenza a metà (per esempio dopo il segno di maiuscola).</summary>
    public bool InAttesa => _sequenza.Length > 0;
}
