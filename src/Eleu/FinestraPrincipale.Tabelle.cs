using Eleu.Documento;
using Eleu.Interfaccia;

namespace Eleu;

// Tabelle (Inserisci > Tabelle). Le tabelle sono quelle native del campo di testo; per sapere dove si è
// si legge la struttura del testo (StrutturaTesto). Righe e colonne si aggiungono e si tolgono riscrivendo
// la tabella: la si legge come modello, la si cambia e la si rimette al suo posto.
public sealed partial class FinestraPrincipale
{
    private (int Tabella, int Riga, int Colonna)? _ultimaCella;
    private bool _celleInSilenzio;

    /// <summary>
    /// Entrando in una cella diversa si dice dove si è ("Riga 2, colonna 3, Età"): lo screen reader legge il
    /// testo della cella, Eleu aggiunge le coordinate. Entrando in una tabella anche quante righe e colonne ha.
    /// </summary>
    private void AnnunciaCambioCella()
    {
        if (_celleInSilenzio || !_testo.Focused)
            return;
        var testo = _testo.Text;
        var cella = StrutturaTesto.CellaDi(testo, _testo.SelectionStart);
        var chiave = cella is null ? ((int, int, int)?)null : (cella.NumeroTabella, cella.Riga, cella.Colonna);
        if (chiave == _ultimaCella)
            return;
        var precedente = _ultimaCella;
        _ultimaCella = chiave;
        if (!_impostazioni.AnnunciaCelle)
            return;
        if (cella is null)
        {
            _annunciatore.Annuncia("Fuori dalla tabella");
            return;
        }
        var dove = Coordinate(testo, cella);
        if (precedente?.Tabella != cella.NumeroTabella)
            dove = $"Tabella {cella.NumeroTabella + 1}, {Dimensioni(cella.Tabella)}. {dove}";
        _annunciatore.Annuncia(dove);
    }

    /// <summary>"3 righe, 2 colonne, con intestazione".</summary>
    private string Dimensioni(TabellaNelTesto tabella)
    {
        int righe = tabella.Righe.Count, colonne = tabella.Righe.Max(r => r.Count);
        return $"{righe} {(righe == 1 ? "riga" : "righe")}, {colonne} {(colonne == 1 ? "colonna" : "colonne")}{(RigheIntestazione(tabella) > 0 ? ", con intestazione" : "")}";
    }

    /// <summary>"Riga 2, colonna 3, Età": la prima riga fa da intestazione e dà il nome alla colonna.</summary>
    private static string Coordinate(string testo, CellaNelTesto cella)
    {
        var dove = $"Riga {cella.Riga + 1}, colonna {cella.Colonna + 1}";
        var intestazione = cella.Tabella.Righe[0];
        if (cella.Riga > 0 && cella.Colonna < intestazione.Count)
        {
            var nome = StrutturaTesto.Leggibile(testo[intestazione[cella.Colonna].Inizio..intestazione[cella.Colonna].Fine]).Trim();
            if (nome.Length > 0)
                dove += $", {nome}";
        }
        return dove;
    }

    private static string TestoCella(string testo, (int Inizio, int Fine) cella)
    {
        var contenuto = StrutturaTesto.Leggibile(testo[cella.Inizio..cella.Fine]).Trim();
        return contenuto.Length == 0 ? "vuota" : contenuto;
    }

    private CellaNelTesto? CellaQui(bool avvisa = true)
    {
        var cella = StrutturaTesto.CellaDi(_testo.Text, _testo.SelectionStart);
        if (cella is null && avvisa)
            _annunciatore.Annuncia("Il cursore non è in una tabella");
        return cella;
    }

