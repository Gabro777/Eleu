using Eleu.Documento;
using Eleu.Editor;
using Eleu.Interfaccia;

namespace Eleu;

// Formattazione, zoom e comandi di lettura ("dove sono").
public sealed partial class FinestraPrincipale
{
    private void CambiaEffetto(RichEditNativo.Effetto effetto, string nome)
    {
        bool attivo = RichEditNativo.StatoEffetto(_testo, effetto) != true;
        RichEditNativo.ImpostaEffetto(_testo, effetto, attivo);
        _annunciatore.Annuncia($"{nome} {(attivo ? "attivato" : "disattivato")}");
    }

    private void CambiaDimensione(int passi)
    {
        RichEditNativo.CambiaDimensione(_testo, passi);
        var dimensione = RichEditNativo.Dimensione(_testo);
        _annunciatore.Annuncia(dimensione is float d ? $"{d:0.#} punti" : passi > 0 ? "Carattere aumentato" : "Carattere ridotto");
    }

    private void ImpostaTitolo(int livello)
    {
        int inizioSelezione = _testo.SelectionStart;
        int lunghezzaSelezione = _testo.SelectionLength;

        // Il titolo vale per paragrafi interi.
        var testo = _testo.Text;
        int inizio = inizioSelezione == 0 ? 0 : testo.LastIndexOf('\n', inizioSelezione - 1) + 1;
        int fine = testo.IndexOf('\n', inizioSelezione + lunghezzaSelezione);
        if (fine < 0)
            fine = testo.Length;

        _testo.Select(inizio, fine - inizio);
        RichEditNativo.ImpostaLivelloTitolo(_testo, livello);
        // Lo stile predefinito di RichEdit ha un suo aspetto: si impone quello di Eleu, uguale a quello del DOCX.
        RichEditNativo.ImpostaDimensione(_testo, livello > 0 ? ContenutoDocumento.DimensioneTitolo(livello) : ContenutoDocumento.DimensioneBase);
        RichEditNativo.ImpostaEffetto(_testo, RichEditNativo.Effetto.Grassetto, livello > 0);
        RichEditNativo.ImpostaEffetto(_testo, RichEditNativo.Effetto.Corsivo, false);
        _testo.Select(inizioSelezione, lunghezzaSelezione);

        _testo.Modified = true;
        _annunciatore.Annuncia(livello > 0 ? $"Titolo {livello}" : "Testo normale");
    }

    private void ImpostaElenco(TipoElenco tipo)
    {
        RichEditNativo.ImpostaFormatoParagrafo(_testo, new FormatoParagrafo(Elenco: tipo));
        _testo.Modified = true;
        _annunciatore.Annuncia(tipo switch
        {
            TipoElenco.Puntato => "Elenco puntato",
            TipoElenco.Numerato => "Elenco numerato",
            _ => "Punti elenco eliminati",
        });
    }

    private void CambiaBarrato()
    {
        bool attivo = RichEditNativo.LeggiFormatoCarattere(_testo).Barrato != true;
        RichEditNativo.ImpostaFormatoCarattere(_testo, new FormatoCarattere(Barrato: attivo));
        _testo.Modified = true;
        _annunciatore.Annuncia(attivo ? "Barrato attivato" : "Barrato disattivato");
    }

    private void CambiaACapo()
    {
        _testo.WordWrap = !_testo.WordWrap;
        _impostazioni.ACapoAutomatico = _testo.WordWrap;
        _annunciatore.Annuncia(_testo.WordWrap ? "A capo automatico attivato" : "A capo automatico disattivato");
    }

    private void ImpostaInterlinea(float interlinea, string nome)
    {
        RichEditNativo.ImpostaFormatoParagrafo(_testo, new FormatoParagrafo(Interlinea: interlinea));
        _testo.Modified = true;
        _annunciatore.Annuncia($"Interlinea {nome}");
    }

    private void ApriCarattere()
    {
        var caratteri = FontFamily.Families.Select(f => f.Name).Where(n => !n.StartsWith('@')).Order().ToList();
        using var finestra = new FinestraCarattere(RichEditNativo.LeggiFormatoCarattere(_testo), caratteri);
        if (finestra.ShowDialog(this) != DialogResult.OK)
            return;
        RichEditNativo.ImpostaFormatoCarattere(_testo, finestra.Modifiche);
        _testo.Modified = true;
        if (finestra.Predefinito && finestra.Base.Carattere is string carattere)
        {
            _impostazioni.CarattereBase = carattere;
            _impostazioni.DimensioneBase = finestra.Base.Dimensione;
            _impostazioni.Salva();
        }
    }

