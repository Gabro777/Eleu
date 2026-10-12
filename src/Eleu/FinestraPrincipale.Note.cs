using Eleu.Documento;
using Eleu.Editor;
using Eleu.Interfaccia;

namespace Eleu;

// Note a piè di pagina (Inserisci > Note). Nel testo il richiamo è il numero in apice, un intervallo del
// Text Object Model che segue il testo; il testo della nota sta a parte e si legge con Alt+Invio.
public sealed partial class FinestraPrincipale
{
    private const int TomVero = -1;

    private readonly List<(string Testo, dynamic Intervallo)> _note = [];
    private List<(string Testo, int Inizio, int Fine)>? _noteInSospeso;

    private void PreparaNote()
    {
        // Come per i segnalibri: se il campo ricrea la sua finestra, gli intervalli vanno ricreati.
        _testo.HandleDestroyed += (_, _) =>
        {
            _noteInSospeso = ElencoNote();
            _note.Clear();
        };
        _testo.HandleCreated += (_, _) =>
        {
            if (_noteInSospeso is { } sospese)
                BeginInvoke(() =>
                {
                    if (Tom is { } tom)
                        _note.AddRange(sospese.Select(n => (n.Testo, (dynamic)tom.Range(n.Inizio, n.Fine))));
                });
            _noteInSospeso = null;
        };
    }

    /// <summary>
    /// Le note in ordine di posizione. Una nota il cui richiamo è stato cancellato scrivendo (intervallo vuoto)
    /// non c'è più.
    /// </summary>
    private List<(string Testo, int Inizio, int Fine)> ElencoNote()
    {
        _note.RemoveAll(n => (int)n.Intervallo.Start >= (int)n.Intervallo.End);
        return _note.Select(n => (n.Testo, Inizio: (int)n.Intervallo.Start, Fine: (int)n.Intervallo.End))
            .OrderBy(n => n.Inizio).ToList();
    }

    /// <summary>Riscrive i numeri dei richiami (1, 2, 3...) nell'ordine in cui stanno nel testo.</summary>
    private void RinumeraNote()
    {
        ElencoNote();
        var ordinate = _note.OrderBy(n => (int)n.Intervallo.Start).ToList();
        for (int i = 0; i < ordinate.Count; i++)
        {
            var richiamo = ordinate[i].Intervallo;
            var numero = (i + 1).ToString();
            if ((string)richiamo.Text != numero)
                richiamo.Text = numero;
            richiamo.Font.Superscript = TomVero;
        }
    }

    /// <summary>La nota del richiamo dove si trova il cursore (dentro o subito dopo), con il suo numero.</summary>
    private (int Numero, string Testo, int Inizio, int Fine)? NotaQui()
    {
        int qui = _testo.SelectionStart;
        var note = ElencoNote();
        int i = note.FindIndex(n => qui >= n.Inizio && qui <= n.Fine);
        return i < 0 ? null : (i + 1, note[i].Testo, note[i].Inizio, note[i].Fine);
    }

    private void AggiungiNota()
    {
        using var finestra = new FinestraNota("Aggiungi nota", "");
        if (finestra.ShowDialog(this) != DialogResult.OK || Tom is not { } tom)
            return;
        int posizione = _testo.SelectionStart + _testo.SelectionLength; // dopo la parola o la selezione
        var richiamo = tom.Range(posizione, posizione);
        richiamo.Text = "0";
        richiamo.Font.Superscript = TomVero;
        _note.Add((finestra.Testo, richiamo));
        RinumeraNote();

        // Si continua a scrivere dopo il richiamo, in carattere normale e non in apice.
        _testo.Select((int)richiamo.End, 0);
        RichEditNativo.ImpostaFormatoCarattere(_testo, new FormatoCarattere(Posizione: PosizioneTesto.Normale));
        _testo.Modified = true;
        AnnunciaTraPoco($"Nota {NotaQui()?.Numero} aggiunta");
    }

    private void ModificaNota()
    {
        if (NotaQui() is not (int numero, string testo, int inizio, _))
        {
            _annunciatore.Annuncia("Il cursore non è su una nota");
            return;
        }
        using var finestra = new FinestraNota($"Modifica nota {numero}", testo);
        if (finestra.ShowDialog(this) != DialogResult.OK)
            return;
        int indice = _note.FindIndex(n => (int)n.Intervallo.Start == inizio);
        _note[indice] = (finestra.Testo, _note[indice].Intervallo);
        _testo.Modified = true;
        AnnunciaTraPoco($"Nota {numero} modificata");
    }

