namespace Eleu.Braille;

/// <summary>
/// Un paragrafo già tradotto in celle Braille. <see cref="Livello"/> &gt; 0 per i titoli (va nel sommario);
/// <see cref="PaginaStampa"/> è la pagina del documento a stampa in cui comincia (0 = non si sa).
/// </summary>
public sealed record ParagrafoBraille(string Celle, bool Titolo = false, bool NuovaPagina = false, int Livello = 0, int PaginaStampa = 0);

public enum NumeroPaginaBraille { Nessuno, InAltoADestra, InBassoADestra }

/// <summary>
/// Formato della pagina Braille: celle per riga e righe per pagina, più numerazione, titolo corrente in cima,
/// margine sinistro (per la rilegatura), indicazione delle pagine a stampa e sommario all'inizio.
/// </summary>
public sealed record FormatoPagina(int CellePerRiga = 32, int RighePerPagina = 27)
{
    public const int MinimoCelle = 10;
    public const int MassimoCelle = 60;
    public const int MinimoRighe = 5;
    public const int MassimoRighe = 60;

    public NumeroPaginaBraille Numero { get; init; } = NumeroPaginaBraille.Nessuno;

    /// <summary>Il titolo del documento, già in celle, da ripetere in cima a ogni pagina; null = niente.</summary>
    public string? Titolo { get; init; }

    public int MargineSinistro { get; init; }

    /// <summary>Una riga di trattini con il numero dove comincia una nuova pagina a stampa.</summary>
    public bool PagineStampa { get; init; }

    public bool Sommario { get; init; }

    /// <summary>Una riga vuota tra una riga di testo e l'altra: si legge più comodamente.</summary>
    public bool InterlineaDoppia { get; init; }

    /// <summary>
    /// Stampa su tutti e due i lati del foglio. Sul retro la rilegatura è a destra: il margine passa a destra e il
    /// numero di pagina si sposta con lui, così non finisce sul bordo dove la stampante non arriva o si rilega.
    /// </summary>
    public bool FronteRetro { get; init; }

    /// <summary>Due righe vuote a metà della pagina: il foglio si piega lì (per spedirlo) senza schiacciare i punti.</summary>
    public bool PiegaAMeta { get; init; }

    /// <summary>Le due righe della piega (da 0, contando tutte le righe della pagina).</summary>
    public (int Prima, int Seconda) RighePiega => (RighePerPagina / 2 - 1, RighePerPagina / 2);
}

/// <summary>
/// Dispone le celle Braille sulla pagina: va a capo tra le parole, rientra di due celle l'inizio dei paragrafi e
/// lascia una riga vuota prima dei titoli. Se chiesto aggiunge in cima (o in fondo) il titolo e il numero di pagina,
/// le righe che segnano le pagine a stampa e, all'inizio, un sommario con i numeri delle pagine Braille.
/// </summary>
public static class Impaginatore
{
    public const int Rientro = 2;

    /// <summary>Punti 3-6: il trattino Braille, per le righe che segnano le pagine a stampa.</summary>
    public const char Trattino = (char)0x2824;

    /// <summary>Punto 3: il puntino dei sommari, tra il titolo e il numero di pagina.</summary>
    public const char Puntino = (char)0x2804;

