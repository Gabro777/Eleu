using System.Diagnostics;
using System.Globalization;
using Eleu.Documento;
using Eleu.Interfaccia;

namespace Eleu;

// Aiuti sulle parole: inserire un simbolo che non è sulla tastiera, cercare la parola sul web (enciclopedia,
// vocabolario) e vedere le parole più usate nel documento.
public sealed partial class FinestraPrincipale
{
    private void InserisciSimbolo()
    {
        using var finestra = new FinestraSimboli();
        if (finestra.ShowDialog(this) != DialogResult.OK || finestra.Scelto.Length == 0)
            return;
        _testo.SelectedText = finestra.Scelto;
        AnnunciaTraPoco($"Inserito {finestra.Scelto}");
    }

    /// <summary>Indirizzi di ricerca: {0} è la parola, già pronta per l'indirizzo.</summary>
    public static readonly IReadOnlyList<(string Nome, string Indirizzo)> ServiziWeb =
    [
        ("&Wikipedia", "https://it.wikipedia.org/w/index.php?search={0}"),
        ("&Vocabolario Treccani", "https://www.treccani.it/vocabolario/ricerca/{0}/"),
        ("Wi&kizionario (significati e sinonimi)", "https://it.wiktionary.org/w/index.php?search={0}"),
        ("&Google", "https://www.google.com/search?q={0}"),
    ];

    /// <summary>Cerca nel browser la selezione o la parola in cui si trova il cursore.</summary>
    private void CercaSulWeb(string indirizzo)
    {
        var testo = _testo.Text;
        var cerca = _testo.SelectionLength > 0
            ? StrutturaTesto.Leggibile(_testo.SelectedText).Trim()
            : ParolaIntorno(testo, Math.Min(_testo.SelectionStart, testo.Length)) is var (inizio, fine) ? testo[inizio..fine] : "";
        if (cerca.Length == 0)
        {
            _annunciatore.Annuncia("Seleziona il testo da cercare o porta il cursore su una parola");
            return;
        }
        if (cerca.Length > 200)
            cerca = cerca[..200];
        try
        {
            Process.Start(new ProcessStartInfo(string.Format(CultureInfo.InvariantCulture, indirizzo, Uri.EscapeDataString(cerca))) { UseShellExecute = true });
            _annunciatore.Annuncia($"Cerco {cerca} nel browser");
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            _annunciatore.Annuncia("Impossibile aprire il browser");
        }
    }

    /// <summary>Strumenti > Informazioni > Parole più usate: le parole di almeno tre lettere, dalla più frequente.</summary>
    private void ParolePiuUsate()
    {
        var testo = StrutturaTesto.Leggibile(_testo.SelectionLength > 0 ? _testo.SelectedText : _testo.Text);
        var conteggio = Frequenze(testo);
        if (conteggio.Count == 0)
        {
            _annunciatore.Annuncia("Nessuna parola");
            return;
        }
        var righe = new List<string>
        {
            $"{conteggio.Count} parole diverse di almeno tre lettere{(_testo.SelectionLength > 0 ? " nella selezione" : "")}.",
            "",
        };
        righe.AddRange(conteggio.Take(200).Select(c => $"{c.Parola}: {c.Volte} {(c.Volte == 1 ? "volta" : "volte")}"));
        using var finestra = new FinestraTesto("Parole più usate", string.Join(Environment.NewLine, righe));
        finestra.ShowDialog(this);
    }

    /// <summary>Le parole (in minuscolo, di almeno tre lettere) con quante volte compaiono, dalla più usata.</summary>
    public static List<(string Parola, int Volte)> Frequenze(string testo)
    {
        var conteggio = new Dictionary<string, int>(StringComparer.CurrentCultureIgnoreCase);
        int i = 0;
        while (i < testo.Length)
        {
            if (!char.IsLetter(testo[i]))
            {
                i++;
                continue;
            }
            int inizio = i;
            while (i < testo.Length && char.IsLetter(testo[i]))
                i++;
            if (i - inizio >= 3)
            {
                var parola = testo[inizio..i].ToLower(CultureInfo.CurrentCulture);
                conteggio[parola] = conteggio.GetValueOrDefault(parola) + 1;
            }
        }
        return conteggio.OrderByDescending(c => c.Value).ThenBy(c => c.Key, StringComparer.CurrentCulture)
            .Select(c => (c.Key, c.Value)).ToList();
    }
}
