using Eleu.Correzione;
using Eleu.Documento;
using Eleu.Importa;
using Eleu.Interfaccia;

namespace Eleu;

// Apertura di PDF e immagini (con OCR) e correzione assistita degli errori probabili.
public sealed partial class FinestraPrincipale
{
    private ControlloOrtografico? _correttore;
    private bool _correttoreCercato;
    private ErroreProbabile? _ultimoErrore;

    private ControlloOrtografico? Correttore
    {
        get
        {
            if (!_correttoreCercato)
            {
                _correttore = ControlloOrtografico.Crea("it-IT");
                _correttoreCercato = true;
            }
            return _correttore;
        }
    }

    private void ImportaFile(string percorso)
    {
        var nome = Path.GetFileName(percorso);
        TestoImportato? risultato = null;
        Exception? errore = null;
        bool annullato = false;

        using (var avanzamento = new FinestraAvanzamento($"Apertura di {nome}"))
        {
            var progresso = new Progress<AvanzamentoImportazione>(a =>
            {
                var stato = a.TotalePagine > 1
                    ? $"Riconoscimento del testo, pagina {a.Pagina} di {a.TotalePagine}"
                    : "Riconoscimento del testo";
                avanzamento.Aggiorna(stato, a.Pagina - 1, a.TotalePagine);
                _annunciatore.Annuncia(stato);
            });
            avanzamento.Shown += async (_, _) =>
            {
                try
                {
                    risultato = await Importatore.ImportaAsync(percorso, progresso, avanzamento.Annullamento);
                }
                catch (OperationCanceledException)
                {
                    annullato = true;
                }
                catch (Exception e) // Al confine con l'utente: l'errore diventa un messaggio.
                {
                    errore = e;
                }
                finally
                {
                    avanzamento.DialogResult = DialogResult.OK;
                }
            };
            avanzamento.ShowDialog(this);
        }

        if (annullato)
        {
            AnnunciaTraPoco("Apertura annullata");
            return;
        }
        if (errore is not null || risultato is null)
        {
            MessageBox.Show(this, $"Impossibile aprire {nome}.\n\n{errore?.Message}", NomeApp, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        if (risultato.Paragrafi.Count == 0)
        {
            MessageBox.Show(this, $"Non ho trovato testo in {nome}.", NomeApp, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var contenuto = new ContenutoDocumento();
        foreach (var testo in risultato.Paragrafi)
        {
            var paragrafo = new Paragrafo();
            paragrafo.Aggiungi(new Segmento(testo));
            contenuto.Paragrafi.Add(paragrafo);
        }
        CaricaContenuto(contenuto);

        // Diventa un nuovo documento: il PDF o l'immagine non vengono mai sovrascritti.
        var proposto = Path.Combine(Path.GetDirectoryName(percorso)!, Path.GetFileNameWithoutExtension(percorso) + ".docx");
        ImpostaDocumentoCorrente(null, FormatoFile.Docx, proposto);
        _impostazioni.AggiungiRecente(percorso);
        _testo.Modified = true;
        AggiornaTitolo();

        var pagine = risultato.Pagine == 1 ? "1 pagina" : $"{risultato.Pagine} pagine";
        var paragrafi = risultato.Paragrafi.Count == 1 ? "1 paragrafo" : $"{risultato.Paragrafi.Count} paragrafi";
        AnnunciaTraPoco(risultato.ConOcr
            ? $"Aperto {nome}: {pagine}, {paragrafi}. Il testo è stato riconosciuto da un'immagine: con F7 vai agli errori probabili."
            : $"Aperto {nome}: {pagine}, {paragrafi}.");
    }
}
