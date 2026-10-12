using Eleu.Braille;
using Eleu.Documento;
using Eleu.Editor;
using Eleu.Interfaccia;

namespace Eleu;

// Braille: riga corrente in Braille, stampa o file BRF, e anteprima.
public sealed partial class FinestraPrincipale
{
    private const float DimensioneRigaBraille = 20f;
    private static readonly char[] AcapoEFineRiga = ['\n', '\r'];

    private readonly Label _rigaBraille;
    private string? _ultimaRigaTradotta;

    private FormatoPagina FormatoBraille => new(_impostazioni.CelleBraille, _impostazioni.RigheBraille)
    {
        Numero = _impostazioni.NumeroBraille,
        MargineSinistro = _impostazioni.MargineBraille,
        PagineStampa = _impostazioni.PagineStampaBraille,
        Sommario = _impostazioni.SommarioBraille,
        InterlineaDoppia = _impostazioni.InterlineaDoppiaBraille,
        FronteRetro = _impostazioni.FronteRetroBraille,
        PiegaAMeta = _impostazioni.PiegaBraille,
    };

    /// <summary>
    /// Striscia in basso con la riga dello schermo dove si trova il cursore, in celle Braille.
    /// È un'etichetta: non prende il focus e non viene annunciata a ogni cambiamento,
    /// così chi usa lo screen reader non sente ripetere la riga a ogni tasto.
    /// </summary>
    private Label CreaRigaBraille() => new()
    {
        Dock = DockStyle.Bottom,
        AutoSize = false,
        AutoEllipsis = true,
        UseMnemonic = false,
        TextAlign = ContentAlignment.MiddleLeft,
        Padding = new Padding(8, 0, 8, 0),
        Height = 48,
        Font = new Font("Segoe UI Symbol", DimensioneRigaBraille),
        AccessibleName = "Riga in Braille",
        Visible = _impostazioni.RigaBraille,
    };

    private void CambiaRigaBraille()
    {
        _impostazioni.RigaBraille = !_impostazioni.RigaBraille;
        _rigaBraille.Visible = _impostazioni.RigaBraille;
        _ultimaRigaTradotta = null;
        BeginInvoke(AggiornaRigaBraille); // a disposizione dei controlli conclusa
        _annunciatore.Annuncia(_impostazioni.RigaBraille ? "Riga in Braille attivata" : "Riga in Braille disattivata");
    }

    private void AggiornaRigaBraille()
    {
        if (_rigaBraille is not { Visible: true })
            return;

        // La riga come appare sullo schermo (va a capo con la larghezza della finestra).
        int riga = _testo.GetLineFromCharIndex(_testo.SelectionStart);
        int inizio = _testo.GetFirstCharIndexFromLine(riga);
        int successiva = _testo.GetFirstCharIndexFromLine(riga + 1);
        int fine = successiva < 0 ? _testo.TextLength : successiva;
        var testo = inizio < 0 ? "" : StrutturaTesto.Leggibile(_testo.Text[inizio..Math.Max(inizio, fine)]).TrimEnd(AcapoEFineRiga);
        int cursoreNellaRiga = Math.Clamp(_testo.SelectionStart - Math.Max(inizio, 0), 0, testo.Length);

        // Si ricalcola solo se cambiano la riga, il cursore o lo spazio disponibile.
        var chiave = $"{cursoreNellaRiga}|{_rigaBraille.ClientSize.Width}|{testo}";
        if (chiave == _ultimaRigaTradotta)
            return;
        _ultimaRigaTradotta = chiave;
        try
        {
            var celle = TraduttoreBraille.Traduci(testo);
            int celleVisibili = CelleVisibili();
            if (celle.Length > celleVisibili)
            {
                // Una cella Braille è più larga di una lettera: se la riga non ci sta, si mostra il pezzo
                // intorno al cursore, come fa un display Braille.
                int celleAlCursore = TraduttoreBraille.Traduci(testo[..cursoreNellaRiga]).Length;
                int partenza = Math.Clamp(celleAlCursore - celleVisibili / 3, 0, celle.Length - celleVisibili);
                celle = celle.Substring(partenza, celleVisibili);
            }
            _rigaBraille.Text = celle;
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or InvalidOperationException)
        {
            _rigaBraille.Text = "Braille non disponibile";
        }
    }

    private int CelleVisibili()
    {
        var campione = new string('⠿', 20);
        int larghezzaCella = Math.Max(1, TextRenderer.MeasureText(campione, _rigaBraille.Font, Size.Empty, TextFormatFlags.NoPadding).Width / 20);
        return Math.Max(1, (_rigaBraille.ClientSize.Width - _rigaBraille.Padding.Horizontal) / larghezzaCella - 1);
    }

    /// <summary>Le celle seguono lo zoom, per chi vede poco.</summary>
    private void AdattaRigaBrailleAlloZoom()
    {
        var vecchio = _rigaBraille.Font;
        _rigaBraille.Font = new Font(vecchio.FontFamily, DimensioneRigaBraille * _impostazioni.Zoom);
        _rigaBraille.Height = (int)(48 * _impostazioni.Zoom);
        vecchio.Dispose();
        _ultimaRigaTradotta = null;
        if (IsHandleCreated)
            BeginInvoke(AggiornaRigaBraille);
    }

