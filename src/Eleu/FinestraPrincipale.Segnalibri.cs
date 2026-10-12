using Eleu.Documento;
using Eleu.Editor;
using Eleu.Interfaccia;

namespace Eleu;

// Segnalibri: punti con un nome a cui tornare (Inserisci > Segnalibri), conservati in RTF e DOCX.
// Ogni segnalibro è un intervallo del Text Object Model di RichEdit, che si sposta da solo mentre si scrive.
public sealed partial class FinestraPrincipale
{
    private readonly List<(string Nome, dynamic Intervallo)> _segnalibri = [];
    private dynamic? _tom;

    // Se il campo ricrea la sua finestra (per esempio cambiando "A capo automatico") gli intervalli non valgono più:
    // si ricordano le posizioni e si ricreano.
    private List<(string Nome, int Posizione)>? _segnalibriInSospeso;

    private dynamic? Tom => _tom ??= RichEditNativo.DocumentoTom(_testo);

    private void PreparaSegnalibri()
    {
        _testo.HandleDestroyed += (_, _) =>
        {
            _segnalibriInSospeso = ElencoSegnalibri();
            _segnalibri.Clear();
            _tom = null;
        };
        _testo.HandleCreated += (_, _) =>
        {
            if (_segnalibriInSospeso is { } sospesi)
                BeginInvoke(() => ImpostaSegnalibri(sospesi));
            _segnalibriInSospeso = null;
        };
    }

    /// <summary>I segnalibri in ordine di posizione.</summary>
    private List<(string Nome, int Posizione)> ElencoSegnalibri() =>
        _segnalibri.Select(s => (s.Nome, Posizione: Math.Clamp((int)s.Intervallo.Start, 0, _testo.TextLength)))
            .OrderBy(s => s.Posizione).ToList();

    private void ImpostaSegnalibri(IEnumerable<(string Nome, int Posizione)> segnalibri)
    {
        _segnalibri.Clear();
        if (Tom is not { } tom)
            return;
        foreach (var (nome, posizione) in segnalibri)
            _segnalibri.Add((nome, tom.Range(posizione, posizione)));
    }

    /// <summary>Segnalibri, note e immagini del documento, nell'ordine in cui stanno: il loro segnaposto è il numero nell'elenco.</summary>
    private sealed record Segnaposti(List<string> Segnalibri, List<string> Note, List<Immagine> Immagini, List<Commento> Commenti)
    {
        public bool Vuoti => Segnalibri.Count == 0 && Note.Count == 0 && Immagini.Count == 0 && Commenti.Count == 0;
    }

    /// <summary>
    /// Mette l'RTF nel campo di testo con segnalibri, note e immagini. Se <paramref name="modello"/> è null sono
    /// ancora come nel file ({\*\bkmkstart}, {\footnote}, {\pict}), altrimenti sono già segnaposto con i dati del modello.
    /// </summary>
    private void CaricaRtf(string rtf, ContenutoDocumento? modello = null)
    {
        // Le righe di intestazione delle tabelle il campo di testo le perde: si leggono prima e si ricordano a parte.
        var conIntestazioni = modello ?? (rtf.Contains(@"\trhdr", StringComparison.Ordinal) ? RtfLettore.Leggi(rtf) : null);
        CaricaRtfConSegnaposto(rtf, modello);
        IntestazioniDopoIlCaricamento(conIntestazioni);
    }

