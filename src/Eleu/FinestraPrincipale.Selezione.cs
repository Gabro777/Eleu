using System.Text;
using Eleu.Documento;

namespace Eleu;

// Selezionare senza tenere premuto Maiusc: F8 segna l'inizio del blocco, spostati con i tasti che vuoi e F8 di
// nuovo seleziona fino a lì. In più seleziona parola, riga e paragrafo, e salva la selezione in un file a parte.
public sealed partial class FinestraPrincipale
{
    private int? _inizioBlocco;

    /// <summary>F8: la prima volta segna l'inizio del blocco, la seconda seleziona dall'inizio segnato al cursore.</summary>
    private void SegnaBlocco()
    {
        int qui = _testo.SelectionStart + _testo.SelectionLength;
        if (_inizioBlocco is not int inizio || inizio > _testo.TextLength)
        {
            _inizioBlocco = _testo.SelectionStart;
            _annunciatore.Annuncia("Inizio del blocco segnato. Spostati e premi di nuovo F8 per selezionare");
            return;
        }
        _inizioBlocco = null;
        int da = Math.Min(inizio, qui), a = Math.Max(inizio, qui);
        _testo.Select(da, a - da);
        AnnunciaSelezione();
    }

    /// <summary>Maiusc+F8: dimentica l'inizio del blocco.</summary>
    private void AnnullaBlocco()
    {
        bool cera = _inizioBlocco is not null;
        _inizioBlocco = null;
        _annunciatore.Annuncia(cera ? "Blocco annullato" : "Nessun blocco iniziato");
    }

    private void SelezionaParola()
    {
        var testo = _testo.Text;
        var (inizio, fine) = ParolaIntorno(testo, Math.Min(_testo.SelectionStart, testo.Length));
        if (fine == inizio)
        {
            _annunciatore.Annuncia("Il cursore non è su una parola");
            return;
        }
        _testo.Select(inizio, fine - inizio);
        AnnunciaSelezione();
    }

    private void SelezionaRiga()
    {
        int riga = _testo.GetLineFromCharIndex(_testo.SelectionStart);
        int inizio = _testo.GetFirstCharIndexFromLine(riga);
        int dopo = _testo.GetFirstCharIndexFromLine(riga + 1);
        _testo.Select(inizio, (dopo < 0 ? _testo.TextLength : dopo) - inizio);
        AnnunciaSelezione();
    }

    private void SelezionaParagrafo()
    {
        var testo = _testo.Text;
        var paragrafi = StrutturaTesto.Paragrafi(testo);
        var (inizio, fine) = paragrafi[StrutturaTesto.ParagrafoDi(paragrafi, Math.Min(_testo.SelectionStart, testo.Length))];
        _testo.Select(inizio, Math.Min(fine + 1, testo.Length) - inizio);
        AnnunciaSelezione();
    }

    /// <summary>Dice quanto è stato selezionato e come comincia, per sapere se il blocco è quello giusto.</summary>
    private void AnnunciaSelezione()
    {
        var selezione = StrutturaTesto.Leggibile(_testo.SelectedText).Trim();
        if (selezione.Length == 0)
        {
            _annunciatore.Annuncia("Niente selezionato");
            return;
        }
        int parole = selezione.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        var inizio = selezione.Length > 60 ? selezione[..60] + "..." : selezione;
        _annunciatore.Annuncia($"Selezionat{(parole == 1 ? "a 1 parola" : $"e {parole} parole")}: {inizio}");
    }

    /// <summary>File > Salva selezione: la parte selezionata in un file nuovo, con la sua formattazione.</summary>
    private void SalvaSelezione()
    {
        if (_testo.SelectionLength == 0)
        {
            _annunciatore.Annuncia("Nessun testo selezionato");
            return;
        }
        using var dialogo = new SaveFileDialog
        {
            Title = "Salva selezione",
            Filter = FormatiFile.FiltroSalva,
            FilterIndex = (int)_formato + 1,
            FileName = "Selezione",
            InitialDirectory = CartellaIniziale(),
            AddExtension = true,
            OverwritePrompt = true,
        };
        if (dialogo.ShowDialog(this) != DialogResult.OK)
            return;

        var rtf = _testo.SelectedRtf ?? "";
        var modello = RtfLettore.Leggi(rtf);
        modello.Pagina = _pagina;
        var formato = FormatiFile.DaPercorso(dialogo.FileName);
        try
        {
            using var flusso = new MemoryStream();
            switch (formato)
            {
                case FormatoFile.Testo:
                    flusso.Write(FileTesto.Scrivi(string.Join(Environment.NewLine,
                        Leggibile.Da(modello).Paragrafi.Select(p => p.Testo.Replace(Editor.Paginatore.SaltoPagina.ToString(), ""))), _codificaTesto));
                    break;
                case FormatoFile.Rtf:
                    flusso.Write(Encoding.Latin1.GetBytes(rtf));
                    break;
                case FormatoFile.Html:
                    flusso.Write(new UTF8Encoding(false).GetBytes(HtmlFormato.Scrivi(modello)));
                    break;
                case FormatoFile.Odt:
                    OdtFormato.Scrivi(modello, flusso);
                    break;
                case FormatoFile.Epub:
                    modello.Metadati = modello.Metadati with { Titolo = Path.GetFileNameWithoutExtension(dialogo.FileName) };
                    EpubFormato.Scrivi(modello, flusso);
                    break;
                default:
                    DocxFormato.Scrivi(modello, flusso);
                    break;
            }
            File.WriteAllBytes(dialogo.FileName, flusso.ToArray());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"Impossibile salvare {Path.GetFileName(dialogo.FileName)}.\n\n{e.Message}", NomeApp,
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        SuonoConferma(Accessibilita.Segnale.Salvato);
        AnnunciaTraPoco($"Selezione salvata in {Path.GetFileName(dialogo.FileName)}");
    }
}
