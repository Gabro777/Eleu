using Eleu.Documento;
using Eleu.Editor;
using Eleu.Interfaccia;

namespace Eleu;

// Elementi del documento con lo stesso schema per ogni tipo: Aggiungi, Elimina, Precedente, Successivo,
// Elenco. Più Vai a (F2 / Maiusc+F2 per ripetere).
public sealed partial class FinestraPrincipale
{
    private int _tipoVaiA; // 0 pagina, 1 riga, 2 paragrafo

    /// <summary>I paragrafi del documento con la posizione in cui iniziano nel testo.</summary>
    private List<(Paragrafo Paragrafo, int Inizio)> ParagrafiConPosizione()
    {
        var paragrafi = RtfLettore.Leggi(_testo.Rtf ?? "").Paragrafi;
        var testo = _testo.Text;
        var inizi = StrutturaTesto.Paragrafi(testo).Select(p => p.Inizio).ToList();
        return paragrafi.Select((p, i) => (p, i < inizi.Count ? inizi[i] : testo.Length)).ToList();
    }

    private int InizioParagrafoCorrente()
    {
        var testo = _testo.Text;
        var paragrafi = StrutturaTesto.Paragrafi(testo);
        return paragrafi[StrutturaTesto.ParagrafoDi(paragrafi, Math.Min(_testo.SelectionStart, testo.Length))].Inizio;
    }

    /// <summary>Va alla posizione e annuncia la riga, con un prefisso (es. "Titolo 2").</summary>
    private void VaiEAnnuncia(int posizione, string prefisso)
    {
        posizione = Paginatore.PrimoDopoIlSalto(_testo.Text, Math.Clamp(posizione, 0, _testo.TextLength));
        _testo.Select(posizione, 0);
        _testo.ScrollToCaret();
        _annunciatore.Annuncia($"{prefisso}. {DescriviRiga(_testo.Text, posizione)}");
    }

    /// <summary>Il prossimo (o precedente) elemento tra quelli dati, rispetto al paragrafo del cursore.</summary>
    private void VaiAlVicino(IReadOnlyList<int> posizioni, bool avanti, string nome, Func<int, string> prefisso)
    {
        int qui = InizioParagrafoCorrente();
        var trovato = avanti ? posizioni.Where(p => p > qui).Cast<int?>().FirstOrDefault()
                             : posizioni.Where(p => p < qui).Cast<int?>().LastOrDefault();
        if (trovato is int posizione)
            VaiEAnnuncia(posizione, prefisso(posizione));
        else
            _annunciatore.Annuncia(posizioni.Count == 0 ? $"Nessun {nome} nel documento" : avanti ? $"Nessun {nome} dopo" : $"Nessun {nome} prima");
    }

    // ---------- Interruzioni di pagina ----------

    private List<int> PosizioniInterruzioni()
    {
        var testo = _testo.Text;
        var posizioni = new List<int>();
        for (int i = testo.IndexOf(Paginatore.SaltoPagina); i >= 0; i = testo.IndexOf(Paginatore.SaltoPagina, i + 1))
            posizioni.Add(i);
        return posizioni;
    }

    private void InterruzioneVicina(bool avanti) =>
        VaiAlVicino(PosizioniInterruzioni(), avanti, "interruzione di pagina", _ => "Interruzione di pagina");

    /// <summary>Elimina le interruzioni nella selezione, o quella del paragrafo del cursore.</summary>
    private void EliminaInterruzioni()
    {
        var posizioni = _testo.SelectionLength > 0
            ? PosizioniInterruzioni().Where(p => p >= _testo.SelectionStart && p < _testo.SelectionStart + _testo.SelectionLength).ToList()
            : PosizioniInterruzioni().Where(p => p == InizioParagrafoCorrente() || p == _testo.SelectionStart).Take(1).ToList();
        if (posizioni.Count == 0)
        {
            _annunciatore.Annuncia("Nessuna interruzione di pagina qui");
            return;
        }
        Elimina(posizioni);
        _annunciatore.Annuncia(posizioni.Count == 1 ? "Interruzione di pagina eliminata" : $"{posizioni.Count} interruzioni di pagina eliminate");
    }

    private void Elimina(IEnumerable<int> posizioni)
    {
        int cursore = _testo.SelectionStart;
        foreach (int posizione in posizioni.OrderDescending())
        {
            _testo.Select(posizione, 1);
            _testo.SelectedText = "";
            if (posizione < cursore)
                cursore--;
        }
        _testo.Select(Math.Min(cursore, _testo.TextLength), 0);
        _testo.Modified = true;
    }

