# Eleu 1.3.0

Eleu è un programma di scrittura per Windows pensato per persone cieche e ipovedenti: tutto dalla tastiera, con lo
screen reader (NVDA, JAWS, Assistente vocale), con il Braille e con la voce. Gratuito e libero, con licenza GNU GPL 3.

## Installazione

Scarica `Eleu.msi` (oppure `Eleu-1.3.0.msi`, è lo stesso file) e aprilo: Eleu si installa per il tuo utente, senza
permessi di amministratore e senza dover installare altro. Se c'è già una versione precedente, viene aggiornata.
Nella guida (F1) ci sono tutti i comandi.

Requisiti: Windows 10 o 11 a 64 bit. Per il controllo ortografico in italiano serve la lingua italiana di Windows;
per la voce, almeno una voce di Windows.

## Cosa sa fare

- **Documenti**: apre e salva Word (DOCX), LibreOffice (ODT), RTF, testo, HTML ed EPUB; legge PDF, foto di pagine
  scritte e fogli di carta direttamente dallo scanner o dalla fotocamera del telefono; apre anche i file Braille (BRF).
  Titoli, elenchi, tabelle, note a piè di pagina, segnalibri, commenti, immagini con la descrizione.
- **Scrivere e correggere**: controllo ortografico e della punteggiatura (F7), menu contestuale con le correzioni,
  trova e sostituisci, selezione a blocchi (F8), simboli, ricerca della parola sul web, statistiche.
- **Orientarsi**: Eleu dice sempre cosa ha fatto; segnali sonori brevi su titoli, segnalibri, note, commenti e parole
  sbagliate (oppure a voce); in tabella dice riga, colonna e nome della colonna.
- **Voce**: lettura ad alta voce con le voci di Windows, audiolibri MP3 e libri DAISY.
- **Braille**: file BRF e stampa Braille con numero di pagina, margine per la rilegatura, interlinea doppia, fronte e
  retro e righe libere a metà pagina per piegare il foglio; controllo prima della stampa; copia in nero per chi non
  legge il Braille; scrittura a sei tasti; immagini e grafici in rilievo.
- **Per chi vede poco**: testo ingrandibile, pulsanti grandi con il nome scritto, righello.
- **Aggiornamenti automatici**: una volta al giorno Eleu controlla se c'è una versione nuova e la installa.

## Novità di questa versione

- **Controllo prima della stampa Braille**: prima di stampare o salvare il Braille, Eleu elenca quello che verrebbe male
  (immagini senza descrizione, titoli vuoti, righe di soli simboli, interruzioni di pagina che lasciano un foglio quasi
  vuoto, tabelle la cui prima riga sembra già contenere dati, simboli che il Braille non sa scrivere, parole più lunghe
  della riga). Vai porta al punto, Correggi e Correggi tutto sistemano, Stampa comunque va avanti. Si può fare anche
  quando si vuole, da Strumenti, Controllo per il Braille.
- **Braille con il testo in nero**: nella finestra della stampa Braille, la casella «Anche una copia in nero (PDF)»
  crea un PDF con le pagine Braille disegnate e, sotto ogni parola Braille, la stessa parola in lettere. Per
  insegnanti, genitori e colleghi che non leggono il Braille. La casella è spenta di partenza.
- **Foto dal telefono** (File, Foto dal telefono): senza scanner, si fotografano le pagine con il telefono. Eleu mostra
  un codice QR e l'indirizzo scritto; dal telefono, sulla stessa rete Wi-Fi, si scatta la foto o si sceglie dalla
  galleria, anche più pagine insieme, ed Eleu ne riconosce il testo. Le foto restano nella rete di casa e vanno a un
  indirizzo segreto che cambia ogni volta, solo mentre la finestra è aperta.
- **Grafici in rilievo dalle tabelle** (Inserisci, Tabelle, Grafico in rilievo dalla tabella): un grafico a barre da
  toccare, per la stampante Braille o la carta a microcapsule, con i nomi e i valori nella didascalia in Braille.
- **Aprire i file BRF**: i file Braille fatti con Eleu o con altri programmi tornano testo da modificare; numeri di
  pagina, titoli ripetuti e righe di impaginazione vengono tolti e i paragrafi ricomposti.
- **La parola letta è evidenziata**: durante la lettura ad alta voce la parola che si sente è evidenziata e il testo
  scorre per tenerla in vista; il cursore non si sposta e lo screen reader non viene disturbato. Se il cursore è alla
  fine del testo, F11 legge dall'inizio.

## Da sapere

- Per le foto dal telefono, la prima volta Windows chiede se Eleu può usare la rete: scegli Consenti. Se Windows
  considera pubblica la rete di casa, il telefono viene bloccato: la finestra di Eleu lo avvisa e spiega come
  impostare la rete come privata.
- L'acquisizione da scanner (dalla versione 1.2.0) non è ancora stata provata con uno scanner vero: se il tuo scanner
  non viene trovato o qualcosa non va, segnalalo indicando il modello.

- La stampa Braille diretta e la carta a microcapsule sono da provare con stampanti diverse: se qualcosa non torna,
  segnalalo indicando il modello della stampante.
- Windows può dire che l'autore è sconosciuto, perché l'installer non ha ancora una firma digitale: come installarlo
  comunque è spiegato nel README, alla voce
  [Se Windows dice che l'autore è sconosciuto](https://github.com/Gabro777/Eleu#se-windows-dice-che-lautore-è-sconosciuto).
