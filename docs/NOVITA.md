# Eleu 1.2.0

Eleu è un programma di scrittura per Windows pensato per persone cieche e ipovedenti: tutto dalla tastiera, con lo
screen reader (NVDA, JAWS, Assistente vocale), con il Braille e con la voce. Gratuito e libero, con licenza GNU GPL 3.

## Installazione

Scarica `Eleu.msi` (oppure `Eleu-1.2.0.msi`, è lo stesso file) e aprilo: Eleu si installa per il tuo utente, senza
permessi di amministratore e senza dover installare altro. Se c'è già una versione precedente, viene aggiornata.
Nella guida (F1) ci sono tutti i comandi.

Requisiti: Windows 10 o 11 a 64 bit. Per il controllo ortografico in italiano serve la lingua italiana di Windows;
per la voce, almeno una voce di Windows.

## Cosa sa fare

- **Documenti**: apre e salva Word (DOCX), LibreOffice (ODT), RTF, testo, HTML ed EPUB; legge PDF, foto di pagine
  scritte e fogli di carta direttamente dallo scanner. Titoli, elenchi, tabelle, note a piè di pagina, segnalibri,
  commenti, immagini con la descrizione.
- **Scrivere e correggere**: controllo ortografico e della punteggiatura (F7), menu contestuale con le correzioni,
  trova e sostituisci, selezione a blocchi (F8), simboli, ricerca della parola sul web, statistiche.
- **Orientarsi**: Eleu dice sempre cosa ha fatto; segnali sonori brevi su titoli, segnalibri, note, commenti e parole
  sbagliate (oppure a voce); in tabella dice riga, colonna e nome della colonna.
- **Voce**: lettura ad alta voce con le voci di Windows, audiolibri MP3 e libri DAISY.
- **Braille**: file BRF e stampa Braille con numero di pagina, margine per la rilegatura, interlinea doppia, fronte e
  retro e righe libere a metà pagina per piegare il foglio; scrittura a sei tasti; immagini in rilievo.
- **Per chi vede poco**: testo ingrandibile, pulsanti grandi con il nome scritto, righello.
- **Aggiornamenti automatici**: una volta al giorno Eleu controlla se c'è una versione nuova e la installa.

## Novità di questa versione

- **Acquisire da scanner** (Ctrl+Alt+S, oppure File, Acquisisci da scanner): Eleu comanda lo scanner, riconosce il
  testo e lo apre come documento nuovo, pronto da correggere e da stampare in Braille. Si sceglie lo scanner e dove
  sono i fogli: sul vetro, una pagina alla volta (dopo ogni pagina Eleu chiede se ce n'è un'altra), oppure
  nell'alimentatore, tutte insieme. Funziona con gli scanner e le stampanti multifunzione che hanno il driver per
  Windows, senza programmi da installare.
- Premendo Grassetto, Corsivo o Sottolineato prima di scrivere, il pulsante nella barra si accende subito, non solo
  dopo il primo carattere. Lo stesso per la barra di stato.
- Il codice del programma ora è in inglese, come nella maggior parte dei progetti liberi: chi vuole contribuire lo
  legge più facilmente. Per chi usa Eleu non cambia nulla, e le impostazioni restano quelle di prima.

## Da sapere

- L'acquisizione da scanner è nuova e finora è stata provata senza uno scanner vero: se il tuo scanner non viene
  trovato o qualcosa non va, segnalalo indicando il modello.

- La stampa Braille diretta e la carta a microcapsule sono da provare con stampanti diverse: se qualcosa non torna,
  segnalalo indicando il modello della stampante.
- Windows può dire che l'autore è sconosciuto, perché l'installer non ha ancora una firma digitale: come installarlo
  comunque è spiegato nel README, alla voce
  [Se Windows dice che l'autore è sconosciuto](https://github.com/Gabro777/Eleu#se-windows-dice-che-lautore-è-sconosciuto).
