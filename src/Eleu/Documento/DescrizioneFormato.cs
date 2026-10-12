using System.Globalization;
using Eleu.Editor;

namespace Eleu.Documento;

/// <summary>
/// Descrizione a voce della formattazione nel punto del cursore (Maiusc+F2).
/// Si dice sempre tipo e dimensione del carattere e l'allineamento; il resto solo se si discosta dal normale,
/// così la frase resta breve. I valori null indicano una selezione con formattazione mista.
/// </summary>
public static class DescrizioneFormato
{
    private static readonly CultureInfo Italiano = CultureInfo.GetCultureInfo("it-IT");

    public static string Descrivi(int livelloTitolo, FormatoCarattere carattere, FormatoParagrafo paragrafo)
    {
        var parti = new List<string> { livelloTitolo > 0 ? $"Titolo {livelloTitolo}" : "Testo normale" };

        var dimensione = carattere.Dimensione is float d ? $"{N(d)} punti" : "dimensioni miste";
        parti.Add(carattere.Carattere is string nome ? $"{nome} {dimensione}" : $"caratteri misti, {dimensione}");

        Effetto(parti, carattere.Grassetto, "grassetto");
        Effetto(parti, carattere.Corsivo, "corsivo");
        Effetto(parti, carattere.Sottolineato, "sottolineato");
        Effetto(parti, carattere.Barrato, "barrato");

        switch (carattere.Posizione)
        {
            case PosizioneTesto.Apice:
                parti.Add("apice");
                break;
            case PosizioneTesto.Pedice:
                parti.Add("pedice");
                break;
            case null:
                parti.Add("in parte apice o pedice");
                break;
        }

        if (carattere.Colore is null)
            parti.Add("colori misti");
        else if (carattere.Colore.Value.Rgb is int colore)
            parti.Add($"colore {Colori.Nome(colore)}");
        if (carattere.Sfondo is null)
            parti.Add("sfondi misti");
        else if (carattere.Sfondo.Value.Rgb is int sfondo)
            parti.Add($"sfondo {Colori.Nome(sfondo, sfondo: true)}");
        if (carattere.Spaziatura is float spaziatura and not 0)
            parti.Add($"spaziatura {N(spaziatura)} punti");

        parti.Add(paragrafo.Allineamento switch
        {
            Allineamento.Centro => "centrato",
            Allineamento.Destra => "allineato a destra",
            Allineamento.Giustificato => "giustificato",
            Allineamento.Sinistra => "allineato a sinistra",
            _ => "allineamenti misti",
        });

        switch (paragrafo.Elenco)
        {
            case TipoElenco.Puntato:
                parti.Add("elenco puntato");
                break;
            case TipoElenco.Numerato:
                parti.Add("elenco numerato");
                break;
        }

        if (paragrafo.Interlinea is float interlinea && Math.Abs(interlinea - 1) > 0.01f)
            parti.Add(interlinea switch
            {
                1.5f => "interlinea una e mezza",
                2f => "interlinea doppia",
                _ => $"interlinea {N(interlinea, "0.##")}",
            });
        Misura(parti, paragrafo.RientroSinistro, "rientro sinistro");
        Misura(parti, paragrafo.RientroDestro, "rientro destro");
        if (paragrafo.RientroPrimaRiga is float primaRiga && Math.Abs(primaRiga) > 0.5f)
            parti.Add($"prima riga {(primaRiga > 0 ? "rientrata" : "sporgente")} di {Centimetri(Math.Abs(primaRiga))}");
        if (paragrafo.SpazioPrima is float prima and > 0)
            parti.Add($"spazio prima {N(prima)} punti");
        if (paragrafo.SpazioDopo is float dopo and > 0)
            parti.Add($"spazio dopo {N(dopo)} punti");

        return string.Join(", ", parti);
    }

    private static void Effetto(List<string> parti, bool? attivo, string nome)
    {
        if (attivo == true)
            parti.Add(nome);
        else if (attivo is null)
            parti.Add($"in parte {nome}");
    }

    private static void Misura(List<string> parti, float? punti, string nome)
    {
        if (punti is float valore && valore > 0.5f)
            parti.Add($"{nome} {Centimetri(valore)}");
    }

    private static string N(float valore, string formato = "0.#") => valore.ToString(formato, Italiano);

    private static string Centimetri(float punti) => $"{N(punti / (72f / 2.54f))} centimetri";
}
