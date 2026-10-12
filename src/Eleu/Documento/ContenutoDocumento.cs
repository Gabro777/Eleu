namespace Eleu.Documento;

public enum Allineamento { Sinistra, Centro, Destra, Giustificato }

public enum PosizioneTesto { Normale, Apice, Pedice }

/// <summary>
/// Un pezzo di testo con la stessa formattazione. Dimensioni e spaziature in punti;
/// colori come 0xRRGGBB, null = automatico (colore del testo) o nessuno (sfondo).
/// </summary>
public sealed record Segmento(
    string Testo,
    bool Grassetto = false,
    bool Corsivo = false,
    bool Sottolineato = false,
    float? Dimensione = null,
    string? Carattere = null,
    bool Barrato = false,
    PosizioneTesto Posizione = PosizioneTesto.Normale,
    int? Colore = null,
    int? Sfondo = null,
    float Spaziatura = 0)
{
    public bool StessoFormato(Segmento altro) => this with { Testo = "" } == altro with { Testo = "" };
}

public sealed class Paragrafo
{
    private readonly List<Segmento> _segmenti = [];

    public IReadOnlyList<Segmento> Segmenti => _segmenti;

    /// <summary>0 = testo normale, 1-9 = Titolo 1, Titolo 2, ...</summary>
    public int LivelloTitolo { get; set; }

    /// <summary>Elenco puntato.</summary>
    public bool Elenco { get; set; }

    /// <summary>Elenco numerato (1. 2. 3.), con numerazione automatica.</summary>
    public bool Numerato { get; set; }

    public Allineamento Allineamento { get; set; }

    // Rientri e spazi in punti. RientroPrimaRiga negativo = prima riga sporgente.
    public float RientroSinistro { get; set; }

    public float RientroDestro { get; set; }

    public float RientroPrimaRiga { get; set; }

    public float SpazioPrima { get; set; }

    public float SpazioDopo { get; set; }

    /// <summary>Interlinea come multiplo della riga singola: 1, 1.15, 1.5, 2.</summary>
    public float Interlinea { get; set; } = 1f;

    /// <summary>Se il paragrafo sta in una cella di tabella: quale tabella, riga e colonna. Una cella può avere più paragrafi.</summary>
    public PosizioneCella? Cella { get; set; }

    /// <summary>
    /// La pagina del documento a stampa in cui comincia il paragrafo (0 = non si sa). Non si salva: serve al Braille
    /// per segnare dove cambia la pagina a stampa.
    /// </summary>
    public int PaginaStampa { get; set; }

    /// <summary>Il paragrafo inizia su una pagina nuova (interruzione di pagina prima). Non passa ai paragrafi seguenti.</summary>
    public bool SaltoPagina { get; set; }

    public string Testo => string.Concat(_segmenti.Select(s => s.Testo));

    /// <summary>Aggiunge il segmento, unendolo al precedente se ha la stessa formattazione.</summary>
    public void Aggiungi(Segmento segmento)
    {
        if (segmento.Testo.Length == 0)
            return;
        if (_segmenti.Count > 0 && _segmenti[^1].StessoFormato(segmento))
            _segmenti[^1] = _segmenti[^1] with { Testo = _segmenti[^1].Testo + segmento.Testo };
        else
            _segmenti.Add(segmento);
    }

    /// <summary>Toglie gli spazi in fondo al paragrafo (dall'ultimo segmento, e da quelli che restano vuoti).</summary>
    public void TogliSpaziFinali()
    {
        while (_segmenti.Count > 0)
        {
            var ultimo = _segmenti[^1];
            var pulito = ultimo.Testo.TrimEnd(' ', '\t');
            if (pulito.Length > 0)
            {
                _segmenti[^1] = ultimo with { Testo = pulito };
                return;
            }
            _segmenti.RemoveAt(_segmenti.Count - 1);
        }
    }