    /// <summary>Va a una cella e dice dove si è e cosa c'è scritto.</summary>
    private void VaiAllaCella(TabellaNelTesto tabella, int numeroTabella, int riga, int colonna, string prefisso = "")
    {
        var testo = _testo.Text;
        var celle = tabella.Righe[riga];
        colonna = Math.Min(colonna, celle.Count - 1);
        _ultimaCella = (numeroTabella, riga, colonna);
        _testo.Select(celle[colonna].Inizio, 0);
        _testo.ScrollToCaret();
        var cella = new CellaNelTesto(tabella, numeroTabella, riga, colonna);
        _annunciatore.Annuncia($"{prefisso}{Coordinate(testo, cella)}: {TestoCella(testo, celle[colonna])}");
    }

    /// <summary>Ctrl+Alt+frecce: la cella vicina nella direzione data.</summary>
    private void CellaVicina(int righe, int colonne)
    {
        if (CellaQui() is not { } cella)
            return;
        int riga = cella.Riga + righe, colonna = cella.Colonna + colonne;
        if (riga < 0 || riga >= cella.Tabella.Righe.Count)
        {
            _annunciatore.Annuncia(riga < 0 ? "Prima riga" : "Ultima riga");
            return;
        }
        if (colonna < 0 || colonna >= cella.Tabella.Righe[riga].Count)
        {
            _annunciatore.Annuncia(colonna < 0 ? "Prima colonna" : "Ultima colonna");
            return;
        }
        VaiAllaCella(cella.Tabella, cella.NumeroTabella, riga, colonna);
    }

    // ---------- Aggiungi, elimina, vai, elenco ----------

    private void AggiungiTabella()
    {
        if (CellaQui(avvisa: false) is not null)
        {
            _annunciatore.Annuncia("Il cursore è già in una tabella");
            return;
        }
        using var finestra = new FinestraTabella();
        if (finestra.ShowDialog(this) != DialogResult.OK)
            return;

        // La tabella va in un paragrafo vuoto tutto suo, dopo quello in cui si è.
        var testo = _testo.Text;
        var paragrafi = StrutturaTesto.Paragrafi(testo);
        var (inizio, fine) = paragrafi[StrutturaTesto.ParagrafoDi(paragrafi, _testo.SelectionStart)];
        int posizione = inizio;
        if (fine > inizio)
        {
            _testo.Select(fine, 0);
            _testo.SelectedText = "\n";
            posizione = fine + 1;
        }
        // Tabella vuota scritta in RTF, come quelle che si leggono dai file.
        var vuota = new ContenutoDocumento();
        for (int r = 0; r < finestra.Righe; r++)
            for (int c = 0; c < finestra.Colonne; c++)
                vuota.Paragrafi.Add(new Paragrafo { Cella = new PosizioneCella(0, r, c) });
        _celleInSilenzio = true;
        try
        {
            _testo.Select(posizione, 0);
            _testo.SelectedRtf = RtfScrittore.Scrivi(vuota, _impostazioni.CarattereBase, _impostazioni.DimensioneBase);
        }
        finally
        {
            _celleInSilenzio = false;
        }
        _testo.Modified = true;
        var nuova = StrutturaTesto.CellaDi(_testo.Text, posizione + 2);
        if (nuova is null)
            return;
        if (finestra.Intestazione)
            ImpostaIntestazione(nuova.Tabella.Inizio, 1);
        _ultimaCella = (nuova.NumeroTabella, 0, 0);
        _testo.Select(nuova.Tabella.Righe[0][0].Inizio, 0);
        AnnunciaTraPoco($"Tabella di {Dimensioni(nuova.Tabella)} inserita. Riga 1, colonna 1");
    }

    private void EliminaTabella()
    {
        if (CellaQui() is not { } cella)
            return;
        ImpostaIntestazione(cella.Tabella.Inizio, 0);
        _celleInSilenzio = true;
        try
        {
            _testo.Select(cella.Tabella.Inizio, cella.Tabella.Fine - cella.Tabella.Inizio);
            _testo.SelectedText = "";
        }
        finally
        {
            _celleInSilenzio = false;
        }
        _ultimaCella = null;
        _testo.Modified = true;
        _annunciatore.Annuncia($"Tabella eliminata. {DescriviRiga(_testo.Text, _testo.SelectionStart)}");
    }

