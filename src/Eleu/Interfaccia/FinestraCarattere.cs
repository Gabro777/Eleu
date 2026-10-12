using Eleu.Documento;
using Eleu.Editor;

namespace Eleu.Interfaccia;

/// <summary>
/// Formato > Carattere: carattere, stile, dimensione, effetti, posizione, colori e spaziatura in una sola finestra.
/// Applica solo ciò che si cambia, così una selezione con formati misti non viene appiattita.
/// </summary>
public sealed class FinestraCarattere : Modulo
{
    private const float MillimetriPerPunto = 25.4f / 72f;
    private static readonly string[] Stili = ["Normale", "Corsivo", "Grassetto", "Grassetto corsivo"];
    private static readonly string[] Posizioni = ["Normale", "Apice", "Pedice"];

    private readonly FormatoCarattere _iniziale;
    private readonly IReadOnlyList<string> _caratteri;
    private readonly ComboBox _tipo, _stile, _posizione, _colore, _sfondo;
    private readonly NumeroConUnita _dimensione, _spaziatura;
    private readonly CheckBox _sottolineato, _barrato, _predefinito;
    private readonly Label _contrasto, _anteprima;
    private readonly List<ColoreConNome> _coloriTesto, _coloriSfondo;
    private readonly int _stileIniziale;
    private bool _dimensioneCambiata, _spaziaturaCambiata;

