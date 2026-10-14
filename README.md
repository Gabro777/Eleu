<p align="center">
  <img src="docs/icon.png" alt="Icona di Eleu: un foglio bianco su sfondo blu con la lettera e in Braille: i punti 1 e 5 in rilievo, neri" width="128" height="128">
</p>

<h1 align="center">Eleu</h1>

<p align="center">
  Il programma di scrittura per Windows pensato per persone <strong>cieche e ipovedenti</strong>.<br>
  Gratuito, libero e in italiano.
</p>

<p align="center">
  <a href="https://github.com/Gabro777/Eleu/releases/latest"><strong>Scarica l'ultima versione</strong></a>
  ·
  <a href="#cosa-sa-fare">Cosa sa fare</a>
  ·
  <a href="#primi-passi">Primi passi</a>
  ·
  <a href="#segnalare-un-problema-o-proporre-unidea">Segnala un problema</a>
</p>

## Cos'è Eleu

Eleu è un programma per scrivere, leggere e stampare documenti, fatto per chi usa uno screen reader o vede poco.

- **Tutto dalla tastiera.** Ogni comando ha il suo posto nei menu e quasi sempre una scorciatoia. Non serve il mouse.
- **Eleu dice sempre cosa ha fatto.** Salvato, corretto, entrato in una tabella, arrivato su un titolo: lo screen
  reader lo annuncia, oppure lo segnala un suono breve.
- **Pensato per NVDA, JAWS e l'Assistente vocale di Windows.** Ogni finestra ha i nomi giusti per lo screen reader e
  si chiude con Esc.
- **Il Braille al centro, non in aggiunta.** File BRF, stampa Braille, scrittura a sei tasti, perfino le immagini in
  rilievo.
- **Per chi vede poco** ci sono testo ingrandibile, pulsanti grandi con il nome scritto e il righello.
- **Apre i documenti di tutti i giorni**: Word, LibreOffice, PDF, pagine web, libri EPUB, foto di pagine scritte; legge
  i fogli di carta direttamente dallo scanner.

Eleu è **gratuito** e lo resterà: è software libero con licenza GNU GPL 3.

## Scaricare e installare