    /// <summary>Riporta le proprietà del paragrafo ai valori predefiniti, lasciando il testo.</summary>
    public void ReimpostaProprieta()
    {
        var predefinito = new Paragrafo();
        LivelloTitolo = predefinito.LivelloTitolo;
        Elenco = predefinito.Elenco;
        Numerato = predefinito.Numerato;
        Allineamento = predefinito.Allineamento;
        RientroSinistro = predefinito.RientroSinistro;
        RientroDestro = predefinito.RientroDestro;
        RientroPrimaRiga = predefinito.RientroPrimaRiga;
        SpazioPrima = predefinito.SpazioPrima;
        SpazioDopo = predefinito.SpazioDopo;
        Interlinea = predefinito.Interlinea;
        SaltoPagina = false;
    }

    public Paragrafo CopiaProprieta() => new()
    {
        LivelloTitolo = LivelloTitolo,
        Elenco = Elenco,
        Numerato = Numerato,
        Allineamento = Allineamento,
        RientroSinistro = RientroSinistro,
        RientroDestro = RientroDestro,
        RientroPrimaRiga = RientroPrimaRiga,
        SpazioPrima = SpazioPrima,
        SpazioDopo = SpazioDopo,
        Interlinea = Interlinea,
    };
}

/// <summary>
/// Modello di documento indipendente dal formato del file: serve da ponte tra
/// il campo di testo (RTF) e i formati salvati su disco (DOCX, TXT).
/// </summary>
public sealed class ContenutoDocumento
{
    public List<Paragrafo> Paragrafi { get; } = [];

    public ImpostazioniPagina Pagina { get; set; } = new();

    /// <summary>Titolo, autore e il resto delle proprietà del documento (File > Proprietà).</summary>
    public Metadati Metadati { get; set; } = new();

    /// <summary>
    /// Parti del file originale che Eleu non sa conservare (es. "tabelle", "immagini"):
    /// salvare sopra l'originale le farebbe perdere.
    /// </summary>
    public List<string> ElementiNonConservati { get; } = [];

    /// <summary>Nomi dei segnalibri; nel testo ognuno è il suo segnaposto (vedi <see cref="Documento.Segnalibri"/>).</summary>
    public List<string> Segnalibri { get; } = [];

    /// <summary>Testi delle note a piè di pagina; nel testo ognuna è il suo segnaposto (vedi <see cref="Documento.Note"/>).</summary>
    public List<string> Note { get; } = [];

    /// <summary>Immagini; nel testo ognuna è il suo segnaposto (vedi <see cref="Documento.Immagini"/>).</summary>
    public List<Immagine> Immagini { get; } = [];

    /// <summary>Commenti; nel testo ognuno ha il segnaposto di inizio e quello di fine (vedi <see cref="Documento.Commenti"/>).</summary>
    public List<Commento> Commenti { get; } = [];

    public static ContenutoDocumento DaTestoSemplice(string testo)
    {
        var contenuto = new ContenutoDocumento();
        foreach (var riga in testo.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var paragrafo = new Paragrafo();
            paragrafo.Aggiungi(new Segmento(riga));
            contenuto.Paragrafi.Add(paragrafo);
        }
        return contenuto;
    }

    public const float DimensioneBase = 12f;

    public static float DimensioneTitolo(int livello) => livello switch
    {
        1 => 18f,
        2 => 15f,
        3 => 13f,
        _ => DimensioneBase,
    };
}

/// <summary>
/// Dove sta un paragrafo in una tabella. Le tabelle del documento sono numerate da 0, righe e colonne pure.
/// Larghezza della colonna in punti (0 = automatica); Intestazione = la riga si ripete in cima a ogni pagina.
/// </summary>
public sealed record PosizioneCella(int Tabella, int Riga, int Colonna, float Larghezza = 0, bool Intestazione = false)
{
    public bool StessaCella(PosizioneCella? altra) =>
        altra is not null && altra.Tabella == Tabella && altra.Riga == Riga && altra.Colonna == Colonna;

    public bool StessaRiga(PosizioneCella? altra) =>
        altra is not null && altra.Tabella == Tabella && altra.Riga == Riga;
}