    public FinestraCarattere(FormatoCarattere attuale, IReadOnlyList<string> caratteri) : base("Carattere")
    {
        _iniziale = attuale;
        _caratteri = caratteri;
        _coloriTesto = ConPersonalizzato(Colori.Testo, attuale.Colore);
        _coloriSfondo = ConPersonalizzato(Colori.Sfondo, attuale.Sfondo);
        _stileIniziale = attuale is { Grassetto: bool g, Corsivo: bool c } ? (g ? 2 : 0) + (c ? 1 : 0) : -1;

        _tipo = Campo("&Carattere:", Scelta(caratteri, IndiceCarattere(attuale.Carattere)));
        _stile = Campo("S&tile:", Scelta(Stili, _stileIniziale));
        _dimensione = Campo("&Dimensione:", new NumeroConUnita("pt", (decimal)(attuale.Dimensione ?? ContenutoDocumento.DimensioneBase), 6, 96, 1, 1));
        _dimensione.ValueChanged += (_, _) => _dimensioneCambiata = true;

        var effetti = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 4, 0, 4) };
        _sottolineato = Spunta("&Sottolineato", attuale.Sottolineato);
        _barrato = Spunta("&Barrato", attuale.Barrato);
        effetti.Controls.AddRange([_sottolineato, _barrato]);
        RigaIntera(effetti);

        _posizione = Campo("&Posizione:", Scelta(Posizioni, attuale.Posizione is PosizioneTesto p ? (int)p : -1));
        _colore = Campo("C&olore del testo:", Scelta(_coloriTesto.Select(c => c.Nome), Indice(_coloriTesto, attuale.Colore)));
        _sfondo = Campo("S&fondo (evidenziato):", Scelta(_coloriSfondo.Select(c => c.Nome), Indice(_coloriSfondo, attuale.Sfondo)));
        _spaziatura = Campo("Sp&aziatura:", new NumeroConUnita("mm", (decimal)Math.Round((attuale.Spaziatura ?? 0) * MillimetriPerPunto, 1), -1, 7, 0.1m, 1));
        _spaziatura.ValueChanged += (_, _) => _spaziaturaCambiata = true;

        _contrasto = RigaIntera(new Label { AutoSize = true, Margin = new Padding(0, 8, 0, 0) });
        _anteprima = RigaIntera(new Label
        {
            Text = "AaBbCc àèìòù 123",
            AutoSize = false,
            Height = 56,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            BorderStyle = BorderStyle.FixedSingle,
            AccessibleName = "Esempio",
        });

        foreach (var scelta in new[] { _tipo, _stile, _posizione, _colore, _sfondo })
            scelta.SelectedIndexChanged += (_, _) => AggiornaAnteprima();
        _dimensione.ValueChanged += (_, _) => AggiornaAnteprima();
        _sottolineato.CheckStateChanged += (_, _) => AggiornaAnteprima();
        _barrato.CheckStateChanged += (_, _) => AggiornaAnteprima();
        AggiornaAnteprima();

        _predefinito = CasellaPredefinito(false);
        AggiungiPulsanti();
        ActiveControl = _tipo;
    }

    /// <summary>Se usare carattere e dimensione scelti anche per i documenti nuovi.</summary>
    public bool Predefinito => _predefinito.Checked;

    /// <summary>Carattere e dimensione mostrati nella finestra (per "Predefinito").</summary>
    public (string? Carattere, float Dimensione) Base =>
        (_tipo.SelectedIndex >= 0 ? _caratteri[_tipo.SelectedIndex] : null, (float)_dimensione.Value);

    /// <summary>Solo le proprietà cambiate dall'utente; le altre restano null ("non cambiare").</summary>
    public FormatoCarattere Modifiche
    {
        get
        {
            bool stileCambiato = _stile.SelectedIndex >= 0 && _stile.SelectedIndex != _stileIniziale;
            return new FormatoCarattere(
                _tipo.SelectedIndex >= 0 && _tipo.SelectedIndex != IndiceCarattere(_iniziale.Carattere) ? _caratteri[_tipo.SelectedIndex] : null,
                _dimensioneCambiata ? (float)_dimensione.Value : null,
                stileCambiato ? _stile.SelectedIndex >= 2 : null,
                stileCambiato ? _stile.SelectedIndex % 2 == 1 : null,
                Cambiato(_sottolineato, _iniziale.Sottolineato),
                Cambiato(_barrato, _iniziale.Barrato),
                _posizione.SelectedIndex >= 0 && (PosizioneTesto)_posizione.SelectedIndex != _iniziale.Posizione ? (PosizioneTesto)_posizione.SelectedIndex : null,
                _colore.SelectedIndex >= 0 && _colore.SelectedIndex != Indice(_coloriTesto, _iniziale.Colore) ? new ValoreColore(_coloriTesto[_colore.SelectedIndex].Rgb) : null,
                _sfondo.SelectedIndex >= 0 && _sfondo.SelectedIndex != Indice(_coloriSfondo, _iniziale.Sfondo) ? new ValoreColore(_coloriSfondo[_sfondo.SelectedIndex].Rgb) : null,
                _spaziaturaCambiata ? (float)_spaziatura.Value / MillimetriPerPunto : null);
        }
    }

    private void AggiornaAnteprima()
    {
        int? testo = _colore.SelectedIndex >= 0 ? _coloriTesto[_colore.SelectedIndex].Rgb : null;
        int? sfondo = _sfondo.SelectedIndex >= 0 ? _coloriSfondo[_sfondo.SelectedIndex].Rgb : null;
        _contrasto.Text = Colori.GiudizioContrasto(testo, sfondo);

        var stile = FontStyle.Regular;
        if (_stile.SelectedIndex >= 2) stile |= FontStyle.Bold;
        if (_stile.SelectedIndex % 2 == 1) stile |= FontStyle.Italic;
        if (_sottolineato.CheckState == CheckState.Checked) stile |= FontStyle.Underline;
        if (_barrato.CheckState == CheckState.Checked) stile |= FontStyle.Strikeout;
        var famiglia = _tipo.SelectedIndex >= 0 ? _caratteri[_tipo.SelectedIndex] : "Segoe UI";
        try
        {
            var vecchio = _anteprima.Font;
            _anteprima.Font = new Font(famiglia, Math.Min(28f, (float)_dimensione.Value), stile);
            if (vecchio != Font)
                vecchio.Dispose();
        }
        catch (ArgumentException)
        {
            // Carattere che non supporta lo stile richiesto: l'esempio resta com'era.
        }
        _anteprima.ForeColor = Color.FromArgb(unchecked((int)0xFF000000) | (testo ?? Colori.Nero));
        _anteprima.BackColor = Color.FromArgb(unchecked((int)0xFF000000) | (sfondo ?? Colori.Bianco));
    }

    private int IndiceCarattere(string? nome) =>
        nome is null ? -1 : _caratteri.ToList().FindIndex(c => c.Equals(nome, StringComparison.OrdinalIgnoreCase));

    private static List<ColoreConNome> ConPersonalizzato(IReadOnlyList<ColoreConNome> elenco, ValoreColore? attuale)
    {
        var colori = elenco.ToList();
        if (attuale is { Rgb: int rgb } && colori.All(c => c.Rgb != rgb))
            colori.Add(new ColoreConNome($"Personalizzato ({rgb:X6})", rgb));
        return colori;
    }

    private static int Indice(List<ColoreConNome> colori, ValoreColore? attuale) =>
        attuale is ValoreColore valore ? colori.FindIndex(c => c.Rgb == valore.Rgb) : -1;

    private static CheckBox Spunta(string testo, bool? valore) => new()
    {
        Text = testo,
        AutoSize = true,
        ThreeState = valore is null,
        CheckState = valore switch { true => CheckState.Checked, false => CheckState.Unchecked, null => CheckState.Indeterminate },
    };

    private static bool? Cambiato(CheckBox spunta, bool? iniziale) =>
        spunta.CheckState == CheckState.Indeterminate ? null
        : (spunta.CheckState == CheckState.Checked) == iniziale ? null
        : spunta.CheckState == CheckState.Checked;
}
