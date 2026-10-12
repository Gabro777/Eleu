using Eleu.Documento;
using Eleu.Interfaccia;

namespace Eleu;

// Proprietà del documento (File > Proprietà): titolo, autore, oggetto, parole chiave.
public sealed partial class FinestraPrincipale
{
    private Metadati _metadati = new();

    /// <summary>Il titolo del documento ({titolo} in intestazione e piè di pagina): quello delle proprietà, se c'è.</summary>
    private string TitoloDocumento => _metadati.Titolo.Length > 0 ? _metadati.Titolo : NomeDocumento;

    /// <summary>L'RTF da salvare: testo con segnalibri, note e immagini, pagina e proprietà del documento.</summary>
    private string RtfCompleto() => _metadati.InRtf(RtfScrittore.ConPagina(RtfDaSalvare(), _pagina));

    private void ApriProprieta()
    {
        var statistiche = Statistiche.Calcola(_testo.Text);
        using var finestra = new FinestraProprieta(_metadati, _percorso, statistiche.Descrizione());
        if (finestra.ShowDialog(this) != DialogResult.OK || finestra.Risultato == _metadati)
            return;
        _metadati = finestra.Risultato;
        _testo.Modified = true;
        AnnunciaTraPoco("Proprietà del documento cambiate");
    }
}

/// <summary>
/// File > Proprietà: titolo, autore, oggetto e parole chiave, che leggono Word, i lettori di libri digitali e le
/// biblioteche; sotto, da leggere, il file e quando è stato creato.
/// </summary>
public sealed class FinestraProprieta : Modulo
{
    private readonly Metadati _iniziali;
    private readonly TextBox _titolo, _autore, _oggetto, _paroleChiave;

    public FinestraProprieta(Metadati metadati, string? percorso, string statistiche) : base("Proprietà del documento", 340)
    {
        _iniziali = metadati;
        _titolo = Campo("&Titolo:", new TextBox { Text = metadati.Titolo });
        _autore = Campo("&Autore:", new TextBox { Text = metadati.Autore });
        _oggetto = Campo("&Oggetto:", new TextBox { Text = metadati.Oggetto });
        _paroleChiave = Campo("&Parole chiave:", new TextBox { Text = metadati.ParoleChiave });
        var informazioni = new List<string>
        {
            percorso is null ? "Documento non ancora salvato" : $"File: {percorso}",
            metadati.Creato is DateTime creato ? $"Creato il {creato:d MMMM yyyy}" : "Data di creazione: al primo salvataggio",
            statistiche,
        };
        RigaIntera(new TextBox
        {
            Text = string.Join(Environment.NewLine, informazioni),
            ReadOnly = true,
            Multiline = true,
            Height = 70,
            AccessibleName = "Informazioni sul documento",
        });
        AggiungiPulsanti();
        ActiveControl = _titolo;
    }

    public Metadati Risultato => _iniziali with
    {
        Titolo = _titolo.Text.Trim(),
        Autore = _autore.Text.Trim(),
        Oggetto = _oggetto.Text.Trim(),
        ParoleChiave = _paroleChiave.Text.Trim(),
    };
}
