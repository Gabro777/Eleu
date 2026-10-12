using Eleu.Documento;

namespace Eleu.Interfaccia;

/// <summary>
/// File > Imposta pagina, in due schede: Margini (foglio, orientamento, margini, distanze di intestazione e
/// piè di pagina) e Intestazioni (testi con variabili, da inserire con il menu contestuale).
/// "Predefinito" rende queste impostazioni il punto di partenza per i documenti nuovi.
/// </summary>
public sealed class FinestraPagina : Modulo
{
    private static readonly string[] Orientamenti = ["Verticale", "Orizzontale"];
    private static readonly string[] Tipologie = ["Intestazioni in tutte le pagine", "Intestazioni in tutte le pagine tranne la prima"];

    private readonly ImpostazioniPagina _iniziale;
    private readonly List<FormatoFoglio> _formati;
    private readonly ComboBox _formato, _orientamento, _tipologia;
    private readonly NumeroConUnita _sopra, _sotto, _sinistro, _destro, _distanzaIntestazione, _distanzaPiede;
    private readonly TextBox _intestazione, _piede;
    private readonly CheckBox _predefinito;

    public FinestraPagina(ImpostazioniPagina attuale, bool predefinito = true) : base("Imposta pagina")
    {
        _iniziale = attuale;
        _formati = ImpostazioniPagina.Formati.ToList();
        if (attuale.Formato is null)
            _formati.Add(new FormatoFoglio("Personalizzato", attuale.Larghezza, attuale.Altezza));
        int indice = attuale.Formato is FormatoFoglio f ? _formati.IndexOf(f) : _formati.Count - 1;

        var margini = NuovaGriglia(220);
        _formato = Campo(margini, "&Foglio:", Scelta(_formati.Select(x => x.Nome), indice));
        _orientamento = Campo(margini, "&Orientamento:", Scelta(Orientamenti, attuale.Orizzontale ? 1 : 0));
        _sopra = Campo(margini, "S&uperiore:", Centimetri(attuale.MargineSopra));
        _sotto = Campo(margini, "I&nferiore:", Centimetri(attuale.MargineSotto));
        _sinistro = Campo(margini, "&Sinistro:", Centimetri(attuale.MargineSinistro));
        _destro = Campo(margini, "&Destro:", Centimetri(attuale.MargineDestro));
        _distanzaIntestazione = Campo(margini, "Intestazione da&l bordo:", Centimetri(attuale.DistanzaIntestazione));
        _distanzaPiede = Campo(margini, "Piè di pagina dal &bordo:", Centimetri(attuale.DistanzaPiede));

        var intestazioni = NuovaGriglia(320);
        _tipologia = Campo(intestazioni, "&Tipologia di pagina:", Scelta(Tipologie, attuale.EscludiPrimaPagina ? 1 : 0));
        _intestazione = Campo(intestazioni, "&Intestazione:", TestoConVariabili(attuale.Intestazione));
        _piede = Campo(intestazioni, "&Piè di pagina:", TestoConVariabili(attuale.PiePagina));
        RigaIntera(intestazioni, new Label
        {
            AutoSize = true,
            MaximumSize = new Size(480, 0),
            Margin = new Padding(0, 8, 0, 0),
            Text = "Per inserire il numero di pagina o altre variabili, nel campo premi il tasto Applicazioni " +
                   "(o Maiusc+F10) e scegli dal menu. Esempio: Pagina {pagina} di {pagine}.",
        });

        var schede = new TabControl { Dock = DockStyle.Fill, Height = 360, Width = 540, AccessibleName = "Schede" };
        schede.TabPages.Add(Scheda("Margini", margini));
        schede.TabPages.Add(Scheda("Intestazioni", intestazioni));
        RigaIntera(schede);

        _predefinito = CasellaPredefinito(predefinito);
        AggiungiPulsanti();
        ActiveControl = _formato;
    }

    public bool Predefinito => _predefinito.Checked;

    public ImpostazioniPagina Risultato
    {
        get
        {
            var foglio = _formato.SelectedIndex >= 0 ? _formati[_formato.SelectedIndex] : null;
            return _iniziale with
            {
                Larghezza = foglio?.Larghezza ?? _iniziale.Larghezza,
                Altezza = foglio?.Altezza ?? _iniziale.Altezza,
                Orizzontale = _orientamento.SelectedIndex == 1,
                MargineSopra = Punti(_sopra),
                MargineSotto = Punti(_sotto),
                MargineSinistro = Punti(_sinistro),
                MargineDestro = Punti(_destro),
                DistanzaIntestazione = Punti(_distanzaIntestazione),
                DistanzaPiede = Punti(_distanzaPiede),
                Intestazione = _intestazione.Text.Trim(),
                PiePagina = _piede.Text.Trim(),
                EscludiPrimaPagina = _tipologia.SelectedIndex == 1,
            };
        }
    }

    /// <summary>Campo di testo con il menu contestuale "Inserisci variabile" (tasto Applicazioni o Maiusc+F10).</summary>
    private static TextBox TestoConVariabili(string valore)
    {
        var campo = new TextBox { Text = valore };
        var menu = new ContextMenuStrip();
        foreach (var variabile in ImpostazioniPagina.Variabili)
        {
            menu.Items.Add($"{variabile.Descrizione} {variabile.Segnaposto}", null, (_, _) =>
            {
                campo.SelectedText = variabile.Segnaposto;
                campo.Focus();
            });
        }
        campo.ContextMenuStrip = menu;
        return campo;
    }

    private static NumeroConUnita Centimetri(float punti) =>
        new("cm", (decimal)Math.Round(punti / ImpostazioniPagina.PuntiPerCentimetro, 2), 0, 10, 0.1m, 2);

    private static float Punti(NumericUpDown campo) => (float)campo.Value * ImpostazioniPagina.PuntiPerCentimetro;
}

/// <summary>Modifica > Vai a: sceglie dove andare (pagina, riga, paragrafo) e il numero.</summary>
public sealed class FinestraVaiA : Modulo
{
    public static readonly string[] Destinazioni = ["Pagina", "Riga", "Paragrafo"];

    private readonly ComboBox _destinazione;
    private readonly NumericUpDown _numero;

    public FinestraVaiA(int destinazione, int numero) : base("Vai a", 160)
    {
        _destinazione = Campo("&Vai a:", Scelta(Destinazioni, destinazione));
        _numero = Campo("&Numero:", Numero(numero, 1, 1_000_000, 1, 0));
        AggiungiPulsanti();
        ActiveControl = _numero;
        _numero.Select(0, _numero.Text.Length);
    }

    public int Destinazione => Math.Max(0, _destinazione.SelectedIndex);

    public int Valore => (int)_numero.Value;
}