    /// <param name="traduci">Per i numeri di pagina e la parola "Sommario"; senza, si usano le cifre come sono.</param>
    public static List<List<string>> Impagina(IEnumerable<ParagrafoBraille> paragrafi, FormatoPagina formato, Func<string, string>? traduci = null)
    {
        traduci ??= t => t;
        var lista = paragrafi.ToList();
        int larghezza = Math.Max(4, formato.CellePerRiga - formato.MargineSinistro);
        bool rigaInCima = formato.Numero == NumeroPaginaBraille.InAltoADestra || formato.Titolo is not null;
        bool rigaInFondo = formato.Numero == NumeroPaginaBraille.InBassoADestra;
        int righeTesto = Math.Max(1, formato.RighePerPagina - (rigaInCima ? 1 : 0) - (rigaInFondo ? 1 : 0) - (formato.PiegaAMeta ? 2 : 0));
        List<Riga> Spaziate(List<Riga> righe) => formato.InterlineaDoppia ? Doppia(righe) : righe;

        var (corpo, titoliCorpo) = Pagine(Spaziate(Righe(lista, formato, larghezza, traduci)), righeTesto);
        var sommario = new List<List<string>>();
        if (formato.Sommario && titoliCorpo.Count > 0)
        {
            // Il sommario sposta in avanti le pagine del testo: lo si rifà finché il numero di pagine non cambia.
            for (int tentativo = 0; tentativo < 4; tentativo++)
            {
                int pagineSommario = sommario.Count;
                sommario = Pagine(Spaziate(Sommario(titoliCorpo, pagineSommario, larghezza, traduci)), righeTesto).Pagine;
                if (sommario.Count == pagineSommario)
                    break;
            }
        }

        var risultato = new List<List<string>>();
        var tutte = sommario.Concat(corpo).ToList();
        var (piega1, piega2) = formato.RighePiega;
        for (int i = 0; i < tutte.Count; i++)
        {
            // Sul retro (pagine pari, con il fronte-retro) il margine della rilegatura sta a destra.
            bool retro = formato.FronteRetro && i % 2 == 1;
            var margine = retro ? "" : new string(TraduttoreBraille.CellaVuota, formato.MargineSinistro);
            int utile = formato.CellePerRiga - formato.MargineSinistro;
            var numero = traduci((i + 1).ToString());
            string? testa = rigaInCima
                ? margine + RigaDiTesta(formato.Titolo ?? "", formato.Numero == NumeroPaginaBraille.InAltoADestra ? numero : "", utile)
                : null;
            string? piede = rigaInFondo ? margine + RigaDiTesta("", numero, utile) : null;

            // Riga per riga: in cima il titolo e il numero, in fondo il numero, a metà le due righe della piega.
            var pagina = new List<string>();
            int testo = 0;
            var righe = tutte[i];
            for (int r = 0; r < formato.RighePerPagina; r++)
            {
                if (r == 0 && testa is not null)
                    pagina.Add(testa);
                else if (r == formato.RighePerPagina - 1 && piede is not null)
                    pagina.Add(piede);
                else if (formato.PiegaAMeta && (r == piega1 || r == piega2))
                {
                    if (testo >= righe.Count && piede is null)
                        break;
                    pagina.Add("");
                }
                else if (testo < righe.Count)
                {
                    // Dopo la piega una riga vuota di interlinea non serve: sarebbero tre righe vuote di fila.
                    if (righe[testo].Length == 0 && pagina.Count > 0 && pagina[^1].Length == 0 && testo + 1 < righe.Count)
                        testo++;
                    var riga = righe[testo++];
                    pagina.Add(riga.Length == 0 ? "" : margine + riga);
                }
                else if (piede is not null)
                    pagina.Add("");
                else
                    break;
            }
            risultato.Add(pagina);
        }
        return risultato;
    }

    /// <summary>L'interlinea doppia: dopo ogni riga di testo una riga vuota (non due di fila).</summary>
    private static List<Riga> Doppia(List<Riga> righe)
    {
        var doppie = new List<Riga>();
        for (int i = 0; i < righe.Count; i++)
        {
            doppie.Add(righe[i]);
            bool prossimaVuota = i + 1 < righe.Count && righe[i + 1].Celle.Length == 0 && !righe[i + 1].NuovaPagina;
            if (righe[i].Celle.Length > 0 && !prossimaVuota)
                doppie.Add(new Riga(""));
        }
        return doppie;
    }

    /// <summary>Una riga con un testo a sinistra e uno a destra; il testo a sinistra si accorcia se non c'è posto.</summary>
    private static string RigaDiTesta(string sinistra, string destra, int larghezza)
    {
        int spazio = destra.Length > 0 ? larghezza - destra.Length - 1 : larghezza;
        if (sinistra.Length > spazio)
            sinistra = sinistra[..Math.Max(0, spazio)];
        if (destra.Length == 0)
            return sinistra;
        return sinistra + new string(TraduttoreBraille.CellaVuota, larghezza - sinistra.Length - destra.Length) + destra;
    }

    /// <summary>Una riga del testo, con il titolo (se è l'inizio di un titolo) e se deve cominciare una pagina nuova.</summary>
    private sealed record Riga(string Celle, bool NuovaPagina = false, (string Celle, int Livello)? Titolo = null);

