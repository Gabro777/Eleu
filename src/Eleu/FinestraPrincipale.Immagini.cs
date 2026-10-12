using Eleu.Documento;
using Eleu.Interfaccia;

namespace Eleu;

// Immagini (Inserisci > Immagini). Nel testo sono oggetti del campo di testo, che le mostra e le stampa; Eleu
// tiene da parte i dati e la descrizione, che è ciò che "vede" chi usa lo screen reader. Ogni immagine è un
// intervallo del Text Object Model lungo un carattere, che segue il testo mentre si scrive.
public sealed partial class FinestraPrincipale
{
    private readonly List<(Immagine Immagine, dynamic Intervallo)> _immagini = [];
    private List<(Immagine Immagine, int Posizione)>? _immaginiInSospeso;
    private int? _ultimaImmagine;

    private void PreparaImmagini()
    {
        // Come per segnalibri e note: se il campo ricrea la sua finestra, gli intervalli vanno ricreati.
        _testo.HandleDestroyed += (_, _) =>
        {
            _immaginiInSospeso = ElencoImmagini();
            _immagini.Clear();
        };
        _testo.HandleCreated += (_, _) =>
        {
            if (_immaginiInSospeso is { } sospese)
                BeginInvoke(() =>
                {
                    if (Tom is { } tom)
                        _immagini.AddRange(sospese.Select(i => (i.Immagine, (dynamic)tom.Range(i.Posizione, i.Posizione + 1))));
                });
            _immaginiInSospeso = null;
        };
    }

    /// <summary>Le immagini in ordine di posizione; quelle cancellate scrivendo (intervallo vuoto) non ci sono più.</summary>
    private List<(Immagine Immagine, int Posizione)> ElencoImmagini()
    {
        _immagini.RemoveAll(i => (int)i.Intervallo.Start >= (int)i.Intervallo.End);
        return _immagini.Select(i => (i.Immagine, Posizione: (int)i.Intervallo.Start)).OrderBy(i => i.Posizione).ToList();
    }

    /// <summary>L'immagine dove si trova il cursore (subito prima o subito dopo), con il suo numero.</summary>
    private (int Numero, Immagine Immagine, int Posizione)? ImmagineQui()
    {
        int qui = _testo.SelectionStart;
        var immagini = ElencoImmagini();
        int i = immagini.FindIndex(m => m.Posizione == qui);
        if (i < 0 && _testo.SelectionLength == 0)
            i = immagini.FindIndex(m => m.Posizione == qui - 1);
        return i < 0 ? null : (i + 1, immagini[i].Immagine, immagini[i].Posizione);
    }

    private static string Descrivi(int numero, Immagine immagine) =>
        $"Immagine {numero}: {(immagine.Descrizione.Length > 0 ? immagine.Descrizione : "senza descrizione")}, {immagine.Misure}";

    /// <summary>Arrivando su un'immagine con le frecce, Eleu ne dice la descrizione.</summary>
    private void AnnunciaImmagine()
    {
        if (_celleInSilenzio || !_testo.Focused || _immagini.Count == 0 || !_impostazioni.AnnunciaImmagini)
            return;
        var qui = ImmagineQui();
        if (qui?.Posizione == _ultimaImmagine)
            return;
        _ultimaImmagine = qui?.Posizione;
        if (qui is var (numero, immagine, _))
            _annunciatore.Annuncia(Descrivi(numero, immagine));
    }

    /// <summary>Per Alt+Invio: se il cursore è su un'immagine, la sua descrizione.</summary>
    private string? DescrizioneImmagineQui() => ImmagineQui() is var (numero, immagine, _) ? Descrivi(numero, immagine) + ". " : null;