    private void CaricaRtfConSegnaposto(string rtf, ContenutoDocumento? modello)
    {
        var nomi = modello?.Segnalibri.ToList() ?? [];
        var testiNote = modello?.Note.ToList() ?? [];
        var immagini = modello?.Immagini.ToList() ?? [];
        var commenti = modello?.Commenti.ToList() ?? [];
        if (modello is null)
            rtf = Immagini.InSegnaposto(Note.InSegnaposto(Segnalibri.InSegnaposto(Commenti.InSegnaposto(rtf, commenti), nomi), testiNote), immagini);
        _ultimaCella = null;
        _testo.Rtf = rtf;
        _segnalibri.Clear();
        _note.Clear();
        _immagini.Clear();
        _commenti.Clear();
        if ((nomi.Count == 0 && testiNote.Count == 0 && immagini.Count == 0 && commenti.Count == 0) || Tom is not { } tom)
            return;

        // Dalla fine all'inizio: i segnalibri lasciano un intervallo vuoto, le note il loro numero in apice,
        // le immagini l'immagine vera. Gli intervalli già creati si spostano da soli quando cambia il testo prima.
        var testo = _testo.Text;
        int numeroNota = testo.Count(c => Note.Indice(c) >= 0 && Note.Indice(c) < testiNote.Count);
        var segnalibri = new List<(string, dynamic)>();
        var note = new List<(string, dynamic)>();
        var figure = new List<(Immagine, dynamic)>();
        // Dei commenti si incontra prima la fine (si va all'indietro): un intervallo vuoto la tiene finché arriva l'inizio.
        var finiCommenti = new Dictionary<int, dynamic>();
        var intervalliCommenti = new SortedDictionary<int, dynamic>();
        for (int i = testo.Length - 1; i >= 0; i--)
        {
            int segnalibro = Segnalibri.Indice(testo[i]), nota = Note.Indice(testo[i]), immagine = Immagini.Indice(testo[i]);
            int inizioCommento = Commenti.IndiceInizio(testo[i]), fineCommento = Commenti.IndiceFine(testo[i]);
            if (inizioCommento >= 0 || fineCommento >= 0)
            {
                tom.Range(i, i + 1).Text = "";
                if (fineCommento >= 0 && fineCommento < commenti.Count)
                    finiCommenti[fineCommento] = tom.Range(i, i);
                else if (inizioCommento >= 0 && inizioCommento < commenti.Count)
                    intervalliCommenti[inizioCommento] = tom.Range(i, finiCommenti.TryGetValue(inizioCommento, out var fine) ? (int)fine.Start : i);
                continue;
            }
            if (segnalibro >= 0)
            {
                tom.Range(i, i + 1).Text = "";
                if (segnalibro < nomi.Count)
                    segnalibri.Insert(0, (nomi[segnalibro], tom.Range(i, i)));
            }
            else if (nota >= 0 && nota < testiNote.Count)
            {
                var richiamo = tom.Range(i, i + 1);
                richiamo.Text = (numeroNota--).ToString();
                richiamo.Font.Superscript = TomVero;
                note.Insert(0, (testiNote[nota], richiamo));
            }
            else if (immagine >= 0 && immagine < immagini.Count)
            {
                _testo.Select(i, 1);
                _testo.SelectedRtf = Immagini.RtfSoloImmagine(immagini[immagine]);
                figure.Insert(0, (immagini[immagine], tom.Range(i, i + 1)));
            }
        }
        _segnalibri.AddRange(segnalibri);
        _note.AddRange(note);
        _immagini.AddRange(figure);
        _commenti.AddRange(intervalliCommenti.Select(c => (commenti[c.Key], c.Value)));
        _testo.Select(0, 0);
        _testo.ClearUndo();
    }

