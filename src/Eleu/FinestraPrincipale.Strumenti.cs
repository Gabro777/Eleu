using System.Globalization;
using System.Text;
using Eleu.Correzione;
using Eleu.Documento;
using Eleu.Interfaccia;

namespace Eleu;

// Strumenti di scrittura: pulizia del testo, sostituzioni multiple, ordinamento dei paragrafi,
// statistiche complete e confronto con un altro documento.
public sealed partial class FinestraPrincipale
{
    /// <summary>
    /// Applica le modifiche dalla fine all'inizio, così le posizioni ancora da fare non si spostano; ogni
    /// modifica tocca solo il suo pezzo e la formattazione del resto resta.
    /// </summary>
    private void ApplicaModifiche(IReadOnlyList<Modifica> modifiche)
    {
        int cursore = _testo.SelectionStart;
        foreach (var m in modifiche.OrderByDescending(m => m.Inizio))
        {
            _testo.Select(m.Inizio, m.Lunghezza);
            _testo.SelectedText = m.Nuovo;
            if (m.Inizio < cursore)
                cursore += m.Nuovo.Length - Math.Min(m.Lunghezza, cursore - m.Inizio);
        }
        _testo.Select(Math.Clamp(cursore, 0, _testo.TextLength), 0);
        if (modifiche.Count > 0)
            _testo.Modified = true;
    }

    private static string Correzioni(int n) => n switch
    {
        0 => "Nessuna correzione da fare",
        1 => "1 correzione fatta",
        _ => $"{n} correzioni fatte",
    };

    private void PuliziaTesto()
    {
        bool selezione = _testo.SelectionLength > 0;
        using var finestra = new FinestraPulizia(_impostazioni.Pulizia, selezione);
        if (finestra.ShowDialog(this) != DialogResult.OK)
            return;
        _impostazioni.Pulizia = finestra.Opzioni;
        int da = finestra.SoloSelezione ? _testo.SelectionStart : 0;
        int a = finestra.SoloSelezione ? _testo.SelectionStart + _testo.SelectionLength : -1;
        var modifiche = Pulizia.Modifiche(_testo.Text, finestra.Opzioni, da, a);
        ApplicaModifiche(modifiche);
        AnnunciaTraPoco(Correzioni(modifiche.Count));
    }

    private void ApriSostituzioniMultiple()
    {
        using var finestra = new FinestraSostituzioni(_impostazioni.Sostituzioni, _impostazioni.SostituzioniParoleIntere);
        if (finestra.ShowDialog(this) != DialogResult.OK)
            return;
        _impostazioni.Sostituzioni = finestra.Coppie;
        _impostazioni.SostituzioniParoleIntere = finestra.ParoleIntere;
        _impostazioni.Salva();
        var modifiche = SostituzioniMultiple.Modifiche(_testo.Text, finestra.Coppie,
            new OpzioniRicerca(MaiuscoleMinuscole: true, ParolaIntera: finestra.ParoleIntere));
        ApplicaModifiche(modifiche);
        AnnunciaTraPoco(modifiche.Count switch
        {
            0 => "Nessuna sostituzione da fare",
            1 => "1 sostituzione fatta",
            var n => $"{n} sostituzioni fatte",
        });
    }

    /// <summary>
    /// Ordina alfabeticamente i paragrafi selezionati (anche solo in parte), con la loro formattazione.
    /// I numeri si ordinano come numeri.
    /// </summary>
    private void OrdinaParagrafi()
    {
        var testo = _testo.Text;
        var paragrafi = StrutturaTesto.Paragrafi(testo);
        int primo = StrutturaTesto.ParagrafoDi(paragrafi, _testo.SelectionStart);
        int ultimo = StrutturaTesto.ParagrafoDi(paragrafi, Math.Max(_testo.SelectionStart, _testo.SelectionStart + _testo.SelectionLength - 1));
        if (ultimo <= primo)
        {
            _annunciatore.Annuncia("Seleziona almeno due paragrafi da ordinare");
            return;
        }
        int inizio = paragrafi[primo].Inizio, fine = paragrafi[ultimo].Fine;
        if (testo.AsSpan(inizio, fine - inizio).ContainsAny([StrutturaTesto.InizioRiga, StrutturaTesto.FineCella]))
        {
            _annunciatore.Annuncia("Nella selezione c'è una tabella: per le tabelle usa Inserisci, Tabelle, Ordina righe");
            return;
        }
        _testo.Select(inizio, fine - inizio);
        var modello = RtfLettore.Leggi(_testo.SelectedRtf ?? "");
        var confronto = new ConfrontoNaturale();
        var ordinati = modello.Paragrafi.OrderBy(p => p.Testo.Trim(), confronto).ToList();
        modello.Paragrafi.Clear();
        modello.Paragrafi.AddRange(ordinati);
        // L'ultimo paragrafo selezionato non ha l'a capo nella selezione: lo si toglie anche dal testo nuovo.
        var rtf = RtfScrittore.Scrivi(modello, _impostazioni.CarattereBase, _impostazioni.DimensioneBase);
        int ultimoPar = rtf.LastIndexOf("\\par", StringComparison.Ordinal);
        if (ultimoPar >= 0)
            rtf = rtf.Remove(ultimoPar, 4);
        _testo.SelectedRtf = rtf;
        _testo.Select(inizio, 0);
        _testo.Modified = true;
        _annunciatore.Annuncia($"{ordinati.Count} paragrafi in ordine alfabetico. {DescriviRiga(_testo.Text, inizio)}");
    }