    private void ApriParagrafo()
    {
        using var finestra = new FinestraParagrafo(RichEditNativo.LeggiFormatoParagrafo(_testo), RichEditNativo.LivelloTitolo(_testo));
        if (finestra.ShowDialog(this) != DialogResult.OK)
            return;
        RichEditNativo.ImpostaFormatoParagrafo(_testo, finestra.Modifiche);
        if (finestra.LivelloTitolo is int livello)
            ImpostaTitolo(livello);
        _testo.Modified = true;
        if (finestra.Predefinito)
        {
            _impostazioni.ParagrafoBase = finestra.Tutto;
            _impostazioni.Salva();
        }
    }

    /// <summary>La selezione, o la parola dove si trova il cursore se non c'è selezione.</summary>
    private (int Inizio, int Lunghezza) SelezioneOParola()
    {
        int inizio = _testo.SelectionStart;
        int lunghezza = _testo.SelectionLength;
        if (lunghezza > 0)
            return (inizio, lunghezza);
        var testo = _testo.Text;
        while (inizio > 0 && char.IsLetterOrDigit(testo[inizio - 1]))
            inizio--;
        int fine = _testo.SelectionStart;
        while (fine < testo.Length && char.IsLetterOrDigit(testo[fine]))
            fine++;
        return (inizio, fine - inizio);
    }

    /// <summary>MAIUSCOLO, minuscolo o Iniziali maiuscole sulla selezione o sulla parola del cursore.</summary>
    private void ImpostaMaiuscole(Maiuscole tipo, string nome)
    {
        var (inizio, lunghezza) = SelezioneOParola();
        if (lunghezza == 0)
        {
            _annunciatore.Annuncia("Nessuna parola da cambiare");
            return;
        }
        int selezioneInizio = _testo.SelectionStart, selezioneLunghezza = _testo.SelectionLength;
        RichEditNativo.CambiaMaiuscole(_testo, inizio, inizio + lunghezza, tipo);
        _testo.Select(selezioneInizio, selezioneLunghezza);
        _testo.Modified = true;
        _annunciatore.Annuncia(nome);
    }

    /// <summary>Alterna MAIUSCOLO, minuscolo e Iniziali maiuscole.</summary>
    private void CambiaMaiuscole()
    {
        var (inizio, lunghezza) = SelezioneOParola();
        var parte = _testo.Text.Substring(inizio, lunghezza);
        if (parte == parte.ToUpperInvariant() && parte != parte.ToLowerInvariant())
            ImpostaMaiuscole(Maiuscole.Minuscolo, "minuscolo");
        else if (parte == parte.ToLowerInvariant())
            ImpostaMaiuscole(Maiuscole.Iniziali, "Iniziali maiuscole");
        else
            ImpostaMaiuscole(Maiuscole.Maiuscolo, "MAIUSCOLO");
    }

    private void Allinea(Allineamento allineamento, string descrizione)
    {
        RichEditNativo.ImpostaAllineamento(_testo, allineamento);
        _testo.Modified = true;
        _annunciatore.Annuncia(descrizione);
    }

    private void CambiaZoom(float variazione) => ImpostaZoom(_impostazioni.Zoom + variazione);

    private void ImpostaZoom(float zoom)
    {
        _impostazioni.Zoom = Math.Clamp(zoom, Impostazioni.ZoomMinimo, Impostazioni.ZoomMassimo);
        ApplicaZoom();
        _annunciatore.Annuncia($"Zoom {_impostazioni.Zoom * 100:0} per cento");
    }

    private void ApplicaZoom()
    {
        // RichTextBox ignora un valore uguale a quello che crede di avere, ma lo zoom vero si azzera
        // a ogni caricamento: passando da 1 lo si forza a riapplicarlo.
        _testo.ZoomFactor = 1f;
        _testo.ZoomFactor = _impostazioni.Zoom;
        AdattaRigaBrailleAlloZoom();
        _barra.Ingrandisci(_impostazioni.Zoom);
        AggiornaBarre();
    }

    private void AnnunciaPosizione()
    {
        var (pagina, totale, _) = PaginaCorrente();
        var cella = StrutturaTesto.CellaDi(_testo.Text, _testo.SelectionStart) is { } c
            ? $"Tabella {c.NumeroTabella + 1}, {Coordinate(_testo.Text, c)}, "
            : "";
        _annunciatore.Annuncia($"{cella}{PosizioneCorrente().Descrizione()}, pagina {pagina} di {totale}");
    }

    private void AnnunciaFormattazione()
    {
        _annunciatore.Annuncia(DescrizioneImmagineQui() + DescrizioneNotaQui() + DescrizioneCommentoQui() + DescrizioneFormato.Descrivi(
            RichEditNativo.LivelloTitolo(_testo),
            RichEditNativo.LeggiFormatoCarattere(_testo),
            RichEditNativo.LeggiFormatoParagrafo(_testo)));
    }

    private void AnnunciaStatistiche()
    {
        if (_testo.SelectionLength > 0)
            _annunciatore.Annuncia($"Selezione: {Statistiche.Calcola(_testo.SelectedText).Descrizione()}");
        else
            _annunciatore.Annuncia(Statistiche.Calcola(_testo.Text).Descrizione());
    }
}