    /// <summary>
    /// L'RTF del documento con segnalibri, note e immagini come segnaposto, e i loro dati. Il campo di testo non
    /// cambia: si lavora su una copia invisibile, così il testo, l'annullamento e il cursore restano come sono.
    /// </summary>
    private string RtfConSegnaposto(out Segnaposti segnaposti)
    {
        var segnalibri = ElencoSegnalibri();
        var note = ElencoNote();
        var immagini = ElencoImmagini();
        var commenti = ElencoCommenti();
        segnaposti = new Segnaposti(segnalibri.Select(s => s.Nome).ToList(), note.Select(n => n.Testo).ToList(),
            immagini.Select(i => i.Immagine).ToList(), commenti.Select(c => c.Commento).ToList());
        if (segnaposti.Vuoti)
            return _testo.Rtf ?? "";

        // Dalla fine all'inizio; a parità di posizione prima note e immagini, così il segnalibro resta davanti.
        var operazioni = segnalibri.Select((s, i) => (Inizio: s.Posizione, Fine: s.Posizione, Carattere: Segnalibri.Segnaposto(i), Ordine: 1))
            .Concat(note.Select((n, i) => (Inizio: n.Inizio, Fine: n.Fine, Carattere: Note.Segnaposto(i), Ordine: 0)))
            .Concat(immagini.Select((m, i) => (Inizio: m.Posizione, Fine: m.Posizione + 1, Carattere: Immagini.Segnaposto(i), Ordine: 0)))
            // La fine prima dell'inizio, così in un commento su un punto l'inizio resta davanti.
            .Concat(commenti.Select((c, i) => (Inizio: c.Fine, Fine: c.Fine, Carattere: Commenti.Fine(i), Ordine: 2)))
            .Concat(commenti.Select((c, i) => (Inizio: c.Inizio, Fine: c.Inizio, Carattere: Commenti.Inizio(i), Ordine: 3)))
            .OrderByDescending(o => o.Inizio).ThenBy(o => o.Ordine);
        using var copia = new RichTextBox();
        copia.CreateControl();
        copia.Rtf = _testo.Rtf;
        foreach (var o in operazioni)
        {
            copia.Select(o.Inizio, o.Fine - o.Inizio);
            copia.SelectedText = o.Carattere.ToString();
        }
        return copia.Rtf ?? "";
    }

    /// <summary>L'RTF da salvare: quello del campo di testo con segnalibri, note, commenti, immagini e intestazioni delle tabelle.</summary>
    private string RtfDaSalvare()
    {
        var rtf = RtfConSegnaposto(out var segnaposti);
        if (segnaposti.Segnalibri.Count > 0)
            rtf = Segnalibri.DaSegnaposto(rtf, segnaposti.Segnalibri);
        if (segnaposti.Note.Count > 0)
            rtf = Note.DaSegnaposto(rtf, segnaposti.Note);
        if (segnaposti.Commenti.Count > 0)
            rtf = Commenti.DaSegnaposto(rtf, segnaposti.Commenti);
        return IntestazioniInRtf(segnaposti.Immagini.Count > 0 ? Immagini.DaSegnaposto(rtf, segnaposti.Immagini) : rtf);
    }

    /// <summary>Il documento come modello, con segnalibri, note e immagini (per salvare in DOCX e per leggerlo).</summary>
    private ContenutoDocumento ModelloCompleto()
    {
        var contenuto = RtfLettore.Leggi(RtfConSegnaposto(out var segnaposti));
        contenuto.Segnalibri.AddRange(segnaposti.Segnalibri);
        contenuto.Note.AddRange(segnaposti.Note);
        contenuto.Immagini.AddRange(segnaposti.Immagini);
        contenuto.Commenti.AddRange(segnaposti.Commenti);
        SegnaIntestazioni(contenuto);
        return contenuto;
    }

    // ---------- Comandi ----------

    private void AggiungiSegnalibro()
    {
        int posizione = _testo.SelectionStart;
        // Proposta: le prime parole del paragrafo, senza spezzarne una.
        var proposto = Riassunto(_testo.Text, InizioParagrafoCorrente()).TrimEnd('.');
        if (proposto.Length > 30)
            proposto = proposto[..(proposto.LastIndexOf(' ', 30) is var spazio and > 0 ? spazio : 30)];
        if (proposto == "(riga vuota)")
            proposto = $"Segnalibro {_segnalibri.Count + 1}";

        using var finestra = new FinestraNome("Aggiungi segnalibro", "&Nome del segnalibro:", proposto);
        if (finestra.ShowDialog(this) != DialogResult.OK || Tom is not { } tom)
            return;
        var nome = finestra.Nome;
        // Un nome usato di nuovo sposta il segnalibro qui, come in Word.
        bool spostato = _segnalibri.RemoveAll(s => string.Equals(s.Nome, nome, StringComparison.CurrentCultureIgnoreCase)) > 0;
        _segnalibri.Add((nome, tom.Range(posizione, posizione)));
        _testo.Modified = true;
        AnnunciaTraPoco(spostato ? $"Segnalibro {nome} spostato qui" : $"Segnalibro {nome} aggiunto");
    }

