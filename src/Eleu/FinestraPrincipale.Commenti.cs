using Eleu.Documento;
using Eleu.Interfaccia;

namespace Eleu;

// Commenti (Inserisci > Commenti): note a margine su un pezzo di testo, come quelle che lascia chi corregge un
// documento in Word o in LibreOffice. Ogni commento è un intervallo del Text Object Model che segue il testo;
// autore e testo stanno a parte. Arrivando su un testo commentato Eleu lo segnala (suono o voce, dalle Opzioni).
public sealed partial class FinestraPrincipale
{
    private readonly List<(Commento Commento, dynamic Intervallo)> _commenti = [];
    private List<(Commento Commento, int Inizio, int Fine)>? _commentiInSospeso;

    private void PreparaCommenti()
    {
        // Come per segnalibri e note: se il campo ricrea la sua finestra, gli intervalli vanno ricreati.
        _testo.HandleDestroyed += (_, _) =>
        {
            _commentiInSospeso = ElencoCommenti();
            _commenti.Clear();
        };
        _testo.HandleCreated += (_, _) =>
        {
            if (_commentiInSospeso is { } sospesi)
                BeginInvoke(() =>
                {
                    if (Tom is { } tom)
                        _commenti.AddRange(sospesi.Select(c => (c.Commento, (dynamic)tom.Range(c.Inizio, c.Fine))));
                });
            _commentiInSospeso = null;
        };
    }

    /// <summary>I commenti in ordine di posizione, con l'intervallo di testo commentato.</summary>
    private List<(Commento Commento, int Inizio, int Fine)> ElencoCommenti() =>
        _commenti.Select(c => (c.Commento, Inizio: Math.Clamp((int)c.Intervallo.Start, 0, _testo.TextLength), Fine: Math.Clamp((int)c.Intervallo.End, 0, _testo.TextLength)))
            .OrderBy(c => c.Inizio).ThenBy(c => c.Fine).ToList();

    /// <summary>Il commento sul testo dove si trova il cursore (il più stretto, se sono uno dentro l'altro), con il suo numero.</summary>
    private (int Numero, Commento Commento, int Inizio, int Fine)? CommentoQui()
    {
        int qui = _testo.SelectionStart;
        var commenti = ElencoCommenti();
        var trovati = commenti.Select((c, i) => (Numero: i + 1, c.Commento, c.Inizio, c.Fine))
            .Where(c => qui >= c.Inizio && (qui < c.Fine || qui == c.Inizio))
            .OrderBy(c => c.Fine - c.Inizio).ToList();
        return trovati.Count == 0 ? null : trovati[0];
    }

    /// <summary>Il testo commentato, abbreviato, per dire su cosa è il commento.</summary>
    private string TestoCommentato(int inizio, int fine)
    {
        var testo = StrutturaTesto.Leggibile(_testo.Text[inizio..Math.Max(inizio, fine)]).ReplaceLineEndings(" ").Trim();
        return testo.Length == 0 ? "" : testo.Length > 60 ? testo[..60] + "..." : testo;
    }

    private string DescriviCommento(int numero, Commento commento, int inizio, int fine)
    {
        var su = TestoCommentato(inizio, fine);
        var autore = commento.Autore.Length > 0 ? $" di {commento.Autore}" : "";
        var testo = commento.Testo.ReplaceLineEndings(" ").Trim();
        // Dopo un testo che finisce già con la sua punteggiatura non si aggiunge il punto.
        var stacco = testo.Length > 0 && ".!?:;…".Contains(testo[^1]) ? " " : ". ";
        return $"Commento {numero}{autore}: {testo}{(su.Length > 0 ? $"{stacco}Sul testo: {su}" : "")}";
    }

    /// <summary>
    /// Ctrl+Alt+M: commenta la selezione o, senza selezione, la parola in cui si trova il cursore.
    /// </summary>
    private void AggiungiCommento()
    {
        int inizio = _testo.SelectionStart, fine = inizio + _testo.SelectionLength;
        if (fine == inizio)
            (inizio, fine) = ParolaIntorno(_testo.Text, Math.Min(inizio, _testo.TextLength));
        using var finestra = new FinestraCommento("Aggiungi commento", "", _impostazioni.AutoreCommenti ?? Environment.UserName);
        if (finestra.ShowDialog(this) != DialogResult.OK || Tom is not { } tom)
            return;
        if (_commenti.Count >= Commenti.Massimo)
        {
            AnnunciaTraPoco($"Un documento può avere al massimo {Commenti.Massimo} commenti");
            return;
        }
        _impostazioni.AutoreCommenti = finestra.Autore;
        _commenti.Add((new Commento(finestra.Testo, finestra.Autore, DateTime.Now), tom.Range(inizio, fine)));
        _testo.Modified = true;
        AnnunciaTraPoco(fine > inizio ? $"Commento aggiunto su: {TestoCommentato(inizio, fine)}" : "Commento aggiunto");
    }