    private static List<Riga> Righe(List<ParagrafoBraille> paragrafi, FormatoPagina formato, int larghezza, Func<string, string> traduci)
    {
        var righe = new List<Riga>();
        int paginaStampa = 0;
        bool nuovaPagina = false;
        foreach (var paragrafo in paragrafi)
        {
            if (paragrafo.NuovaPagina && righe.Count > 0)
                nuovaPagina = true;
            if (formato.PagineStampa && paragrafo.PaginaStampa > 0 && paragrafo.PaginaStampa != paginaStampa)
            {
                // La prima pagina a stampa non si segna: si comincia lì.
                if (paginaStampa > 0)
                {
                    var numero = traduci(paragrafo.PaginaStampa.ToString());
                    righe.Add(new Riga(new string(Trattino, Math.Max(1, larghezza - numero.Length - 1)) + TraduttoreBraille.CellaVuota + numero, nuovaPagina));
                    nuovaPagina = false;
                }
                paginaStampa = paragrafo.PaginaStampa;
            }
            if (paragrafo.Celle.Trim(TraduttoreBraille.CellaVuota, ' ').Length == 0)
                continue;
            if (paragrafo.Titolo && righe.Count > 0 && !nuovaPagina)
                righe.Add(new Riga(""));
            int rientro = paragrafo.Titolo ? 0 : Rientro;
            bool prima = true;
            foreach (var riga in VaiACapo(paragrafo.Celle, larghezza, rientro))
            {
                righe.Add(new Riga(riga, nuovaPagina,
                    prima && paragrafo.Titolo ? (paragrafo.Celle.Trim(TraduttoreBraille.CellaVuota), Math.Max(1, paragrafo.Livello)) : null));
                nuovaPagina = false;
                prima = false;
            }
        }
        return righe;
    }

    /// <summary>Le righe divise in pagine, e per ogni titolo la pagina (da 0) in cui si trova.</summary>
    private static (List<List<string>> Pagine, List<(string Celle, int Livello, int Pagina)> Titoli) Pagine(List<Riga> righe, int righeTesto)
    {
        var pagine = new List<List<string>>();
        var titoli = new List<(string, int, int)>();
        List<string>? pagina = null;
        foreach (var riga in righe)
        {
            if (pagina is null || riga.NuovaPagina || pagina.Count >= righeTesto)
            {
                // Una riga vuota in cima alla pagina non serve.
                if (riga.Celle.Length == 0 && !riga.NuovaPagina && pagina is not null)
                {
                    pagina = null;
                    continue;
                }
                pagina = [];
                pagine.Add(pagina);
            }
            if (riga.Celle.Length == 0 && pagina.Count == 0)
                continue;
            if (riga.Titolo is var (celle, livello))
                titoli.Add((celle, livello, pagine.Count - 1));
            pagina.Add(riga.Celle);
        }
        pagine.RemoveAll(p => p.Count == 0);
        return (pagine, titoli);
    }

    /// <summary>Le righe del sommario: "Sommario", poi ogni titolo (rientrato per livello), i puntini e la pagina.</summary>
    private static List<Riga> Sommario(List<(string Celle, int Livello, int Pagina)> titoli, int pagineSommario, int larghezza, Func<string, string> traduci)
    {
        var righe = new List<Riga> { new(traduci("Sommario")), new("") };
        foreach (var (celle, livello, pagina) in titoli)
        {
            var numero = traduci((pagina + 1 + pagineSommario).ToString());
            var rientro = new string(TraduttoreBraille.CellaVuota, Math.Min(6, (livello - 1) * 2));
            int spazio = larghezza - rientro.Length - numero.Length - 2; // almeno uno spazio e un puntino
            var titolo = celle.Length > spazio ? celle[..Math.Max(1, spazio)] : celle;
            var puntini = new string(Puntino, Math.Max(1, larghezza - rientro.Length - titolo.Length - numero.Length - 2));
            righe.Add(new Riga(rientro + titolo + TraduttoreBraille.CellaVuota + puntini + TraduttoreBraille.CellaVuota + numero));
        }
        return righe;
    }

    private static IEnumerable<string> VaiACapo(string celle, int larghezza, int rientro)
    {
        var parole = celle.Split(TraduttoreBraille.CellaVuota, StringSplitOptions.RemoveEmptyEntries);
        var riga = new string(TraduttoreBraille.CellaVuota, rientro);
        bool rigaVuota = true;

        foreach (var parolaIntera in parole)
        {
            var parola = parolaIntera;
            while (true)
            {
                int spazio = rigaVuota ? 0 : 1;
                if (riga.Length + spazio + parola.Length <= larghezza)
                {
                    riga += (rigaVuota ? "" : TraduttoreBraille.CellaVuota.ToString()) + parola;
                    rigaVuota = false;
                    break;
                }
                if (!rigaVuota)
                {
                    yield return riga;
                    riga = "";
                    rigaVuota = true;
                    continue;
                }
                // Parola più lunga della riga: la si spezza.
                int resto = larghezza - riga.Length;
                yield return riga + parola[..resto];
                parola = parola[resto..];
                riga = "";
            }
        }
        if (!rigaVuota)
            yield return riga;
    }
}