1. Vai alla [pagina dell'ultima versione](https://github.com/Gabro777/Eleu/releases/latest) e scarica il file
   `Eleu-<versione>.msi`, nella sezione "Assets" in fondo alla pagina.
2. Aprilo. Eleu si installa per il tuo utente, senza permessi di amministratore e senza dover installare nient'altro:
   contiene già tutto ciò che gli serve.
3. Lo trovi nel menu Start. Dentro Eleu, **F1** apre la guida.

**Requisiti:** Windows 10 o 11 a 64 bit. Il controllo ortografico in italiano usa la lingua italiana di Windows; la
lettura ad alta voce usa le voci di Windows (quelle italiane ci sono già nelle versioni italiane di Windows).

**Aggiornamenti:** una volta al giorno Eleu controlla se c'è una versione nuova e, se vuoi, la installa da solo.
Si può spegnere in Strumenti, Opzioni.

### Se Windows dice che l'autore è sconosciuto

L'installer di Eleu non ha ancora una firma digitale: per questo Windows, e a volte il browser, avvisano che il file
non è riconosciuto o che l'autore è sconosciuto. Succede con tutti i programmi non firmati scaricati da internet e
non vuol dire che il file sia pericoloso. Per installare comunque:

- **Se il browser blocca lo scaricamento** (in Edge: "non viene scaricato comunemente"): apri l'elenco dei download
  con Ctrl+J, sul file scegli il menu "Altre azioni" e poi **Mantieni**; nella finestra che si apre scegli
  **Mostra altro** e poi **Mantieni comunque**.
- **Se all'apertura compare "Windows ha protetto il PC"**: premi Tab fino al collegamento **Ulteriori informazioni**
  e premi Invio; poi premi Tab fino al pulsante **Esegui comunque** e premi Invio.
- **In alternativa, prima di aprirlo**: nella cartella Download seleziona il file, premi Alt+Invio per aprire le
  proprietà, nella scheda Generale spunta **Annulla blocco** (in fondo, alla voce Sicurezza) e premi OK. Da quel
  momento Windows non mostra più l'avviso per quel file.

Una volta installato, l'avviso non ricompare.

## Cosa sa fare

### Scrivere e correggere

- Apre e salva documenti **Word (DOCX)**, **LibreOffice (ODT)**, **RTF**, **testo semplice**, **pagine web (HTML)** e
  **libri EPUB**.
- Legge i **PDF** e le **foto o scansioni di pagine scritte**, con il riconoscimento dei caratteri di Windows, e
  **acquisisce direttamente dallo scanner** (Ctrl+Alt+S): una pagina alla volta dal vetro o tutte dall'alimentatore.
- Carattere, paragrafo, titoli su nove livelli, elenchi, interruzioni di pagina, intestazione e piè di pagina.
- **Controllo ortografico e della punteggiatura**: F7 porta al prossimo errore, Ctrl+F7 lo corregge, il tasto
  Applicazioni sulla parola sbagliata propone le correzioni. Dizionario personale.
- Trova e sostituisci, anche con i caratteri jolly; sostituzioni multiple; pulizia del testo copiato dai PDF.
- Selezione a blocchi con F8, simboli che non sono sulla tastiera, ricerca della parola sul web.
- Statistiche con l'indice di leggibilità Gulpease, parole più usate, confronto tra due documenti.
- Recupero automatico del testo se il computer si spegne all'improvviso; riaprendo un documento si riparte da dove
  eri rimasto.

### Muoversi nel documento

- **Tabelle**: Ctrl+Alt e le frecce passano da una cella all'altra; entrando in una cella Eleu dice riga, colonna e
  nome della colonna.
- **Titoli, segnalibri, note a piè di pagina, commenti, immagini**: per ognuno lo stesso schema di comandi nel menu
  Inserisci (aggiungi, elimina, precedente, successivo, elenco).
- **Immagini con la descrizione** per lo screen reader, conservata anche nei file Word e LibreOffice.
- **Commenti** di Word e LibreOffice: si leggono, si aggiungono e si conservano; Eleu avvisa quando arrivi su un testo
  commentato.
- **Segnali sonori** quando il cursore arriva su un titolo, un segnalibro, una nota, un commento o una parola sbagliata:
  ti orienti senza ascoltare lunghe frasi. Si possono avere anche a voce, o spegnere.

### Ascoltare

- Lettura ad alta voce con le voci di Windows, anche quelle acquistate a parte (SAPI): F11 legge e mette in pausa,
  Esc ferma lasciando il cursore dove sei arrivato.
- **Audiolibri MP3**, un file per capitolo, e **libri DAISY** con testo e voce sincronizzati.
- Pronunce personali per parole e sigle.

### Il Braille

- Braille italiano con liblouis: **file BRF** e **stampa Braille** con numero di pagina, titolo in cima, margine per
  la rilegatura, pagine a stampa, sommario e intervallo di pagine.
- **Interlinea doppia**, **stampa fronte e retro** (sul retro margine e numero passano dalla parte giusta) e **due
  righe libere a metà foglio**, per piegarlo e spedirlo senza schiacciare i punti.
- Anteprima Braille, riga corrente in Braille in fondo alla finestra, copia della selezione già in Braille.
- **Scrittura a sei tasti** (F D S J K L) come con una dattilobraille.
- **Immagini in rilievo**: un'immagine diventa grafica a punti per la stampante Braille, con un'anteprima che si legge
  dal display Braille, oppure un disegno a tratti spessi per la carta a microcapsule, con la didascalia in Braille.

### Per chi vede poco

- Testo ingrandibile, colonna di pulsanti grandi con il nome scritto, righello con i rientri, avvio a tutto schermo.
- Errori sottolineati in rosso mentre scrivi.

## Primi passi

All'avvio Eleu apre un documento vuoto: si comincia a scrivere subito. I menu si aprono con Alt e la lettera
sottolineata; accanto a ogni comando c'è la sua scorciatoia.

| Cosa | Tasti |
| --- | --- |
| Guida di Eleu | F1 |
| Nuovo, apri, salva, salva con nome | Ctrl+N, Ctrl+O, Ctrl+S, F12 |
| Dove sono (riga, pagina, cella) | F6 |
| Formattazione del punto in cui sono | Alt+Invio |
| Errore successivo, correggi | F7, Ctrl+F7 |
| Menu del punto in cui sono | tasto Applicazioni o Maiusc+F10 |
| Leggi o metti in pausa, leggi tutto, ferma | F11, Ctrl+F11, Esc |
| Trova, sostituisci, vai a | Ctrl+F, Ctrl+H, Ctrl+G |
| Carattere, paragrafo | Ctrl+Maiusc+F, Ctrl+Maiusc+P |
| Stampa, stampa Braille | Ctrl+P, Ctrl+Alt+P |
| Cella vicina in una tabella | Ctrl+Alt+frecce |
| Scrittura Braille a sei tasti | Ctrl+Maiusc+6 |
| Opzioni | Ctrl+Maiusc+O |

L'elenco completo è in Aiuto, Scorciatoie da tastiera; la guida (F1) spiega tutto il resto.

## Segnalare un problema o proporre un'idea

Eleu cresce con i suggerimenti di chi lo usa. Se qualcosa non funziona, se lo screen reader dice una cosa poco
chiara o se ti manca un comando:

- apri una segnalazione nella pagina [Issues](https://github.com/Gabro777/Eleu/issues) (serve un account GitHub
  gratuito);
- scrivi cosa stavi facendo, cosa ti aspettavi e cosa è successo, e quale screen reader usi;
- la versione di Eleu è in Aiuto, Informazioni su Eleu.

## Privacy

Eleu non raccoglie dati e non manda i tuoi documenti da nessuna parte. Si collega a internet solo per due cose:
controllare su GitHub se c'è una versione nuova (si spegne in Strumenti, Opzioni) e, quando lo chiedi tu, cercare una
parola sul web aprendo il browser.

## Cose da sapere

- Eleu è per Windows e, per ora, solo in italiano; il Braille è quello italiano.
- Con l'Assistente vocale di Windows il documento si presenta come "RichEdit Control": è il nome fisso del controllo
  di testo di Windows. NVDA e JAWS lo chiamano "Documento".
- Dei documenti Word non si conservano celle unite e tabelle dentro le tabelle; le revisioni si leggono già accettate.
  In questi casi Eleu lo dice e non salva sopra l'originale.

## Per chi sviluppa

Eleu è scritto in C# con Windows Forms (.NET 10). Serve Windows 10 o 11 e il .NET SDK 10.

```bash
dotnet build
dotnet test
dotnet run --project src/Eleu
```

- `src/Eleu`: il programma; `tests/Eleu.Tests`: le prove automatiche; `installer`: il pacchetto di installazione.
- L'installer si costruisce con `./build-installer.ps1`, che crea `artifacts/Eleu-<versione>.msi` (con WiX 5 come
  strumento locale del progetto). Su GitHub si costruisce a ogni modifica; con un tag `vX.Y.Z` va in una release.