    private void ModificaCommento()
    {
        if (CommentoQui() is not var (numero, commento, inizio, fine))
        {
            _annunciatore.Annuncia("Il cursore non è su un testo commentato");
            return;
        }
        using var finestra = new FinestraCommento($"Modifica commento {numero}", commento.Testo, commento.Autore);
        if (finestra.ShowDialog(this) != DialogResult.OK)
            return;
        int indice = _commenti.FindIndex(c => ReferenceEquals(c.Commento, commento));
        _commenti[indice] = (commento with { Testo = finestra.Testo, Autore = finestra.Autore }, _commenti[indice].Intervallo);
        _testo.Modified = true;
        AnnunciaTraPoco($"Commento {numero} modificato");
    }

    /// <summary>Toglie il commento dove si trova il cursore; il testo commentato resta.</summary>
    private void EliminaCommento()
    {
        if (CommentoQui() is not var (numero, commento, _, _))
        {
            _annunciatore.Annuncia("Il cursore non è su un testo commentato");
            return;
        }
        _commenti.RemoveAll(c => ReferenceEquals(c.Commento, commento));
        _testo.Modified = true;
        _annunciatore.Annuncia($"Commento {numero} eliminato");
    }

    private void CommentoVicino(bool avanti)
    {
        var commenti = ElencoCommenti();
        int qui = _testo.SelectionStart;
        int i = avanti ? commenti.FindIndex(c => c.Inizio > qui) : commenti.FindLastIndex(c => c.Inizio < qui);
        if (i < 0)
        {
            _annunciatore.Annuncia(commenti.Count == 0 ? "Nessun commento nel documento" : avanti ? "Nessun commento dopo" : "Nessun commento prima");
            return;
        }
        VaiAlCommento(i + 1, commenti[i]);
    }

    private void VaiAlCommento(int numero, (Commento Commento, int Inizio, int Fine) c)
    {
        _ultimoCommentoSegnalato = c.Inizio; // l'annuncio c'è già: niente segnale in più
        _testo.Select(c.Inizio, 0);
        _testo.ScrollToCaret();
        _annunciatore.Annuncia(DescriviCommento(numero, c.Commento, c.Inizio, c.Fine));
    }

    private void MostraElencoCommenti()
    {
        var commenti = ElencoCommenti();
        var voci = commenti.Select((c, i) => new VoceElenco(
            [c.Commento.Autore, c.Commento.Testo.ReplaceLineEndings(" "), TestoCommentato(c.Inizio, c.Fine)], i)).ToList();
        using var finestra = new FinestraElenco("Commenti", ["Autore", "Commento", "Sul testo"], voci);
        finestra.ShowDialog(this);
        if (finestra.DaEliminare.Count > 0)
        {
            var togliere = finestra.DaEliminare.Select(i => commenti[i].Commento).ToHashSet();
            _commenti.RemoveAll(c => togliere.Contains(c.Commento));
            _testo.Modified = true;
            AnnunciaTraPoco(togliere.Count == 1 ? "Commento eliminato" : $"{togliere.Count} commenti eliminati");
        }
        else if (finestra.Destinazione is int indice)
        {
            BeginInvoke(() => VaiAlCommento(indice + 1, commenti[indice]));
        }
    }

    /// <summary>Per Alt+Invio: se il cursore è su un testo commentato, il commento.</summary>
    private string? DescrizioneCommentoQui() =>
        CommentoQui() is var (numero, commento, inizio, fine) ? DescriviCommento(numero, commento, inizio, fine) + ". " : null;
}

/// <summary>Scrive o modifica un commento: il testo (Invio va a capo, Ctrl+Invio conferma) e l'autore.</summary>
public sealed class FinestraCommento : Modulo
{
    private readonly TextBox _testo, _autore;

    public FinestraCommento(string titolo, string testo, string autore) : base(titolo, 420)
    {
        _testo = Campo("&Testo del commento:", new TextBox
        {
            Text = testo.ReplaceLineEndings(Environment.NewLine),
            Multiline = true,
            AcceptsReturn = true,
            ScrollBars = ScrollBars.Vertical,
            Height = 120,
        });
        _autore = Campo("&Autore:", new TextBox { Text = autore });
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
                MessageBox.Show(this, "Scrivi il testo del commento.", titolo, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        };
    }

    public string Testo => _testo.Text.ReplaceLineEndings("\n").Trim();

    public string Autore => _autore.Text.Trim();
}
