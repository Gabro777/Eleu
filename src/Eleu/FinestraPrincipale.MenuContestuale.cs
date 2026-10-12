using Eleu.Correzione;
using Eleu.Documento;

namespace Eleu;

// Il menu contestuale del documento (tasto Applicazioni, Maiusc+F10 o clic destro): i comandi utili nel punto in
// cui si è. Su una parola sbagliata prima di tutto i suggerimenti; in una tabella, su una nota o su un'immagine
// i loro comandi; sempre taglia, copia, incolla e le proprietà dell'elemento.
public sealed partial class FinestraPrincipale
{
    private ContextMenuStrip CreaMenuContestuale()
    {
        var menu = new ContextMenuStrip { AccessibleName = "Menu del documento" };
        menu.Opening += (_, e) =>
        {
            menu.Items.Clear();
            var errore = ErroreQui();
            menu.AccessibleName = errore is null ? "Menu del documento" : $"Correzione di {Parlato.Di(errore.Parola)}";
            menu.Items.AddRange([.. VociContestuali(errore)]);
            e.Cancel = false;
        };
        return menu;
    }

    private IEnumerable<ToolStripItem> VociContestuali(ErroreProbabile? errore)
    {
        if (errore is not null)
        {
            foreach (var voce in VociErrore(errore))
                yield return voce;
            yield return new ToolStripSeparator();
        }

        bool selezione = _testo.SelectionLength > 0;
        yield return VoceContesto("&Taglia", "Ctrl+X", Taglia, selezione);
        yield return VoceContesto("&Copia", "Ctrl+C", Copia, selezione);
        yield return VoceContesto("&Incolla", "Ctrl+V", Incolla);
        yield return VoceContesto("Seleziona t&utto", "Ctrl+A", () => _testo.SelectAll());

        if (CellaQui(avvisa: false) is not null)
        {
            yield return new ToolStripSeparator();
            var tabella = new ToolStripMenuItem("T&abella");
            tabella.DropDownItems.AddRange(
            [
                VoceContesto("Inserisci riga sop&ra", null, () => InserisciRiga(sotto: false)),
                VoceContesto("Inserisci riga so&tto", null, () => InserisciRiga(sotto: true)),
                VoceContesto("Inserisci colonna a si&nistra", null, () => InserisciColonna(aDestra: false)),
                VoceContesto("Inserisci colonna a &destra", null, () => InserisciColonna(aDestra: true)),
                VoceContesto("Elimina ri&ga", null, EliminaRiga),
                VoceContesto("Elimina co&lonna", null, EliminaColonna),
                VoceContesto("&Elimina tabella", null, EliminaTabella),
            ]);
            yield return tabella;
        }
        if (NotaQui() is not null)
        {
            yield return new ToolStripSeparator();
            yield return VoceContesto("&Modifica nota...", null, ModificaNota);
            yield return VoceContesto("Elimina n&ota", null, EliminaNota);
        }
        if (CommentoQui() is not null)
        {
            yield return new ToolStripSeparator();
            yield return VoceContesto("A&scolta il commento", "Alt+Invio", AnnunciaFormattazione);
            yield return VoceContesto("Modi&fica commento...", null, ModificaCommento);
            yield return VoceContesto("Elimina comme&nto", null, EliminaCommento);
        }
        if (ImmagineQui() is not null)
        {
            yield return new ToolStripSeparator();
            yield return VoceContesto("&Modifica immagine...", null, ModificaImmagine);
            yield return VoceContesto("Elimina immagi&ne", null, EliminaImmagine);
            yield return VoceContesto("Gra&fica in rilievo...", null, GraficaInRilievo);
        }

        yield return new ToolStripSeparator();
        yield return VoceContesto("Ca&rattere...", "Ctrl+Maiusc+F", ApriCarattere);
        yield return VoceContesto("&Paragrafo...", "Ctrl+Maiusc+P", ApriParagrafo);
        yield return VoceContesto("&Leggi", "F11", LeggiOPausa);
        yield return VoceContesto("D&ettagli del punto", "Alt+Invio", AnnunciaFormattazione);
    }