    private void EliminaNota()
    {
        if (NotaQui() is not (int numero, _, int inizio, _))
        {
            _annunciatore.Annuncia("Il cursore non è su una nota");
            return;
        }
        EliminaNote([inizio]);
        _annunciatore.Annuncia($"Nota {numero} eliminata");
    }

    private void EliminaNote(IEnumerable<int> inizi)
    {
        var daTogliere = inizi.ToHashSet();
        foreach (var nota in _note.Where(n => daTogliere.Contains((int)n.Intervallo.Start)).ToList())
        {
            nota.Intervallo.Text = "";
            _note.Remove(nota);
        }
        RinumeraNote();
        _testo.Modified = true;
    }

    private void NotaVicina(bool avanti)
    {
        var note = ElencoNote();
        int qui = _testo.SelectionStart;
        int i = avanti ? note.FindIndex(n => n.Inizio > qui) : note.FindLastIndex(n => n.Fine < qui);
        if (i < 0)
        {
            _annunciatore.Annuncia(note.Count == 0 ? "Nessuna nota nel documento" : avanti ? "Nessuna nota dopo" : "Nessuna nota prima");
            return;
        }
        VaiAllaNota(i + 1, note[i].Testo, note[i].Fine);
    }

    private void VaiAllaNota(int numero, string testo, int fine)
    {
        _testo.Select(fine, 0);
        _testo.ScrollToCaret();
        _annunciatore.Annuncia($"Nota {numero}: {testo}");
    }

    private void MostraElencoNote()
    {
        var note = ElencoNote();
        var voci = note.Select((n, i) => new VoceElenco([$"Nota {i + 1}", n.Testo.ReplaceLineEndings(" ")], n.Inizio)).ToList();
        using var finestra = new FinestraElenco("Note", ["Nota", "Testo"], voci);
        finestra.ShowDialog(this);
        if (finestra.DaEliminare.Count > 0)
        {
            EliminaNote(finestra.DaEliminare);
            AnnunciaTraPoco(finestra.DaEliminare.Count == 1 ? "Nota eliminata" : $"{finestra.DaEliminare.Count} note eliminate");
        }
        else if (finestra.Destinazione is int inizio)
        {
            int i = note.FindIndex(n => n.Inizio == inizio);
            BeginInvoke(() => VaiAllaNota(i + 1, note[i].Testo, note[i].Fine));
        }
    }

    /// <summary>
    /// Il documento per il Braille e l'audiolibro (vedi <see cref="Leggibile"/>): note in fondo, immagini con la
    /// loro descrizione, tabelle riga per riga.
    /// </summary>
    private ContenutoDocumento ContenutoConNote() => Leggibile.Da(ModelloCompleto());

    /// <summary>Per Alt+Invio: se il cursore è su una nota, il suo testo.</summary>
    private string? DescrizioneNotaQui() => NotaQui() is (int numero, string testo, _, _) ? $"Nota {numero}: {testo.TrimEnd('.')}. " : null;
}

/// <summary>Scrive o modifica il testo di una nota; Invio va a capo, Ctrl+Invio o OK conferma.</summary>
public sealed class FinestraNota : Modulo
{
    private readonly TextBox _testo;

    public FinestraNota(string titolo, string testo) : base(titolo, 420)
    {
        _testo = Campo("&Testo della nota:", new TextBox
        {
            Text = testo.ReplaceLineEndings(Environment.NewLine),
            Multiline = true,
            AcceptsReturn = true,
            ScrollBars = ScrollBars.Vertical,
            Height = 120,
        });
        AggiungiPulsanti();
        ActiveControl = _testo;
        _testo.KeyDown += (_, e) =>
        {
            if (e.KeyData == (Keys.Control | Keys.Enter))
            {
                e.SuppressKeyPress = true;
                DialogResult = DialogResult.OK;
            }
        };
        FormClosing += (_, e) =>
        {
            if (DialogResult == DialogResult.OK && Testo.Length == 0)
            {
                e.Cancel = true;
                MessageBox.Show(this, "Scrivi il testo della nota.", titolo, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        };
    }

    public string Testo => _testo.Text.ReplaceLineEndings("\n").Trim();
}
