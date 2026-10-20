# Eleu 1.4.3

Eleu è un programma di scrittura per Windows pensato per persone cieche e ipovedenti: tutto dalla tastiera, con lo
screen reader (NVDA, JAWS, Assistente vocale), con il Braille e con la voce. Gratuito e libero, con licenza GNU GPL 3.

## Installazione

Scarica `Eleu.msi` (oppure `Eleu-1.4.3.msi`, è lo stesso file) e aprilo: Eleu si installa per il tuo utente, senza
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

- **Corretto l'aggiornamento alla 1.4.2**: su alcuni computer, dopo l'aggiornamento automatico alla 1.4.2, Eleu non
  partiva più e chiedeva di installare «.NET Desktop Runtime». Non serve installare .NET: basta scaricare `Eleu.msi`
  da questa pagina e aprirlo, Eleu torna a funzionare. D'ora in poi l'installazione copia sempre tutti i file.
- **Stampa Braille tramite il driver (ViewPlus) come nei programmi Braille che già funzionano con queste stampanti**:
  - **Misura del foglio**: si sceglie nella finestra (11,5 × 11, 12 × 11, 12 × 12, Lettera, A4 o un'altra misura) e
    Eleu la manda insieme alla stampa: il foglio impostato nel driver non conta più. La finestra dice quante celle e
    quante righe ci stanno e, se se ne chiedono di più, Eleu lo dice prima di stampare e propone quante ce ne stanno.
  - **Font giusto scelto da solo**: Braille29, oppure Braille36 o Braille36 US, cioè quelli con la tabella dei file
    Braille. Gli altri font che il driver installa (con un'altra lingua, come «Braille36 IT», o a 8 punti, come i
    «Computer») si possono scegliere, ma la finestra avvisa e prima di stampare Eleu chiede conferma: con quei font la
    punteggiatura e i simboli escono sbagliati. La lingua del Braille la decide comunque Eleu, non il font.
  - Righe alla distanza data dal font, mezzo pollice di margine in alto e a sinistra e, sul retro, celle spostate in
    modo che cadano tra quelle del fronte. Prima la prima riga poteva non uscire e alla prima e all'ultima riga
    mancavano dei punti.
- **I messaggi di Eleu si vedono anche a schermo** (dalla 1.4.1): quello che Eleu dice allo screen reader compare
  anche per qualche secondo in fondo alla barra di stato.

Dalla versione 1.4.0:

- **Pagina di prova per la stampante Braille**: nella finestra della stampa Braille, il pulsante «Pagina di prova»
  stampa una riga con le celle numerate fino all'ultima, due righe di simboli per controllare la tabella dei caratteri
  della stampante e poi le righe numerate fino all'ultima della pagina. Così si vede subito quante celle e quante righe
  entrano davvero sul foglio.
- **Tipo di invio alla stampante Braille**: «Diretto» manda il file Braille così com'è (e valgono le impostazioni della
  stampante); «Tramite il driver di Windows» è per le stampanti come le ViewPlus, che installano un font Braille con il
  loro driver: Eleu stampa con quel font, una pagina Braille per foglio, e decide lui il fronte e retro. La scelta resta
  per ogni stampante.
- **Scorciatoie da tastiera personalizzabili** (Strumenti, Cambia le scorciatoie da tastiera): ogni comando dei menu può
  avere un'altra scorciatoia, o nessuna. Eleu avvisa se la combinazione è già usata e permette di tornare a quelle di
  partenza, per un comando o per tutti. Chi non cambia niente trova le scorciatoie di sempre.
- **Segnalibri per studiare**: F5 porta al segnalibro successivo e Maiusc+F5 al precedente. Inserisci, Segnalibri,
  Riassunto dai segnalibri raccoglie in un documento nuovo i paragrafi in cui c'è un segnalibro, nell'ordine.

Nella versione 1.3.0: controllo prima della stampa Braille, copia in nero per chi non legge il Braille, foto dal
telefono, grafici in rilievo dalle tabelle, apertura dei file BRF, parola evidenziata durante la lettura.

## Da sapere

- **Stampanti Braille**: con l'invio diretto valgono le impostazioni della stampante. Righe per pagina, celle per riga,
  fronte e retro e tabella dei caratteri vanno impostati sulla stampante e devono corrispondere a quelli scelti in Eleu.
  Per la tabella va scelta quella nordamericana (US, North American, MIT): è lo standard dei file BRF. La pagina di
  prova aiuta a trovare i valori giusti.
- Con l'invio tramite il driver di Windows scegli la misura del foglio e stampa prima la pagina di prova: se qualcosa non torna,
  segnalalo indicando il modello e cosa si legge sulla pagina di prova.
- Per le foto dal telefono, la prima volta Windows chiede se Eleu può usare la rete: scegli Consenti. Se Windows
  considera pubblica la rete di casa, il telefono viene bloccato: la finestra di Eleu lo avvisa e spiega come
  impostare la rete come privata.
- L'acquisizione da scanner non è ancora stata provata con uno scanner vero: se il tuo scanner non viene trovato o
  qualcosa non va, segnalalo indicando il modello.
- Windows può dire che l'autore è sconosciuto, perché l'installer non ha ancora una firma digitale: come installarlo
  comunque è spiegato nel README, alla voce
  [Se Windows dice che l'autore è sconosciuto](https://github.com/Gabro777/Eleu#se-windows-dice-che-lautore-è-sconosciuto).