    private void TabellaVicina(bool avanti)
    {
        var tabelle = StrutturaTesto.Tabelle(_testo.Text);
        int qui = _testo.SelectionStart;
        int i = avanti ? tabelle.FindIndex(t => t.Inizio > qui) : tabelle.FindLastIndex(t => t.Fine <= qui);
        if (i < 0)
            _annunciatore.Annuncia(tabelle.Count == 0 ? "Nessuna tabella nel documento" : avanti ? "Nessuna tabella dopo" : "Nessuna tabella prima");
        else
            VaiAllaTabella(tabelle[i], i);
    }

    private void VaiAllaTabella(TabellaNelTesto tabella, int numero)
    {
        _ultimaCella = (numero, 0, 0);
        _testo.Select(tabella.Righe[0][0].Inizio, 0);
        _testo.ScrollToCaret();
        _annunciatore.Annuncia($"Tabella {numero + 1}, {Dimensioni(tabella)}. Riga 1, colonna 1: {TestoCella(_testo.Text, tabella.Righe[0][0])}");
    }

    private void MostraElencoTabelle()
    {
        var testo = _testo.Text;
        var tabelle = StrutturaTesto.Tabelle(testo);
        var voci = tabelle.Select((t, i) => new VoceElenco(
            [$"Tabella {i + 1}", Dimensioni(t), TestoCella(testo, t.Righe[0][0])], i)).ToList();
        using var finestra = new FinestraElenco("Tabelle", ["Tabella", "Dimensioni", "Prima cella"], voci);
        finestra.ShowDialog(this);
        if (finestra.DaEliminare.Count > 0)
        {
            _celleInSilenzio = true;
            try
            {
                foreach (var i in finestra.DaEliminare.OrderDescending())
                {
                    _testo.Select(tabelle[i].Inizio, tabelle[i].Fine - tabelle[i].Inizio);
                    _testo.SelectedText = "";
                }
            }
            finally
            {
                _celleInSilenzio = false;
            }
            _testo.Modified = true;
            AnnunciaTraPoco(finestra.DaEliminare.Count == 1 ? "Tabella eliminata" : $"{finestra.DaEliminare.Count} tabelle eliminate");
        }
        else if (finestra.Destinazione is int indice)
        {
            BeginInvoke(() => VaiAllaTabella(tabelle[indice], indice));
        }
    }

    // ---------- Righe e colonne ----------

    private void InserisciRiga(bool sotto) => CambiaTabella((griglia, riga, colonna) =>
    {
        int nuova = sotto ? riga + 1 : riga;
        griglia.InserisciRiga(nuova);
        return (nuova, colonna, sotto ? "Riga inserita sotto" : "Riga inserita sopra");
    });

    private void InserisciColonna(bool aDestra) => CambiaTabella((griglia, riga, colonna) =>
    {
        int nuova = aDestra ? colonna + 1 : colonna;
        griglia.InserisciColonna(nuova);
        return (riga, nuova, aDestra ? "Colonna inserita a destra" : "Colonna inserita a sinistra");
    });

    private void EliminaRiga() => CambiaTabella((griglia, riga, colonna) =>
    {
        if (griglia.Righe.Count == 1)
            return null; // l'ultima riga: si elimina la tabella
        griglia.Righe.RemoveAt(riga);
        return (Math.Min(riga, griglia.Righe.Count - 1), colonna, "Riga eliminata");
    }, EliminaTabella);

    private void EliminaColonna() => CambiaTabella((griglia, riga, colonna) =>
    {
        if (griglia.Colonne == 1)
            return null;
        griglia.EliminaColonna(colonna);
        return (riga, Math.Max(0, colonna - 1), "Colonna eliminata");
    }, EliminaTabella);

