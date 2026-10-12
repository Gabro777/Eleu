using Eleu.Accessibilita;
using Eleu.Documento;
using Eleu.Interfaccia;
using Eleu.Voce;

namespace Eleu;

// Lettura ad alta voce ed esportazione dell'audiolibro.
public sealed partial class FinestraPrincipale
{
    private CatalogoVoci? _catalogo;
    private LettoreVocale? _lettore;
    private LettoreVocale? _lettoreProva;
    private int? _cursoreInPausa;

    private CatalogoVoci Catalogo
    {
        get
        {
            _catalogo ??= CatalogoVoci.Predefinito();
            _catalogo.Pronunce = _impostazioni.Pronunce;
            return _catalogo;
        }
    }

    private void GestisciPronunce()
    {
        using var finestra = new FinestraPronunce(_impostazioni.Pronunce);
        if (finestra.ShowDialog(this) != DialogResult.OK)
            return;
        _impostazioni.Pronunce = finestra.Pronunce;
        _impostazioni.Salva();
        AnnunciaTraPoco(finestra.Pronunce.Count == 1 ? "1 pronuncia" : $"{finestra.Pronunce.Count} pronunce");
    }

    private ParametriVoce ParametriVoce => new(_impostazioni.VelocitaVoce, _impostazioni.VolumeVoce);