    private void MostraElencoInterruzioni()
    {
        var posizioni = PosizioniInterruzioni();
        var inizi = Paginatore.InizioPagine(_testo, _pagina);
        var testo = _testo.Text;
        var voci = posizioni.Select(p => new VoceElenco(
            [Riassunto(testo, p + 1), $"Pagina {Paginatore.PaginaDi(inizi, p)}"], p)).ToList();
        using var finestra = new FinestraElenco("Interruzioni di pagina", ["Descrizione", "Posizione"], voci);
        finestra.ShowDialog(this);
        if (finestra.DaEliminare.Count > 0)
        {
            Elimina(finestra.DaEliminare);
            AnnunciaTraPoco(finestra.DaEliminare.Count == 1 ? "Interruzione di pagina eliminata" : $"{finestra.DaEliminare.Count} interruzioni di pagina eliminate");
        }
        else if (finestra.Destinazione is int destinazione)
        {
            BeginInvoke(() => VaiEAnnuncia(destinazione, "Interruzione di pagina"));
        }
    }

    /// <summary>Il testo della riga che inizia alla posizione, abbreviato: la "descrizione" di un elemento.</summary>
    private static string Riassunto(string testo, int inizio)
    {
        inizio = Math.Clamp(inizio, 0, testo.Length);
        int fine = testo.IndexOfAny(['\n', StrutturaTesto.FineCella], inizio);
        var riga = StrutturaTesto.Leggibile(testo[inizio..(fine < 0 ? testo.Length : fine)])
            .Replace(Paginatore.SaltoPagina.ToString(), "").Trim();
        return riga.Length > 70 ? riga[..70] + "..." : riga.Length == 0 ? "(riga vuota)" : riga;
    }

    // ---------- Titoli ----------

    private void AggiungiTitolo()
    {
        int attuale = RichEditNativo.LivelloTitolo(_testo);
        using var finestra = new FinestraLivello(attuale == 0 ? 1 : attuale);
        if (finestra.ShowDialog(this) == DialogResult.OK)
            ImpostaTitolo(finestra.Livello);
    }

    private List<(Paragrafo Paragrafo, int Inizio)> Titoli() =>
        ParagrafiConPosizione().Where(x => x.Paragrafo.LivelloTitolo > 0).ToList();

    private void TitoloVicino(bool avanti)
    {
        var titoli = Titoli();
        VaiAlVicino(titoli.Select(t => t.Inizio).ToList(), avanti, "titolo",
            p => $"Titolo {titoli.First(t => t.Inizio == p).Paragrafo.LivelloTitolo}");
    }

    private void MostraElencoTitoli()
    {
        var titoli = Titoli();
        var voci = titoli.Select(t => new VoceElenco(
            [$"{new string(' ', (t.Paragrafo.LivelloTitolo - 1) * 2)}{t.Paragrafo.Testo.Trim()}", $"Titolo {t.Paragrafo.LivelloTitolo}"], t.Inizio)).ToList();
        using var finestra = new FinestraElenco("Titoli", ["Titolo", "Livello"], voci);
        finestra.ShowDialog(this);
        foreach (int posizione in finestra.DaEliminare)
        {
            _testo.Select(posizione, 0);
            ImpostaTitolo(0);
        }
        if (finestra.DaEliminare.Count > 0)
            AnnunciaTraPoco(finestra.DaEliminare.Count == 1 ? "Titolo eliminato" : $"{finestra.DaEliminare.Count} titoli eliminati");
        else if (finestra.Destinazione is int destinazione)
            BeginInvoke(() => VaiEAnnuncia(destinazione, $"Titolo {titoli.First(t => t.Inizio == destinazione).Paragrafo.LivelloTitolo}"));
    }