    /// <summary>Ordina le righe secondo la colonna del cursore; la prima riga (intestazione) resta in cima.</summary>
    private void OrdinaRighe(bool crescente) => CambiaTabella((griglia, _, colonna) =>
    {
        griglia.Ordina(colonna, crescente, intestazione: true);
        return (griglia.Righe.Count > 1 ? 1 : 0, colonna,
            crescente ? "Righe ordinate dalla A alla Z, dal più piccolo al più grande" : "Righe ordinate dalla Z alla A, dal più grande al più piccolo");
    });

    private void LarghezzaColonna()
    {
        if (CellaQui() is not { } cella)
            return;
        _testo.Select(cella.Tabella.Inizio, cella.Tabella.Fine - cella.Tabella.Inizio);
        var griglia = Griglia.Da(RtfLettore.Leggi(_testo.SelectedRtf ?? "").Paragrafi);
        _testo.Select(cella.Testo.Inizio, 0);
        float attuale = cella.Colonna < griglia.Larghezze.Count && griglia.Larghezze[cella.Colonna] > 0
            ? griglia.Larghezze[cella.Colonna] : 2268 / 20f;
        using var finestra = new FinestraLarghezza(cella.Colonna + 1, attuale * 2.54f / 72f);
        if (finestra.ShowDialog(this) != DialogResult.OK)
            return;
        float punti = finestra.Centimetri * 72f / 2.54f;
        CambiaTabella((g, riga, colonna) =>
        {
            while (g.Larghezze.Count <= colonna)
                g.Larghezze.Add(0);
            g.Larghezze[colonna] = punti;
            return (riga, colonna, $"Colonna {colonna + 1} larga {finestra.Centimetri:0.##} centimetri");
        });
    }

    /// <summary>
    /// Legge la tabella del cursore come <see cref="Griglia"/>, la fa cambiare e la rimette al suo posto, poi
    /// porta il cursore nella cella indicata. Se il cambiamento restituisce null si chiama
    /// <paramref name="altrimenti"/> (per esempio: eliminare l'ultima riga elimina la tabella).
    /// </summary>
    private void CambiaTabella(Func<Griglia, int, int, (int Riga, int Colonna, string Annuncio)?> cambia, Action? altrimenti = null)
    {
        if (CellaQui() is not { } cella)
            return;
        var tabella = cella.Tabella;
        int intestazione = RigheIntestazione(tabella); // la tabella si riscrive: le righe di intestazione si rimettono dopo
        _celleInSilenzio = true;
        try
        {
            _testo.Select(tabella.Inizio, tabella.Fine - tabella.Inizio);
            var griglia = Griglia.Da(RtfLettore.Leggi(_testo.SelectedRtf ?? "").Paragrafi);
            if (griglia.Righe.Count == 0)
                return;

            var esito = cambia(griglia, Math.Min(cella.Riga, griglia.Righe.Count - 1), cella.Colonna);
            if (esito is not var (rigaDopo, colonnaDopo, annuncio))
            {
                _testo.Select(cella.Testo.Inizio, 0);
                altrimenti?.Invoke();
                return;
            }

            var nuovo = new ContenutoDocumento();
            nuovo.Paragrafi.AddRange(griglia.Paragrafi());
            // Prima si toglie la tabella vecchia, poi si inserisce la nuova nello stesso punto: incollare l'RTF
            // sopra la tabella selezionata lascia il campo di testo in uno stato che manda in errore
            // l'accessibilità (UI Automation) di Windows.
            _testo.SelectedText = "";
            _testo.Select(tabella.Inizio, 0);
            _testo.SelectedRtf = RtfScrittore.Scrivi(nuovo, _impostazioni.CarattereBase, _impostazioni.DimensioneBase);
            _testo.Modified = true;

            var rifatta = StrutturaTesto.CellaDi(_testo.Text, tabella.Inizio + 2);
            if (rifatta is null)
                return;
            ImpostaIntestazione(rifatta.Tabella.Inizio, Math.Min(intestazione, rifatta.Tabella.Righe.Count));
            rigaDopo = Math.Clamp(rigaDopo, 0, rifatta.Tabella.Righe.Count - 1);
            colonnaDopo = Math.Clamp(colonnaDopo, 0, rifatta.Tabella.Righe[rigaDopo].Count - 1);
            _celleInSilenzio = false;
            VaiAllaCella(rifatta.Tabella, rifatta.NumeroTabella, rigaDopo, colonnaDopo, $"{annuncio}. ");
        }
        finally
        {
            _celleInSilenzio = false;
        }
    }