    private LettoreVocale Lettore
    {
        get
        {
            if (_lettore is null)
            {
                _lettore = new LettoreVocale(SintetizzaConVoceScelta);
                _lettore.Terminato += (_, _) => BeginInvoke(() => SuonoConferma(Segnale.FineLettura));
                _lettore.Errore += (_, e) => MessageBox.Show(this, $"La sintesi vocale si è interrotta.\n\n{e.Message}", NomeApp,
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            return _lettore;
        }
    }

    private Task<AudioSintetizzato> SintetizzaConVoceScelta(string testo, CancellationToken annulla)
    {
        var voce = Catalogo.Scegli(_impostazioni.VoceId) ?? throw new InvalidOperationException("Nessuna voce installata.");
        return Catalogo.SintetizzaAsync(testo, voce, ParametriVoce, annulla);
    }

    /// <summary>F5: legge dal cursore (o la selezione); durante la lettura mette in pausa e riprende.</summary>
    private void LeggiOPausa()
    {
        if (_lettore is { InLettura: true })
        {
            if (!_lettore.InPausa)
            {
                _lettore.Pausa();
                PortaCursoreAllaLettura();
                _cursoreInPausa = _testo.SelectionStart;
                return;
            }
            // Se in pausa si è spostato il cursore, si riparte da lì; altrimenti si riprende.
            if (_cursoreInPausa == _testo.SelectionStart)
            {
                _lettore.Riprendi();
                return;
            }
        }

        int inizio = _testo.SelectionStart;
        int fine = _testo.SelectionLength > 0 ? inizio + _testo.SelectionLength : _testo.TextLength;
        AvviaLettura(inizio, fine);
    }

    private void LeggiTutto() => AvviaLettura(0, _testo.TextLength);

    private void AvviaLettura(int inizio, int fine)
    {
        if (Catalogo.Scegli(_impostazioni.VoceId) is null)
        {
            MostraNessunaVoce();
            return;
        }
        var brani = Brani.DaTesto(_testo.Text, inizio, fine);
        if (brani.Count == 0)
        {
            _annunciatore.Annuncia("Niente da leggere");
            return;
        }
        _cursoreInPausa = null;
        Lettore.Avvia(brani);
    }

    /// <summary>Ferma la lettura lasciando il cursore sulla parola raggiunta. Restituisce false se non si stava leggendo.</summary>
    private bool FermaLettura()
    {
        if (_lettore is not { InLettura: true })
            return false;
        PortaCursoreAllaLettura();
        _lettore.Ferma();
        return true;
    }

    private void PortaCursoreAllaLettura()
    {
        if (_lettore?.PosizioneCorrente is int posizione)
        {
            _testo.Select(Math.Min(posizione, _testo.TextLength), 0);
            _testo.ScrollToCaret();
        }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape && FermaLettura())
            return true;
        if (TastoAlternativo(keyData))
            return true;
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void ScegliVoce()
    {
        var voci = Catalogo.Voci;
        if (voci.Count == 0)
        {
            MostraNessunaVoce();
            return;
        }

        using var finestra = new FinestraVoce(voci, Catalogo.Scegli(_impostazioni.VoceId), ParametriVoce);
        finestra.ProvaRichiesta += (_, _) =>
        {
            if (finestra.VoceScelta is not VoceDisponibile voce)
                return;
            var parametri = finestra.Parametri;
            _lettoreProva?.Dispose();
            _lettoreProva = new LettoreVocale((testo, annulla) => Catalogo.SintetizzaAsync(testo, voce, parametri, annulla));
            _lettoreProva.Avvia([new Brano($"Ciao, sono {voce.Nome}. Così leggerò i tuoi documenti.", 0)]);
        };

        if (finestra.ShowDialog(this) == DialogResult.OK && finestra.VoceScelta is VoceDisponibile scelta)
        {
            _impostazioni.VoceId = scelta.Id;
            _impostazioni.VelocitaVoce = finestra.Parametri.Velocita;
            _impostazioni.VolumeVoce = finestra.Parametri.Volume;
            _impostazioni.Salva();
        }
        _lettoreProva?.Ferma();
    }

    private void EsportaAudiolibro()
    {
        FermaLettura();
        if (Catalogo.Scegli(_impostazioni.VoceId) is not VoceDisponibile voce)
        {
            MostraNessunaVoce();
            return;
        }

        var capitoli = Brani.Capitoli(ContenutoConNote(), Path.GetFileNameWithoutExtension(NomeDocumento));
        if (capitoli.Count == 0)
        {
            _annunciatore.Annuncia("Il documento è vuoto");
            return;
        }

        using var dialogo = new FolderBrowserDialog
        {
            Description = "Scegli la cartella in cui salvare l'audiolibro",
            UseDescriptionForTitle = true,
            InitialDirectory = CartellaIniziale(),
            ShowNewFolderButton = true,
        };
        if (dialogo.ShowDialog(this) != DialogResult.OK)
            return;
        var cartella = dialogo.SelectedPath;

        int giaPresenti = EsportatoreAudiolibro.NomiFile(capitoli).Count(n => File.Exists(Path.Combine(cartella, n)));
        if (giaPresenti > 0 && MessageBox.Show(this,
                $"Nella cartella ci sono già {giaPresenti} file con lo stesso nome. Vuoi sostituirli?", NomeApp,
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        IReadOnlyList<string> creati = [];
        Exception? errore = null;
        bool annullato = false;

        using (var avanzamento = new FinestraAvanzamento("Creazione dell'audiolibro"))
        {
            var progresso = new Progress<AvanzamentoEsportazione>(a =>
            {
                var stato = $"Capitolo {a.Capitolo} di {a.TotaleCapitoli}: {a.Titolo}";
                avanzamento.Aggiorna(stato, a.Capitolo - 1, a.TotaleCapitoli);
                _annunciatore.Annuncia(stato);
            });
            avanzamento.Shown += async (_, _) =>
            {
                try
                {
                    creati = await new EsportatoreAudiolibro(Catalogo).EsportaAsync(
                        capitoli, cartella, voce, ParametriVoce, progresso, avanzamento.Annullamento);
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
            AnnunciaTraPoco("Creazione dell'audiolibro annullata");
        else if (errore is not null)
            MessageBox.Show(this, $"Impossibile creare l'audiolibro.\n\n{errore.Message}", NomeApp,
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        else
            MessageBox.Show(this,
                $"Audiolibro creato: {(creati.Count == 1 ? "1 file MP3" : $"{creati.Count} file MP3")} nella cartella {cartella}.",
                NomeApp, MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    /// <summary>
    /// Voce > Crea un libro DAISY: in una cartella nuova col nome del documento, il libro DAISY 2.02 con testo
    /// e audio sincronizzati, da ascoltare con un lettore DAISY andando di titolo in titolo.
    /// </summary>
    private void EsportaDaisy()
    {
        FermaLettura();
        if (Catalogo.Scegli(_impostazioni.VoceId) is not VoceDisponibile voce)
        {
            MostraNessunaVoce();
            return;
        }
        var contenuto = ContenutoConNote();
        contenuto.Metadati = _metadati.Titolo.Length > 0 ? _metadati : _metadati with { Titolo = Path.GetFileNameWithoutExtension(NomeDocumento) };
        if (EsportatoreDaisy.Sezioni(contenuto, contenuto.Metadati.Titolo).Count == 0)
        {
            _annunciatore.Annuncia("Il documento è vuoto");
            return;
        }

        using var dialogo = new FolderBrowserDialog
        {
            Description = "Scegli dove creare il libro DAISY (verrà fatta una cartella col nome del documento)",
            UseDescriptionForTitle = true,
            InitialDirectory = CartellaIniziale(),
            ShowNewFolderButton = true,
        };
        if (dialogo.ShowDialog(this) != DialogResult.OK)
            return;
        var nome = Brani.NomeFile(1, 1, contenuto.Metadati.Titolo)["01 - ".Length..^".mp3".Length]; // il titolo pulito per un nome di cartella
        var cartella = Path.Combine(dialogo.SelectedPath, $"{nome} (DAISY)");
        if (Directory.Exists(cartella) && Directory.EnumerateFileSystemEntries(cartella).Any())
        {
            if (MessageBox.Show(this, $"La cartella {Path.GetFileName(cartella)} esiste già e non è vuota. Vuoi sostituire il libro che contiene?",
                    NomeApp, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;
            foreach (var vecchio in Directory.EnumerateFiles(cartella).Where(f => Path.GetExtension(f) is ".smil" or ".mp3" or ".html"))
                File.Delete(vecchio);
        }

        int file = 0;
        Exception? errore = null;
        bool annullato = false;
        using (var avanzamento = new FinestraAvanzamento("Creazione del libro DAISY"))
        {
            var progresso = new Progress<AvanzamentoEsportazione>(a =>
            {
                var stato = $"Sezione {a.Capitolo} di {a.TotaleCapitoli}: {a.Titolo}";
                avanzamento.Aggiorna(stato, a.Capitolo - 1, a.TotaleCapitoli);
                _annunciatore.Annuncia(stato);
            });
            avanzamento.Shown += async (_, _) =>
            {
                try
                {
                    file = await new EsportatoreDaisy(Catalogo).EsportaAsync(contenuto, cartella, voce, ParametriVoce, progresso, avanzamento.Annullamento);
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
            AnnunciaTraPoco("Creazione del libro DAISY annullata");
        else if (errore is not null)
            MessageBox.Show(this, $"Impossibile creare il libro DAISY.\n\n{errore.Message}", NomeApp, MessageBoxButtons.OK, MessageBoxIcon.Error);
        else
            MessageBox.Show(this, $"Libro DAISY creato nella cartella {cartella}: {file} file. Si apre con un lettore DAISY partendo da ncc.html.",
                NomeApp, MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void MostraNessunaVoce() =>
        MessageBox.Show(this,
            "Non ho trovato voci di sintesi installate.\nPuoi aggiungerle da Impostazioni di Windows, Ora e lingua, Voce.",
            NomeApp, MessageBoxButtons.OK, MessageBoxIcon.Warning);
}
