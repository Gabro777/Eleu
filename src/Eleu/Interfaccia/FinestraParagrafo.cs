using Eleu.Documento;
using Eleu.Editor;

namespace Eleu.Interfaccia;

/// <summary>
/// Formato > Paragrafo: allineamento, livello di struttura (testo o titolo), rientri, interlinea e spazi.
/// I valori mostrano l'unità: rientri e spazi in centimetri, interlinea in percentuale.
/// La prima linea negativa è sporgente.
/// </summary>
public sealed class FinestraParagrafo : Modulo
{
    public const float PuntiPerCentimetro = 72f / 2.54f;

    private static readonly string[] Allineamenti = ["A sinistra", "Centrato", "A destra", "Giustificato"];
    private static readonly string[] Livelli =
        ["Corpo del testo", .. Enumerable.Range(1, 9).Select(n => $"Titolo {n}")];

    private readonly ComboBox _allineamento, _livello;
    private readonly NumeroConUnita _sinistro, _destro, _primaLinea, _interlinea, _prima, _dopo;
    private readonly CheckBox _predefinito;
    private readonly int _livelloIniziale;
    private readonly HashSet<Control> _cambiati = [];

    public FinestraParagrafo(FormatoParagrafo attuale, int livelloTitolo = 0) : base("Paragrafo", 220)
    {
        _livelloIniziale = livelloTitolo;
        _allineamento = Campo("&Allineamento paragrafo:", Scelta(Allineamenti, attuale.Allineamento is Allineamento a ? (int)a : -1));
        _livello = Campo("&Livello struttura:", Scelta(Livelli, livelloTitolo));
        _sinistro = Campo("A &sinistra:", Centimetri(attuale.RientroSinistro, 0));
        _destro = Campo("A &destra:", Centimetri(attuale.RientroDestro, 0));
        _primaLinea = Campo("&Prima linea:", Centimetri(attuale.RientroPrimaRiga, -10));
        _interlinea = Campo("&Interlinea:", new NumeroConUnita("%", (decimal)Math.Round((attuale.Interlinea ?? 1f) * 100), 50, 400, 5, 0));
        _prima = Campo("Spazio p&rima:", Centimetri(attuale.SpazioPrima, 0));
        _dopo = Campo("Spazio d&opo:", Centimetri(attuale.SpazioDopo, 0));

        foreach (var numero in new NumericUpDown[] { _sinistro, _destro, _primaLinea, _interlinea, _prima, _dopo })
            numero.ValueChanged += (_, _) => _cambiati.Add(numero);
        foreach (var scelta in new[] { _allineamento, _livello })
            scelta.SelectedIndexChanged += (_, _) => _cambiati.Add(scelta);

        _predefinito = CasellaPredefinito(false);
        AggiungiPulsanti();
        ActiveControl = _allineamento;
    }

    /// <summary>Se usare questi rientri, spazi e interlinea anche per i documenti nuovi.</summary>
    public bool Predefinito => _predefinito.Checked;

    /// <summary>Il livello di struttura scelto, se cambiato: 0 = corpo del testo, 1-9 = titolo.</summary>
    public int? LivelloTitolo => _cambiati.Contains(_livello) && _livello.SelectedIndex != _livelloIniziale ? _livello.SelectedIndex : null;

    /// <summary>Solo le proprietà cambiate dall'utente; le altre restano null ("non cambiare").</summary>
    public FormatoParagrafo Modifiche => new(
        Cambiato(_allineamento) && _allineamento.SelectedIndex >= 0 ? (Allineamento)_allineamento.SelectedIndex : null,
        Cambiato(_sinistro) ? Punti(_sinistro) : null,
        Cambiato(_destro) ? Punti(_destro) : null,
        Cambiato(_primaLinea) ? Punti(_primaLinea) : null,
        Cambiato(_prima) ? Punti(_prima) : null,
        Cambiato(_dopo) ? Punti(_dopo) : null,
        Cambiato(_interlinea) ? (float)_interlinea.Value / 100f : null);

    /// <summary>Tutti i valori mostrati, per "Predefinito".</summary>
    public FormatoParagrafo Tutto => new(
        _allineamento.SelectedIndex >= 0 ? (Allineamento)_allineamento.SelectedIndex : Allineamento.Sinistra,
        Punti(_sinistro), Punti(_destro), Punti(_primaLinea), Punti(_prima), Punti(_dopo), (float)_interlinea.Value / 100f);

    private bool Cambiato(Control controllo) => _cambiati.Contains(controllo);

    private static NumeroConUnita Centimetri(float? punti, decimal minimo) =>
        new("cm", (decimal)Math.Round((punti ?? 0) / PuntiPerCentimetro, 2), minimo, 20, 0.1m, 2);

    private static float Punti(NumericUpDown campo) => (float)campo.Value * PuntiPerCentimetro;
}