    private void AggiungiImmagine()
    {
        using var dialogo = new OpenFileDialog
        {
            Title = "Aggiungi immagine",
            Filter = "Immagini|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.tif;*.tiff|Tutti i file|*.*",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        };
        if (dialogo.ShowDialog(this) != DialogResult.OK)
            return;
        Immagine immagine;
        try
        {
            immagine = Immagini.DaDati(File.ReadAllBytes(dialogo.FileName), "");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or OutOfMemoryException)
        {
            MessageBox.Show(this, $"Impossibile aprire l'immagine {Path.GetFileName(dialogo.FileName)}.", NomeApp, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        using var finestra = new FinestraImmagine("Aggiungi immagine", immagine);
        if (finestra.ShowDialog(this) != DialogResult.OK || Tom is not { } tom)
            return;
        immagine = finestra.Risultato;
        int posizione = _testo.SelectionStart + _testo.SelectionLength;
        _testo.Select(posizione, 0);
        _testo.SelectedRtf = Immagini.RtfSoloImmagine(immagine);
        _immagini.Add((immagine, tom.Range(posizione, posizione + 1)));
        _ultimaImmagine = posizione;
        _testo.Select(posizione, 0);
        _testo.Modified = true;
        AnnunciaTraPoco($"Immagine aggiunta. {Descrivi(ImmagineQui()?.Numero ?? 1, immagine)}");
    }

    private void ModificaImmagine()
    {
        if (ImmagineQui() is not var (numero, immagine, posizione) || Tom is not { } tom)
        {
            _annunciatore.Annuncia("Il cursore non è su un'immagine");
            return;
        }
        using var finestra = new FinestraImmagine($"Modifica immagine {numero}", immagine);
        if (finestra.ShowDialog(this) != DialogResult.OK)
            return;
        var nuova = finestra.Risultato;
        _immagini.RemoveAll(i => (int)i.Intervallo.Start == posizione);
        // Se cambia la misura si rimette l'immagine; se cambia solo la descrizione basta ricordarla.
        if (nuova.Larghezza != immagine.Larghezza)
        {
            _testo.Select(posizione, 1);
            _testo.SelectedRtf = Immagini.RtfSoloImmagine(nuova);
            _testo.Select(posizione, 0);
        }
        _immagini.Add((nuova, tom.Range(posizione, posizione + 1)));
        _testo.Modified = true;
        AnnunciaTraPoco($"Immagine modificata. {Descrivi(numero, nuova)}");
    }

    private void EliminaImmagine()
    {
        if (ImmagineQui() is not var (numero, _, posizione))
        {
            _annunciatore.Annuncia("Il cursore non è su un'immagine");
            return;
        }
        EliminaImmagini([posizione]);
        _annunciatore.Annuncia($"Immagine {numero} eliminata");
    }

    private void EliminaImmagini(IEnumerable<int> posizioni)
    {
        foreach (int posizione in posizioni.OrderDescending())
        {
            _immagini.RemoveAll(i => (int)i.Intervallo.Start == posizione);
            _testo.Select(posizione, 1);
            _testo.SelectedText = "";
        }
        _ultimaImmagine = null;
        _testo.Modified = true;
    }

    private void ImmagineVicina(bool avanti)
    {
        var immagini = ElencoImmagini();
        int qui = _testo.SelectionStart;
        int i = avanti ? immagini.FindIndex(m => m.Posizione > qui) : immagini.FindLastIndex(m => m.Posizione < qui - 1);
        if (i < 0)
        {
            _annunciatore.Annuncia(immagini.Count == 0 ? "Nessuna immagine nel documento" : avanti ? "Nessuna immagine dopo" : "Nessuna immagine prima");
            return;
        }
        VaiAllImmagine(i + 1, immagini[i].Immagine, immagini[i].Posizione);
    }

    private void VaiAllImmagine(int numero, Immagine immagine, int posizione)
    {
        _ultimaImmagine = posizione;
        _testo.Select(posizione, 0);
        _testo.ScrollToCaret();
        _annunciatore.Annuncia(Descrivi(numero, immagine));
    }

    private void MostraElencoImmagini()
    {
        var immagini = ElencoImmagini();
        var voci = immagini.Select((m, i) => new VoceElenco(
            [$"Immagine {i + 1}", m.Immagine.Descrizione.Length > 0 ? m.Immagine.Descrizione : "senza descrizione", m.Immagine.Misure], m.Posizione)).ToList();
        using var finestra = new FinestraElenco("Immagini", ["Immagine", "Descrizione", "Misure"], voci);
        finestra.ShowDialog(this);
        if (finestra.DaEliminare.Count > 0)
        {
            EliminaImmagini(finestra.DaEliminare);
            AnnunciaTraPoco(finestra.DaEliminare.Count == 1 ? "Immagine eliminata" : $"{finestra.DaEliminare.Count} immagini eliminate");
        }
        else if (finestra.Destinazione is int posizione)
        {
            int i = immagini.FindIndex(m => m.Posizione == posizione);
            BeginInvoke(() => VaiAllImmagine(i + 1, immagini[i].Immagine, posizione));
        }
    }

    private void SalvaImmagine()
    {
        if (ImmagineQui() is not var (numero, immagine, _))
        {
            _annunciatore.Annuncia("Il cursore non è su un'immagine");
            return;
        }
        var estensione = immagine.Formato == "jpeg" ? "jpg" : "png";
        using var dialogo = new SaveFileDialog
        {
            Title = "Salva immagine con nome",
            Filter = estensione == "jpg" ? "Immagine JPEG|*.jpg" : "Immagine PNG|*.png",
            FileName = $"Immagine {numero}.{estensione}",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            OverwritePrompt = true,
        };
        if (dialogo.ShowDialog(this) != DialogResult.OK)
            return;
        try
        {
            File.WriteAllBytes(dialogo.FileName, immagine.Dati);
            AnnunciaTraPoco($"Immagine salvata in {Path.GetFileName(dialogo.FileName)}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"Impossibile salvare l'immagine.\n\n{e.Message}", NomeApp, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// Inserisci > Immagini > Grafica in rilievo: l'immagine su cui si è (o una scelta dal disco) in punti Braille
    /// per la stampante Braille, oppure come disegno per la carta a microcapsule.
    /// </summary>
    private void GraficaInRilievo()
    {
        byte[] dati;
        string descrizione;
        if (ImmagineQui() is var (_, immagine, _))
        {
            dati = immagine.Dati;
            descrizione = immagine.Descrizione;
        }
        else
        {
            using var dialogo = new OpenFileDialog
            {
                Title = "Scegli l'immagine da mettere in rilievo",
                Filter = "Immagini|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff|Tutti i file|*.*",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            };
            if (dialogo.ShowDialog(this) != DialogResult.OK)
                return;
            try
            {
                dati = File.ReadAllBytes(dialogo.FileName);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                MessageBox.Show(this, $"Impossibile leggere l'immagine.\n\n{e.Message}", NomeApp, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            descrizione = Path.GetFileNameWithoutExtension(dialogo.FileName);
        }

        Bitmap figura;
        try
        {
            using var flusso = new MemoryStream(dati);
            using var letta = Image.FromStream(flusso);
            figura = new Bitmap(letta);
        }
        catch (ArgumentException)
        {
            MessageBox.Show(this, "Il file non è un'immagine che Eleu sa leggere.", NomeApp, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        using (figura)
        using (var finestra = new FinestraGraficaTattile(figura, descrizione, Eleu.Braille.StampanteBraille.Installate(), _impostazioni.StampanteBraille,
                   _impostazioni.CelleBraille, _impostazioni.RigheBraille, TraduciSePossibile))
        {
            finestra.ShowDialog(this);
            if (finestra.StampanteScelta is string stampante)
                _impostazioni.StampanteBraille = stampante;
        }
    }

    private static string? TraduciSePossibile(string testo)
    {
        try
        {
            return Eleu.Braille.TraduttoreBraille.Traduci(testo);
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }
}

/// <summary>
/// Descrizione e larghezza di un'immagine. La descrizione è ciò che legge lo screen reader: senza, chi non vede
/// non saprà cosa c'è, quindi Eleu la chiede.
/// </summary>
public sealed class FinestraImmagine : Modulo
{
    private readonly Immagine _immagine;
    private readonly TextBox _descrizione;
    private readonly NumeroConUnita _larghezza;

    public FinestraImmagine(string titolo, Immagine immagine) : base(titolo, 360)
    {
        _immagine = immagine;
        _descrizione = Campo("&Descrizione:", new TextBox
        {
            Text = immagine.Descrizione,
            Multiline = true,
            Height = 80,
            ScrollBars = ScrollBars.Vertical,
        });
        _larghezza = Campo("&Larghezza:", new NumeroConUnita("cm", (decimal)(immagine.Larghezza * 2.54f / 72f), 0.5m, 40, 0.5m, 1));
        RigaIntera(new Label
        {
            AutoSize = true,
            MaximumSize = new Size(480, 0),
            Margin = new Padding(0, 8, 0, 0),
            Text = "La descrizione è ciò che legge lo screen reader al posto dell'immagine. L'altezza segue la larghezza.",
        });
        AggiungiPulsanti();
        ActiveControl = _descrizione;
        FormClosing += (_, e) =>
        {
            if (DialogResult != DialogResult.OK || Descrizione.Length > 0)
                return;
            var risposta = MessageBox.Show(this, "L'immagine non ha una descrizione: chi usa lo screen reader non saprà cosa mostra. Continuare senza?",
                titolo, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
            e.Cancel = risposta != DialogResult.Yes;
        };
    }

    private string Descrizione => _descrizione.Text.ReplaceLineEndings(" ").Trim();

    public Immagine Risultato =>
        Immagini.ConLarghezza(_immagine, (float)_larghezza.Value * 72f / 2.54f) with { Descrizione = Descrizione };
}