    /// <summary>Ordine alfabetico italiano, con i numeri confrontati come numeri ("9" prima di "10").</summary>
    private sealed class ConfrontoNaturale : IComparer<string>
    {
        private readonly CompareInfo _italiano = CultureInfo.GetCultureInfo("it-IT").CompareInfo;

        public int Compare(string? a, string? b) =>
            _italiano.Compare(a, b, CompareOptions.IgnoreCase | CompareOptions.NumericOrdering);
    }

    private void MostraStatisticheComplete()
    {
        bool selezione = _testo.SelectionLength > 0;
        var statistiche = StatisticheComplete.Calcola(selezione ? _testo.SelectedText : _testo.Text);
        var titolo = selezione ? "Statistiche della selezione" : "Statistiche del documento";
        using var finestra = new FinestraTesto(titolo, string.Join(Environment.NewLine, statistiche.Righe()));
        finestra.ShowDialog(this);
    }

    private void ConfrontaDocumento()
    {
        using var dialogo = new OpenFileDialog
        {
            Title = "Confronta con un altro documento",
            Filter = "Documenti|*.docx;*.odt;*.rtf;*.txt|Tutti i file|*.*",
            InitialDirectory = CartellaIniziale(),
        };
        if (dialogo.ShowDialog(this) != DialogResult.OK)
            return;
        List<string> altro;
        try
        {
            altro = ParagrafiDelFile(dialogo.FileName);
        }
        catch (Exception e) // Al confine con l'utente: un file illeggibile diventa un messaggio.
        {
            MessageBox.Show(this, $"Impossibile leggere {Path.GetFileName(dialogo.FileName)}.\n\n{e.Message}", NomeApp,
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        var qui = ParagrafiConPosizione();
        var differenze = Confronto.Differenze(qui.Select(p => SenzaSegnaposto(p.Paragrafo.Testo)).ToList(), altro);
        if (differenze.Count == 0)
        {
            AnnunciaTraPoco($"I due documenti hanno lo stesso testo");
            return;
        }
        int PosizioneDi(Differenza d)
        {
            int indice = d.Tipo == TipoDifferenza.SoloNellAltro ? d.Qui + 1 : d.Qui;
            return indice < 0 ? 0 : indice < qui.Count ? qui[indice].Inizio : _testo.TextLength;
        }
        var voci = differenze.Select((d, i) => new VoceElenco([d.Descrizione], i)).ToList();
        using var finestra = new FinestraElenco($"Differenze con {Path.GetFileName(dialogo.FileName)}: {differenze.Count}", ["Differenza"], voci, eliminabile: false);
        finestra.ShowDialog(this);
        if (finestra.Destinazione is int scelta)
        {
            var d = differenze[scelta];
            BeginInvoke(() =>
            {
                int posizione = PosizioneDi(d);
                _testo.Select(posizione, 0);
                _testo.ScrollToCaret();
                _annunciatore.Annuncia(d.Descrizione);
            });
        }
    }

    /// <summary>I paragrafi di un file DOCX, ODT, RTF o di testo, senza i segnaposto di segnalibri, note e immagini.</summary>
    private static List<string> ParagrafiDelFile(string percorso)
    {
        IEnumerable<string> paragrafi;
        switch (FormatiFile.DaPercorso(percorso))
        {
            case FormatoFile.Docx:
                using (var flusso = File.OpenRead(percorso))
                    paragrafi = DocxFormato.Leggi(flusso).Paragrafi.Select(p => p.Testo).ToList();
                break;
            case FormatoFile.Odt:
                using (var flusso = File.OpenRead(percorso))
                    paragrafi = OdtFormato.Leggi(flusso).Paragrafi.Select(p => p.Testo).ToList();
                break;
            case FormatoFile.Rtf:
                paragrafi = RtfLettore.Leggi(File.ReadAllText(percorso, Encoding.Latin1)).Paragrafi.Select(p => p.Testo);
                break;
            default:
                paragrafi = FileTesto.Leggi(File.ReadAllBytes(percorso)).Testo.ReplaceLineEndings("\n").Split('\n');
                break;
        }
        return paragrafi.Select(SenzaSegnaposto).ToList();
    }

    private static string SenzaSegnaposto(string testo) =>
        string.Concat(testo.Where(c => c is < (char)0xE000 or > (char)0xF8FF));
}
