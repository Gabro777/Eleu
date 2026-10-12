using System.Globalization;

namespace Eleu.Documento;

/// <summary>Un formato di foglio, in punti, in verticale.</summary>
public sealed record FormatoFoglio(string Nome, float Larghezza, float Altezza);

/// <summary>Una variabile da usare in intestazione e piè di pagina, es. {pagina}.</summary>
public sealed record VariabilePagina(string Segnaposto, string Descrizione, string CampoWord);

/// <summary>
/// Come si stampa il documento: foglio, orientamento, margini, intestazione e piè di pagina.
/// Misure in punti; Larghezza e Altezza sono quelle del foglio in verticale.
/// Intestazione e piè di pagina sono testi che possono contenere variabili ({pagina}, {pagine}, {titolo}, {data}).
/// </summary>
public sealed record ImpostazioniPagina(
    float Larghezza = 595.3f,
    float Altezza = 841.9f,
    bool Orizzontale = false,
    float MargineSinistro = ImpostazioniPagina.DueCentimetri,
    float MargineDestro = ImpostazioniPagina.DueCentimetri,
    float MargineSopra = ImpostazioniPagina.DueCentimetri,
    float MargineSotto = ImpostazioniPagina.DueCentimetri,
    float DistanzaIntestazione = ImpostazioniPagina.UnCentimetro,
    float DistanzaPiede = ImpostazioniPagina.UnCentimetro,
    string Intestazione = "",
    string PiePagina = "",
    bool EscludiPrimaPagina = false)
{
    public const float PuntiPerCentimetro = 72f / 2.54f;
    public const float UnCentimetro = PuntiPerCentimetro;
    public const float DueCentimetri = 2 * PuntiPerCentimetro;

    public static IReadOnlyList<FormatoFoglio> Formati { get; } =
    [
        new("A4", 595.3f, 841.9f),
        new("A5", 419.5f, 595.3f),
        new("A3", 841.9f, 1190.6f),
        new("Lettera", 612f, 792f),
        new("Legale", 612f, 1008f),
    ];

    public static IReadOnlyList<VariabilePagina> Variabili { get; } =
    [
        new("{pagina}", "Numero di pagina", "PAGE"),
        new("{pagine}", "Numero totale di pagine", "NUMPAGES"),
        new("{titolo}", "Titolo del documento", "TITLE"),
        new("{data}", "Data di oggi", "DATE"),
    ];

    /// <summary>Larghezza e altezza del foglio così come viene stampato (scambiate se orizzontale).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public (float Larghezza, float Altezza) Effettive => Orizzontale ? (Altezza, Larghezza) : (Larghezza, Altezza);

    /// <summary>Il formato con queste misure (in verticale o orizzontale), oppure null se è personalizzato.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public FormatoFoglio? Formato
    {
        get
        {
            float corto = Math.Min(Larghezza, Altezza), lungo = Math.Max(Larghezza, Altezza);
            return Formati.FirstOrDefault(f => Math.Abs(f.Larghezza - corto) < 2 && Math.Abs(f.Altezza - lungo) < 2);
        }
    }

    /// <summary>Il testo da stampare con le variabili sostituite; vuoto sulla prima pagina se va esclusa.</summary>
    public string Componi(string modello, int pagina, int totale, string titolo, DateTime data)
    {
        if (modello.Length == 0 || (EscludiPrimaPagina && pagina == 1))
            return "";
        return modello
            .Replace("{pagina}", pagina.ToString(CultureInfo.InvariantCulture))
            .Replace("{pagine}", totale.ToString(CultureInfo.InvariantCulture))
            .Replace("{titolo}", titolo)
            .Replace("{data}", data.ToString("d MMMM yyyy", CultureInfo.GetCultureInfo("it-IT")));
    }

    /// <summary>
    /// Divide un testo con variabili in pezzi: testo normale (Campo null) o variabile (Campo = nome del campo di Word).
    /// Serve per scrivere i campi veri nei file RTF e DOCX.
    /// </summary>
    public static IEnumerable<(string Testo, string? Campo)> Pezzi(string modello)
    {
        int i = 0;
        while (i < modello.Length)
        {
            var prossima = Variabili
                .Select(v => (Variabile: v, Indice: modello.IndexOf(v.Segnaposto, i, StringComparison.OrdinalIgnoreCase)))
                .Where(x => x.Indice >= 0)
                .OrderBy(x => x.Indice)
                .FirstOrDefault();
            if (prossima.Variabile is null)
            {
                yield return (modello[i..], null);
                yield break;
            }
            if (prossima.Indice > i)
                yield return (modello[i..prossima.Indice], null);
            yield return (prossima.Variabile.Segnaposto, prossima.Variabile.CampoWord);
            i = prossima.Indice + prossima.Variabile.Segnaposto.Length;
        }
    }

    /// <summary>La variabile corrispondente a un campo di Word (PAGE, NUMPAGES, ...), se Eleu la conosce.</summary>
    public static string? SegnapostoDelCampo(string istruzione)
    {
        var nome = istruzione.Trim().Split(' ', 2)[0];
        return Variabili.FirstOrDefault(v => v.CampoWord.Equals(nome, StringComparison.OrdinalIgnoreCase))?.Segnaposto;
    }
}