    private void SegnalibroVicino(bool avanti)
    {
        var segnalibri = ElencoSegnalibri();
        int qui = _testo.SelectionStart;
        var trovato = avanti ? segnalibri.Where(s => s.Posizione > qui).Cast<(string, int)?>().FirstOrDefault()
                             : segnalibri.Where(s => s.Posizione < qui).Cast<(string, int)?>().LastOrDefault();
        if (trovato is var (nome, posizione))
            VaiAlSegnalibro(nome, posizione);
        else
            _annunciatore.Annuncia(segnalibri.Count == 0 ? "Nessun segnalibro nel documento" : avanti ? "Nessun segnalibro dopo" : "Nessun segnalibro prima");
    }

    private void VaiAlSegnalibro(string nome, int posizione)
    {
        _testo.Select(posizione, 0);
        _testo.ScrollToCaret();
        _annunciatore.Annuncia($"Segnalibro {nome}. {DescriviRiga(_testo.Text, posizione)}");
    }

    /// <summary>Elimina il segnalibro dove si trova il cursore, o se non c'è il primo del paragrafo.</summary>
    private void EliminaSegnalibro()
    {
        int qui = _testo.SelectionStart, inizio = InizioParagrafoCorrente();
        int fine = _testo.Text.IndexOf('\n', qui) is var f and >= 0 ? f : _testo.TextLength;
        var candidati = ElencoSegnalibri().Where(s => s.Posizione >= inizio && s.Posizione <= fine).ToList();
        if (candidati.Count == 0)
        {
            _annunciatore.Annuncia("Nessun segnalibro in questo paragrafo");
            return;
        }
        var scelto = candidati.OrderBy(s => Math.Abs(s.Posizione - qui)).First();
        _segnalibri.RemoveAll(s => s.Nome == scelto.Nome);
        _testo.Modified = true;
        _annunciatore.Annuncia($"Segnalibro {scelto.Nome} eliminato");
    }

    private void MostraElencoSegnalibri()
    {
        var segnalibri = ElencoSegnalibri();
        var inizi = Paginatore.InizioPagine(_testo, _pagina);
        var testo = _testo.Text;
        // La "posizione" di ogni voce è il suo numero nell'elenco: più segnalibri possono stare nello stesso punto.
        var voci = segnalibri.Select((s, i) => new VoceElenco(
            [s.Nome, $"Pagina {Paginatore.PaginaDi(inizi, s.Posizione)}, {Riassunto(testo, testo.LastIndexOf('\n', Math.Max(0, s.Posizione - 1)) + 1)}"], i)).ToList();
        using var finestra = new FinestraElenco("Segnalibri", ["Nome", "Dove"], voci);
        finestra.ShowDialog(this);
        if (finestra.DaEliminare.Count > 0)
        {
            var nomi = finestra.DaEliminare.Select(i => segnalibri[i].Nome).ToHashSet();
            _segnalibri.RemoveAll(s => nomi.Contains(s.Nome));
            _testo.Modified = true;
            AnnunciaTraPoco(nomi.Count == 1 ? "Segnalibro eliminato" : $"{nomi.Count} segnalibri eliminati");
        }
        else if (finestra.Destinazione is int indice)
        {
            BeginInvoke(() => VaiAlSegnalibro(segnalibri[indice].Nome, segnalibri[indice].Posizione));
        }
    }
}

/// <summary>Chiede un nome (per esempio di un segnalibro).</summary>
public sealed class FinestraNome : Modulo
{
    private readonly TextBox _nome;

    public FinestraNome(string titolo, string etichetta, string proposto) : base(titolo, 280)
    {
        _nome = Campo(etichetta, new TextBox { Text = proposto });
        AggiungiPulsanti();
        ActiveControl = _nome;
        _nome.SelectAll();
        FormClosing += (_, e) =>
        {
            if (DialogResult == DialogResult.OK && Nome.Length == 0)
            {
                e.Cancel = true;
                MessageBox.Show(this, "Scrivi un nome.", titolo, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        };
    }

    public string Nome => _nome.Text.Trim();
}