- [docs/ROADMAP.md](docs/ROADMAP.md) è il piano di lavoro, [docs/NOVITA.md](docs/NOVITA.md) le novità della versione.

I contributi sono benvenuti: per una modifica grande conviene prima aprire una segnalazione e parlarne. Ogni
modifica deve restare usabile da tastiera e con lo screen reader.

## Autore e licenza

Eleu è sviluppato da **Gabriele Cusimano** ([github.com/Gabro777](https://github.com/Gabro777)).

È software libero e gratuito, distribuito con la licenza **GNU General Public License versione 3** (o, a tua scelta,
una versione successiva): puoi usarlo, studiarlo, modificarlo e ridistribuirlo, a patto che anche le versioni
modificate restino libere con la stessa licenza. Il testo completo è nel file [LICENSE](LICENSE).

Copyright © 2026 Gabriele Cusimano.

### Componenti usati

- [liblouis](https://liblouis.io) per la traduzione Braille (LGPL 2.1)
- [Open XML SDK](https://github.com/dotnet/Open-XML-SDK) per i file Word (MIT)
- [NAudio](https://github.com/naudio/NAudio) per l'audio (MIT)
- [PdfPig](https://github.com/UglyToad/PdfPig) per i PDF (Apache 2.0)
- [Html Agility Pack](https://html-agility-pack.net) per HTML ed EPUB (MIT)
- Le voci, il riconoscimento dei caratteri e il controllo ortografico sono quelli di Windows