    /// <summary>File > Salva come Braille, BRF (suFile) oppure File > Stampa braille.</summary>
    private void Braille(bool suFile)
    {
        if (TraduciInBraille() is not List<ParagrafoBraille> paragrafi)
            return;

        IReadOnlyList<string> stampanti = suFile ? [] : StampanteBraille.Installate();
        if (!suFile && stampanti.Count == 0)
        {
            MessageBox.Show(this, "Non ci sono stampanti installate.", NomeApp, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        var titolo = TitoloInBraille();
        using var finestra = new FinestraBraille(stampanti, _impostazioni.StampanteBraille, FormatoBraille with { Titolo = _impostazioni.TitoloBraille ? titolo : null },
            formato => Impagina(paragrafi, formato).Count,
            suFile ? "Salva come Braille (BRF)" : "Stampa braille", conFile: suFile, titoloInCelle: titolo);
        if (finestra.ShowDialog(this) != DialogResult.OK)
            return;

        var scelto = finestra.Formato;
        if (!suFile)
            _impostazioni.StampanteBraille = finestra.Stampante;
        _impostazioni.CelleBraille = scelto.CellePerRiga;
        _impostazioni.RigheBraille = scelto.RighePerPagina;
        _impostazioni.NumeroBraille = scelto.Numero;
        _impostazioni.TitoloBraille = scelto.Titolo is not null;
        _impostazioni.MargineBraille = scelto.MargineSinistro;
        _impostazioni.PagineStampaBraille = scelto.PagineStampa;
        _impostazioni.SommarioBraille = scelto.Sommario;
        _impostazioni.InterlineaDoppiaBraille = scelto.InterlineaDoppia;
        _impostazioni.FronteRetroBraille = scelto.FronteRetro;
        _impostazioni.PiegaBraille = scelto.PiegaAMeta;
        _impostazioni.Salva();

        var tutte = Impagina(paragrafi, scelto);
        var (da, a) = finestra.Intervallo;
        var pagine = tutte.Skip(da - 1).Take(a - da + 1).ToList();
        var dati = FormatoBrf.Byte(pagine);
        var quantePagine = pagine.Count == 1 ? "1 pagina" : $"{pagine.Count} pagine";

        if (finestra.Stampante is string stampante)
        {
            try
            {
                StampanteBraille.Stampa(stampante, dati, NomeDocumento);
                AnnunciaTraPoco($"Inviate {quantePagine} Braille a {stampante}");
            }
            catch (IOException e)
            {
                MessageBox.Show(this, $"{e.Message}\n\nControlla che la stampante sia accesa e collegata.", NomeApp,
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            return;
        }

        using var dialogo = new SaveFileDialog
        {
            Title = "Salva il file Braille",
            Filter = "File Braille (*.brf)|*.brf",
            FileName = Path.GetFileNameWithoutExtension(NomeDocumento),
            InitialDirectory = CartellaIniziale(),
            AddExtension = true,
            OverwritePrompt = true,
        };
        if (dialogo.ShowDialog(this) != DialogResult.OK)
            return;
        try
        {
            File.WriteAllBytes(dialogo.FileName, dati);
            AnnunciaTraPoco($"Salvato {Path.GetFileName(dialogo.FileName)}, {quantePagine} Braille");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"Impossibile salvare il file Braille.\n\n{e.Message}", NomeApp,
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>Le pagine Braille, con i numeri e il sommario tradotti in Braille.</summary>
    private static List<List<string>> Impagina(List<ParagrafoBraille> paragrafi, FormatoPagina formato) =>
        Impaginatore.Impagina(paragrafi, formato, TraduttoreBraille.Traduci);

    private string? TitoloInBraille()
    {
        try
        {
            return TraduttoreBraille.Traduci(TitoloDocumento);
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }

    private void AnteprimaBraille()
    {
        if (TraduciInBraille() is not List<ParagrafoBraille> paragrafi)
            return;

        var pagine = Impagina(paragrafi, FormatoBraille with { Titolo = _impostazioni.TitoloBraille ? TitoloInBraille() : null });
        var righe = new List<string>();
        for (int i = 0; i < pagine.Count; i++)
        {
            righe.Add($"--- Pagina {i + 1} di {pagine.Count} ---");
            righe.AddRange(pagine[i]);
        }
        using var finestra = new FinestraTesto("Anteprima Braille", string.Join(Environment.NewLine, righe));
        finestra.ShowDialog(this);
    }

    /// <summary>
    /// Il documento tradotto in Braille, un elemento per paragrafo, con il livello dei titoli (per il sommario) e la
    /// pagina a stampa in cui comincia ogni paragrafo; null se vuoto o se la traduzione fallisce.
    /// </summary>
    private List<ParagrafoBraille>? TraduciInBraille()
    {
        var modello = ModelloCompleto();
        // La pagina a stampa di ogni paragrafo, dalla stessa impaginazione della stampa su carta.
        var inizi = StrutturaTesto.Paragrafi(_testo.Text);
        var pagineStampa = Paginatore.InizioPagine(_testo, _pagina);
        for (int i = 0; i < modello.Paragrafi.Count && i < inizi.Count; i++)
            modello.Paragrafi[i].PaginaStampa = Paginatore.PaginaDi(pagineStampa, inizi[i].Inizio);

        var testi = Leggibile.Da(modello).Paragrafi.Where(p => p.Testo.Trim().Length > 0).ToList();
        if (testi.Count == 0)
        {
            _annunciatore.Annuncia("Il documento è vuoto");
            return null;
        }

        try
        {
            return testi
                .Select(p => new ParagrafoBraille(
                    TraduttoreBraille.Traduci((p.Elenco ? "- " : "") + p.Testo.Replace(Paginatore.SaltoPagina.ToString(), "").Trim()),
                    Titolo: p.LivelloTitolo > 0,
                    NuovaPagina: p.SaltoPagina,
                    Livello: p.LivelloTitolo,
                    PaginaStampa: p.PaginaStampa))
                .ToList();
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or InvalidOperationException)
        {
            MessageBox.Show(this, $"La traduzione in Braille non è disponibile.\n\n{e.Message}", NomeApp,
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return null;
        }
    }
}
