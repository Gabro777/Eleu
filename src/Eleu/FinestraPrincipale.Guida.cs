using Eleu.Documento;

namespace Eleu;

// Aiuto > Guida di Eleu (F1): la guida è un documento di Eleu, da leggere con gli stessi tasti di sempre.
public sealed partial class FinestraPrincipale
{
    private readonly bool _apriGuida;

    private static string PercorsoGuida => Path.Combine(AppContext.BaseDirectory, "Guida", "Guida.html");

    /// <summary>Apre la guida qui, se la finestra è vuota, altrimenti in una finestra nuova; se è già aperta si va lì.</summary>
    private void ApriGuida()
    {
        var aperta = ContestoEleu.Finestre.FirstOrDefault(f => f._guida);
        if (aperta is not null)
        {
            aperta.Attiva();
            return;
        }
        if (DocumentoVuoto)
            CaricaGuida();
        else
            ContestoEleu.Apri(guida: true);
    }

    private bool _guida;

    private void CaricaGuida()
    {
        if (!File.Exists(PercorsoGuida))
        {
            _annunciatore.Annuncia("La guida non è stata installata insieme a Eleu");
            return;
        }
        var guida = HtmlFormato.Leggi(File.ReadAllText(PercorsoGuida));
        CaricaContenuto(guida);
        // Non ha un file: se la si cambia e la si salva, Eleu chiede dove (nei Documenti).
        ImpostaDocumentoCorrente(null, FormatoFile.Docx,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Guida di Eleu.docx"));
        _metadati = guida.Metadati;
        _guida = true;
        AnnunciaTraPoco("Guida di Eleu. Si passa da un titolo all'altro con Inserisci, Titoli, Titolo successivo; Ctrl+F4 la chiude.");
    }
}