    /// <summary>
    /// Se negli appunti c'è una tabella (copiata da Word, Excel o un altro programma che dà l'RTF), la incolla
    /// come tabella, ripulita come quelle di Eleu, in un paragrafo tutto suo. Restituisce false se non c'è.
    /// </summary>
    private bool IncollaTabella()
    {
        if (!Clipboard.ContainsText(TextDataFormat.Rtf) || CellaQui(avvisa: false) is not null)
            return false;
        var modello = RtfLettore.Leggi(Clipboard.GetText(TextDataFormat.Rtf));
        if (!modello.Paragrafi.Any(p => p.Cella is not null))
            return false;

        if (_testo.SelectionLength > 0)
            _testo.SelectedText = "";
        var testo = _testo.Text;
        var paragrafi = StrutturaTesto.Paragrafi(testo);
        var (inizio, fine) = paragrafi[StrutturaTesto.ParagrafoDi(paragrafi, _testo.SelectionStart)];
        int posizione = inizio;
        if (fine > inizio)
        {
            _testo.Select(fine, 0);
            _testo.SelectedText = "\n";
            posizione = fine + 1;
        }
        _celleInSilenzio = true;
        try
        {
            _testo.Select(posizione, 0);
            _testo.SelectedRtf = RtfScrittore.Scrivi(modello, _impostazioni.CarattereBase, _impostazioni.DimensioneBase);
        }
        finally
        {
            _celleInSilenzio = false;
        }
        _testo.Modified = true;
        if (StrutturaTesto.CellaDi(_testo.Text, posizione + 2) is { } incollata)
            ImpostaIntestazione(incollata.Tabella.Inizio, IntestazioniDelModello(modello).GetValueOrDefault(0));
        if (StrutturaTesto.CellaDi(_testo.Text, posizione + 2) is { } cella)
            VaiAllaCella(cella.Tabella, cella.NumeroTabella, 0, 0, $"Tabella incollata, {Dimensioni(cella.Tabella)}. ");
        return true;
    }
}

/// <summary>Inserisci > Tabelle > Larghezza colonna: in centimetri.</summary>
public sealed class FinestraLarghezza : Modulo
{
    private readonly NumeroConUnita _centimetri;

    public FinestraLarghezza(int colonna, float centimetri) : base($"Larghezza della colonna {colonna}", 140)
    {
        _centimetri = Campo("&Larghezza:", new NumeroConUnita("cm", (decimal)centimetri, 0.5m, 30, 0.25m, 2));
        AggiungiPulsanti();
        ActiveControl = _centimetri;
    }

    public float Centimetri => (float)_centimetri.Value;
}

/// <summary>Inserisci > Tabelle > Aggiungi tabella: quante righe e quante colonne.</summary>
public sealed class FinestraTabella : Modulo
{
    private readonly NumericUpDown _righe, _colonne;
    private readonly CheckBox _intestazione;

    public FinestraTabella() : base("Aggiungi tabella", 120)
    {
        _colonne = Campo("&Colonne:", Numero(2, 1, 30, 1, 0));
        _righe = Campo("&Righe:", Numero(2, 1, 500, 1, 0));
        _intestazione = Casella(Griglia, "&Prima riga di intestazione (nomi delle colonne, ripetuti su ogni pagina)", true);
        AggiungiPulsanti();
        ActiveControl = _colonne;
    }

    public bool Intestazione => _intestazione.Checked;

    public int Righe => (int)_righe.Value;

    public int Colonne => (int)_colonne.Value;
}
