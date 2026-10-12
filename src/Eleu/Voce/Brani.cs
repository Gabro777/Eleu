using System.Text.RegularExpressions;
using Eleu.Documento;

namespace Eleu.Voce;

/// <summary>Un pezzo di testo da leggere, con la sua posizione nel documento.</summary>
public sealed record Brano(string Testo, int Inizio);

/// <summary>Un capitolo dell'audiolibro: diventa un file MP3.</summary>
public sealed record Capitolo(string Titolo, IReadOnlyList<string> Paragrafi, bool IniziaConTitolo = false);

public static partial class Brani
{
    /// <summary>Oltre questa lunghezza un paragrafo si divide in frasi, così la lettura parte senza attese.</summary>
    public const int LunghezzaMassima = 400;

    /// <summary>
    /// Divide il testo tra <paramref name="inizio"/> e <paramref name="fine"/> in brani da leggere uno dopo l'altro:
    /// un brano per paragrafo, e i paragrafi lunghi divisi in frasi. Le righe vuote si saltano.
    /// </summary>
    public static List<Brano> DaTesto(string testo, int inizio, int fine)
    {
        inizio = Math.Clamp(inizio, 0, testo.Length);
        fine = Math.Clamp(fine, inizio, testo.Length);
        var brani = new List<Brano>();

        int posizione = inizio;
        while (posizione < fine)
        {
            // In tabella ogni cella è un paragrafo a sé.
            int acapo = testo.IndexOfAny(['\n', StrutturaTesto.FineCella], posizione, fine - posizione);
            int fineParagrafo = acapo < 0 ? fine : acapo;
            AggiungiParagrafo(testo, posizione, fineParagrafo, brani);
            posizione = fineParagrafo + 1;
        }
        return brani;
    }

    private static void AggiungiParagrafo(string testo, int inizio, int fine, List<Brano> brani)
    {
        while (inizio < fine)
        {
            int finePezzo = fine;
            if (fine - inizio > LunghezzaMassima)
            {
                // Si taglia dopo l'ultima fine di frase entro il limite; se non ce n'è, all'ultimo spazio.
                var finestra = testo.AsSpan(inizio, LunghezzaMassima);
                int taglio = finestra.LastIndexOfAny(".!?;:");
                if (taglio < LunghezzaMassima / 4)
                    taglio = finestra.LastIndexOf(' ');
                if (taglio > 0)
                    finePezzo = inizio + taglio + 1;
                else
                    finePezzo = inizio + LunghezzaMassima;
            }

            var pezzo = StrutturaTesto.Leggibile(testo[inizio..finePezzo]);
            int spaziIniziali = pezzo.Length - pezzo.TrimStart().Length;
            if (pezzo.Trim().Length > 0)
                brani.Add(new Brano(pezzo.Trim(), inizio + spaziIniziali));
            inizio = finePezzo;
        }
    }

    /// <summary>
    /// Divide il documento in capitoli sui titoli del livello più alto presente
    /// (Titolo 1 se c'è, altrimenti Titolo 2, ...). Il testo prima del primo titolo
    /// diventa un capitolo "Introduzione"; senza titoli c'è un solo capitolo.
    /// </summary>
    public static List<Capitolo> Capitoli(ContenutoDocumento contenuto, string titoloDocumento)
    {
        var livelli = contenuto.Paragrafi.Where(p => p.LivelloTitolo > 0).Select(p => p.LivelloTitolo).ToList();
        int livelloCapitolo = livelli.Count > 0 ? livelli.Min() : 0;

        var capitoli = new List<Capitolo>();
        string titolo = livelloCapitolo == 0 ? titoloDocumento : "Introduzione";
        var paragrafi = new List<string>();
        bool iniziaConTitolo = false;

        void Chiudi()
        {
            if (paragrafi.Count > 0)
                capitoli.Add(new Capitolo(titolo, paragrafi.ToList(), iniziaConTitolo));
            paragrafi.Clear();
        }

        foreach (var paragrafo in contenuto.Paragrafi)
        {
            var testo = paragrafo.Testo.Trim();
            if (paragrafo.LivelloTitolo == livelloCapitolo && livelloCapitolo > 0)
            {
                Chiudi();
                titolo = testo.Length > 0 ? testo : $"Capitolo {capitoli.Count + 1}";
                iniziaConTitolo = testo.Length > 0;
            }
            if (testo.Length > 0)
                paragrafi.Add(testo);
        }
        Chiudi();
        return capitoli;
    }

    /// <summary>"03 - Il ritorno.mp3": numerato per restare in ordine, senza caratteri vietati nei nomi di file.</summary>
    public static string NomeFile(int numero, int totale, string titolo)
    {
        var pulito = CaratteriVietati().Replace(titolo, " ");
        pulito = SpaziMultipli().Replace(pulito, " ").Trim().TrimEnd('.');
        if (pulito.Length > 80)
            pulito = pulito[..80].Trim();
        if (pulito.Length == 0)
            pulito = "Capitolo";
        int cifre = Math.Max(2, totale.ToString().Length);
        return $"{numero.ToString().PadLeft(cifre, '0')} - {pulito}.mp3";
    }

    [GeneratedRegex(@"[<>:""/\\|?*\x00-\x1F]")]
    private static partial Regex CaratteriVietati();

    [GeneratedRegex(@"\s+")]
    private static partial Regex SpaziMultipli();
}
