# Eleu 1.5.1

Eleu è un programma di scrittura per Windows pensato per persone cieche e ipovedenti: tutto dalla tastiera, con lo
screen reader (NVDA, JAWS, Assistente vocale), con il Braille e con la voce. Gratuito e libero, con licenza GNU GPL 3.

## Installazione

Scarica `Eleu.msi` (oppure `Eleu-1.5.1.msi`, è lo stesso file) e aprilo: Eleu si installa per il tuo utente, senza
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

- **Stampanti Index Braille** (Basic, Everest, BrailleBox, FanFold): con l'invio diretto, in «Modello della stampante»
  si sceglie «Index Braille» (Eleu lo propone da solo se il nome della stampante lo fa capire). Eleu manda alla
  stampante i comandi che impostano celle per riga, righe per pagina, fronte e retro, copie e tabella, quindi valgono
  quelli scelti in Eleu e non serve più cambiarli sulla stampante; alla fine manda il segnale di fine documento, senza
  il quale la stampa poteva non partire o ripetersi.
- **Numero di copie** nella finestra della stampa Braille.
- **Apostrofi e virgolette**: con la tabella italiana, l'apostrofo, le virgolette e i trattini «tipografici» che Word
  inserisce da solo escono giusti in Braille (prima l'apostrofo diventava una cella vuota).
- **Stampa tramite il driver (ViewPlus) come BrailleBlaster**: misura del foglio e margini sinistro e superiore in
  millimetri, gli stessi valori che si usano in BrailleBlaster (per il modulo a trattore, per esempio, 292,1 × 304,8 mm
  con margini 25,4 e 12,7); il retro del foglio usa lo stesso margine sinistro; ogni cella è stampata nella sua colonna
  esatta, così il numero di pagina arriva alla fine della riga; il foglio su misura viene chiesto alla stampante nel
  modo standard di Windows.
- **Resoconto della stampa Braille** (Aiuto, Resoconto della stampa Braille sul desktop): se una stampa tramite driver
  esce spostata, mette sul desktop un file con quello che la stampante ha davvero usato, da mandare a chi ti aiuta.

Dalla versione 1.4.4:

- **Tabelle Braille**: italiano, inglese UEB integrale o contratto, francese integrale o abbreviato, tedesco integrale o
  contratto, spagnolo, portoghese; la tabella scelta vale per tutto Eleu.
- **Rientro dei paragrafi regolabile**, da 0 a 10 celle.

Dalla versione 1.4.3:

- **Stampa Braille tramite il driver (ViewPlus)**: misura del foglio scelta in Eleu e mandata insieme alla stampa, font
  giusto (Braille29, Braille36 o Braille36 US) scelto da solo con un avviso per gli altri, righe e margini come nei
  programmi Braille che già funzionano con queste stampanti.
- **Corretto l'aggiornamento alla 1.4.2**, che su alcuni computer lasciava Eleu senza .NET: chi ha ancora quel problema
  scarichi `Eleu.msi` da questa pagina e lo apra.

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

- **Stampanti Braille**: con l'invio diretto e il modello «Generica» valgono le impostazioni della stampante. Righe per
  pagina, celle per riga, fronte e retro e tabella dei caratteri vanno impostati sulla stampante e devono corrispondere a
  quelli scelti in Eleu (con le Index, invece, li imposta Eleu).
  Per la tabella va scelta quella nordamericana (US, North American, MIT): è lo standard dei file BRF. La pagina di
  prova aiuta a trovare i valori giusti.
- Con l'invio tramite il driver di Windows scegli la misura vera del foglio e stampa prima la pagina di prova: se qualcosa
  non torna, segnalalo indicando il modello e allegando il resoconto della stampa (menu Aiuto).
- Il supporto per le Index è nuovo e va ancora provato su più modelli: se qualcosa non va, segnalalo indicando il
  modello.
- Per le foto dal telefono, la prima volta Windows chiede se Eleu può usare la rete: scegli Consenti. Se Windows
  considera pubblica la rete di casa, il telefono viene bloccato: la finestra di Eleu lo avvisa e spiega come
  impostare la rete come privata.
- L'acquisizione da scanner non è ancora stata provata con uno scanner vero: se il tuo scanner non viene trovato o
  qualcosa non va, segnalalo indicando il modello.
- Windows può dire che l'autore è sconosciuto, perché l'installer non ha ancora una firma digitale: come installarlo
  comunque è spiegato nel README, alla voce
  [Se Windows dice che l'autore è sconosciuto](https://github.com/Gabro777/Eleu#se-windows-dice-che-lautore-è-sconosciuto).