    /// <summary>
    /// Inserisce all'inizio del paragrafo il sommario: un paragrafo per titolo, "Titolo, pagina N",
    /// rientrato secondo il livello. È testo normale: se il documento cambia, si inserisce di nuovo.
    /// </summary>
    private void InserisciSommario()
    {
        var titoli = Titoli();
        if (titoli.Count == 0)
        {
            _annunciatore.Annuncia("Nessun titolo nel documento: il sommario si crea dai titoli");
            return;
        }
        int inizio = InizioParagrafoCorrente();
        string Righe(Func<int, int> pagina) =>
            string.Concat(titoli.Select((t, i) => $"{t.Paragrafo.Testo.Replace(Paginatore.SaltoPagina.ToString(), "").Trim()}, pagina {pagina(i)}\n"));

        // Prima si inserisce, poi si contano le pagine: il sommario stesso può spostare i titoli più avanti.
        var provvisorio = Righe(_ => 0);
        _testo.Select(inizio, 0);
        _testo.SelectedText = provvisorio;
        var inizi = Paginatore.InizioPagine(_testo, _pagina);
        var pagine = titoli.Select(t => Paginatore.PaginaDi(inizi, t.Inizio >= inizio ? t.Inizio + provvisorio.Length : t.Inizio)).ToList();
        var sommario = Righe(i => pagine[i]);
        _testo.Select(inizio, provvisorio.Length);
        _testo.SelectedText = sommario;

        // Righe di testo normale, rientrate di mezzo centimetro abbondante per ogni livello sotto il primo.
        int posizione = inizio;
        foreach (var (riga, titolo) in sommario.TrimEnd('\n').Split('\n').Zip(titoli))
        {
            _testo.Select(posizione, 0);
            RichEditNativo.ImpostaLivelloTitolo(_testo, 0);
            RichEditNativo.ImpostaFormatoParagrafo(_testo, new FormatoParagrafo(
                RientroSinistro: (titolo.Paragrafo.LivelloTitolo - 1) * 18f, RientroPrimaRiga: 0, Elenco: TipoElenco.Nessuno));
            posizione += riga.Length + 1;
        }
        _testo.Select(inizio, 0);
        _annunciatore.Annuncia($"Sommario inserito, {(titoli.Count == 1 ? "1 titolo" : $"{titoli.Count} titoli")}. {DescriviRiga(_testo.Text, inizio)}");
    }
    // ---------- Elenchi ----------

    private void PuntoElencoVicino(bool avanti)
    {
        var punti = ParagrafiConPosizione().Where(x => x.Paragrafo.Elenco || x.Paragrafo.Numerato).Select(x => x.Inizio).ToList();
        VaiAlVicino(punti, avanti, "punto elenco", _ => "Punto elenco");
    }

    // ---------- Vai a ----------

    private void ApriVaiA()
    {
        int numero = _tipoVaiA switch
        {
            0 => PaginaCorrente().Pagina,
            1 => _testo.GetLineFromCharIndex(_testo.SelectionStart) + 1,
            _ => PosizioneCorrente().Riga,
        };
        using var finestra = new FinestraVaiA(_tipoVaiA, numero);
        if (finestra.ShowDialog(this) != DialogResult.OK)
            return;
        _tipoVaiA = finestra.Destinazione;
        int valore = finestra.Valore;
        BeginInvoke(() => VaiANumero(_tipoVaiA, valore));
    }

    private void VaiANumero(int tipo, int numero)
    {
        switch (tipo)
        {
            case 0:
                var (_, _, inizi) = PaginaCorrente();
                VaiAPagina(numero, inizi);
                break;
            case 1:
                int inizioRiga = _testo.GetFirstCharIndexFromLine(numero - 1);
                if (inizioRiga < 0)
                    _annunciatore.Annuncia($"Il documento ha {_testo.GetLineFromCharIndex(_testo.TextLength) + 1} righe");
                else
                    VaiEAnnuncia(inizioRiga, $"Riga {numero}");
                break;
            default:
                var paragrafi = ParagrafiConPosizione();
                if (numero > paragrafi.Count)
                    _annunciatore.Annuncia($"Il documento ha {paragrafi.Count} paragrafi");
                else
                    VaiEAnnuncia(paragrafi[numero - 1].Inizio, $"Paragrafo {numero}");
                break;
        }
    }

    /// <summary>F2 / Maiusc+F2: ripete l'ultimo Vai a sull'elemento successivo o precedente.</summary>
    private void VaiASuccessivo(bool avanti)
    {
        switch (_tipoVaiA)
        {
            case 0:
                PaginaSuccessiva(avanti ? +1 : -1);
                break;
            case 1:
                int riga = _testo.GetLineFromCharIndex(_testo.SelectionStart) + (avanti ? 2 : 0);
                if (riga < 1 || _testo.GetFirstCharIndexFromLine(riga - 1) < 0)
                    _annunciatore.Annuncia(avanti ? "Ultima riga" : "Prima riga");
                else
                    VaiANumero(1, riga);
                break;
            default:
                var paragrafi = ParagrafiConPosizione().Select(p => p.Inizio).ToList();
                VaiAlVicino(paragrafi, avanti, "paragrafo", p => $"Paragrafo {paragrafi.IndexOf(p) + 1}");
                break;
        }
    }
}

/// <summary>Inserisci > Titoli > Aggiungi titolo: sceglie il livello del titolo.</summary>
public sealed class FinestraLivello : Modulo
{
    private readonly ComboBox _livello;

    public FinestraLivello(int livello) : base("Aggiungi titolo", 160)
    {
        _livello = Campo("&Livello:", Scelta(Enumerable.Range(1, 9).Select(n => $"Titolo {n}"), livello - 1));
        AggiungiPulsanti();
        ActiveControl = _livello;
    }

    public int Livello => _livello.SelectedIndex + 1;
}