    /// <summary>
    /// Le voci per l'errore sotto il cursore: i suggerimenti (la prima riga dice di che errore si tratta, così
    /// lo screen reader lo legge aprendo il menu), poi Ignora e Aggiungi al dizionario.
    /// </summary>
    private IEnumerable<ToolStripItem> VociErrore(ErroreProbabile errore)
    {
        void Correggi(string correzione)
        {
            _testo.Select(errore.Inizio, errore.Lunghezza);
            _testo.SelectedText = correzione;
            AnnunciaTraPoco(correzione.Any(char.IsLetterOrDigit) ? $"Corretto in {correzione}" : "Corretto");
        }

        List<string> suggerimenti = errore.Motivo is not null || Correttore is null
            ? (errore.Suggerimento is null ? [] : [errore.Suggerimento])
            : [.. Correttore.Suggerimenti(errore.Parola).Prepend(ConSuggerimento(errore).Suggerimento).OfType<string>().Distinct().Take(5)];

        if (suggerimenti.Count == 0)
            yield return new ToolStripMenuItem($"{Descrivi(errore)}") { Enabled = false };
        foreach (var suggerimento in suggerimenti)
        {
            var testo = suggerimento.Length == 0 ? "Togli" : suggerimento;
            var voce = VoceContesto(testo.Replace("&", "&&"), null, () => Correggi(suggerimento));
            voce.AccessibleDescription = errore.Motivo is null
                ? $"Correzione di {errore.Parola}"
                : $"{errore.Motivo}: {Parlato.Di(errore.Parola)}";
            yield return voce;
        }
        yield return VoceContesto("I&gnora tutti", null, () =>
        {
            _ignorateOggi.Add(errore.Parola);
            _annunciatore.Annuncia($"{errore.Parola} ignorata fino alla chiusura di Eleu");
        });
        if (errore.Motivo is null)
            yield return VoceContesto("Aggiungi al &dizionario", null, () =>
            {
                if (!_impostazioni.DizionarioPersonale.Contains(errore.Parola, StringComparer.CurrentCultureIgnoreCase))
                    _impostazioni.DizionarioPersonale.Add(errore.Parola);
                _impostazioni.Salva();
                _annunciatore.Annuncia($"{errore.Parola} aggiunta al dizionario personale");
            });
    }

    /// <summary>L'errore di ortografia o di punteggiatura in cui si trova il cursore, cercato solo nel suo paragrafo.</summary>
    private ErroreProbabile? ErroreQui()
    {
        var testo = _testo.Text;
        if (testo.Length == 0)
            return null;
        int qui = _testo.SelectionStart;
        var paragrafi = StrutturaTesto.Paragrafi(testo);
        var (inizio, fine) = paragrafi[StrutturaTesto.ParagrafoDi(paragrafi, qui)];
        var errore = TrovaErrori(testo[inizio..fine]).FirstOrDefault(e => qui - inizio >= e.Inizio && qui - inizio <= e.Inizio + e.Lunghezza);
        return errore is null ? null : errore with { Inizio = errore.Inizio + inizio };
    }

    /// <summary>
    /// Una voce del menu contestuale. La scorciatoia è solo scritta accanto: se fosse vera, il menu la
    /// eseguirebbe una seconda volta insieme a quella del menu principale.
    /// </summary>
    private ToolStripMenuItem VoceContesto(string testo, string? scorciatoia, Action azione, bool attiva = true) =>
        new(testo, null, (_, _) => BeginInvoke(azione))
        {
            ShortcutKeyDisplayString = scorciatoia,
            ShowShortcutKeys = scorciatoia is not null,
            Enabled = attiva,
        };
}
