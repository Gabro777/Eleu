# Piano d'azione

## Principi di progetto (valgono per ogni fase)

1. **Tastiera prima di tutto.** Ogni comando ha una scorciatoia ed è nel menu (raggiungibile con Alt).
   Niente funzioni disponibili solo con il mouse.
2. **Screen reader compatibili.** Si usano controlli Win32 standard (il RichEdit di WinForms), che NVDA
   e JAWS leggono già bene. Ogni controllo ha nome accessibile (`AccessibleName`) e un ordine di tabulazione corretto.
3. **Feedback parlato.** Le azioni senza un effetto visibile ("File salvato", "3 sostituzioni eseguite")
   vengono annunciate allo screen reader attivo, tramite la libreria Tolk (NVDA/JAWS) con fallback su UI Automation.
4. **Poche opzioni, buoni default.** Meglio una finestra di dialogo in meno che un'impostazione in più.
5. **Familiare, ma nostro.** Comandi, menu e scorciatoie seguono le abitudini di chi usa già programmi
   di videoscrittura accessibili, così ci si orienta subito. Si studia come funzionano altri programmi
   usandoli normalmente e dalla loro documentazione pubblica; codice, testi, icone e altri materiali
   di altri non si copiano mai: etichette, messaggi e guida sono scritti per Eleu.
6. **Test con utenti veri.** Ogni fase si chiude con una prova pratica con NVDA (gratuito) e,
   se possibile, con una persona cieca.

## Stack tecnico

| Area | Scelta | Perché |
|---|---|---|
| UI | C# / WinForms su .NET 10 | Il RichTextBox è il controllo RichEdit nativo: il più compatibile con gli screen reader |
| DOCX | DocumentFormat.OpenXml | Libreria ufficiale Microsoft, gratuita |
| Annunci screen reader | Notifiche UI Automation (Tolk in futuro, se serve) | Incluse in Windows, supportate da NVDA, JAWS 2021+ e Assistente vocale |
| Sintesi vocale | System.Speech (SAPI 5) + voci OneCore di Windows | Voci italiane già presenti in Windows |
| Audio e MP3 | NAudio (riproduzione) + encoder MP3 di Windows (Media Foundation) | Nessuna libreria nativa da distribuire |
| Braille | liblouis (tabella italiana `it-it-comp6`) | Standard open source per la traduzione Braille |
| PDF | PdfPig | Estrazione del testo dai PDF |
| OCR | Windows.Media.Ocr | Integrato in Windows, supporta l'italiano |
| Installer | Inno Setup o MSIX | |

## Fasi

### Fase 0 — Fondamenta (settimana 1)
- [x] Repo, soluzione .NET, progetto di test
- [x] Finestra principale: un solo RichTextBox a tutto schermo, barra di stato con la posizione, menu da tastiera
- [x] Servizio `IAnnunciatore` per i messaggi parlati (notifiche UI Automation; Tolk solo se servirà)
- [x] GitHub Actions: build e test a ogni push
- [x] Prova pratica con NVDA (F2 legge la posizione; Copia/Taglia annunciano l'esito)

### Fase 1 — Editor base (settimane 2-4)
- [x] Nuovo / Apri / Salva / Salva con nome per TXT (codifica riconosciuta: UTF-8, UTF-16, ANSI) e RTF
- [x] DOCX: apertura e salvataggio con la formattazione base (grassetto, corsivo, sottolineato, dimensione, titoli, elenchi, allineamento)
- [x] DOCX con tabelle, immagini, note o intestazioni: avviso, e il salvataggio chiede un nome nuovo per non perderle
- [x] Avviso alla chiusura con modifiche non salvate
- [x] Trova (Ctrl+F), Trova successivo/precedente (F3 / Maiusc+F3), Sostituisci (Ctrl+H): annunciano la riga trovata
- [x] Formattazione: grassetto, corsivo, sottolineato, dimensione, Titolo 1-3, elenco puntato, allineamento
- [x] Comandi "dove sono": posizione (F2), formattazione (Maiusc+F2), conteggio parole (Ctrl+F2).
      "Leggi riga" non serve: lo fanno già gli screen reader
- [x] File recenti; copia di ripristino ogni minuto, proposta al riavvio dopo una chiusura anomala
      (al posto del salvataggio automatico, che sovrascriverebbe il file senza chiedere)
- [x] Zoom (Ctrl++ / Ctrl+- / Ctrl+0). Contrasto: nessun colore forzato, si seguono i temi a contrasto di Windows
- [x] Elenco delle scorciatoie (F1), generato dai menu
- [x] Prova con NVDA 2026.2: menu, lettura riga per riga, scrittura, F2/Maiusc+F2/Ctrl+F2, Trova,
      formattazione e domanda di salvataggio vengono letti correttamente.
      Corretto: Windows Forms, quando il RichTextBox riceve il focus, mandava agli screen reader tutto
      il testo del documento e NVDA lo leggeva (es. chiudendo Trova). Vedi `Editor/CampoTesto.cs`
- **Traguardo:** si scrive, si salva e si riapre una lettera usando solo tastiera e NVDA.

### Fase 2 — Sintesi vocale (settimane 5-6)
- [x] Lettura dal cursore o della selezione (F5), di tutto il documento (Ctrl+F5), pausa e ripresa (F5),
      stop (Maiusc+F5 o Esc). Fermandosi, il cursore va sulla parola raggiunta
- [x] Voci OneCore (Elsa, Cosimo) e SAPI 5 (anche quelle acquistate a parte); voce, velocità e volume
      nella finestra Lettura > Voce, con pulsante Prova
- [x] Audiolibro MP3 (File > Esporta audiolibro MP3): un file per capitolo, diviso sui titoli
      del livello più alto, con pause dopo i titoli e tra i paragrafi; encoder MP3 di Windows
- [x] Prova con NVDA 2026.2: lettura, pausa, stop con il cursore sulla parola raggiunta, finestra Voce
      ed esportazione dell'audiolibro (ogni passaggio viene annunciato). Durante la lettura di Eleu
      NVDA resta in silenzio, quindi le voci non si sovrappongono
- **Traguardo:** un documento di 50 pagine diventa un audiolibro in pochi clic.

### Fase 3 — Braille (settimane 7-9)
- [x] liblouis 3.39 (DLL ufficiale e solo le tabelle necessarie, LGPL) e traduzione in Braille italiano
      a 6 punti. Gli 8 punti servono ai display, non alla carta: per semplicità non c'è la scelta
- [x] Un solo comando, File > Braille (Ctrl+Maiusc+B): una finestra con destinazione (file BRF o
      stampante), celle per riga e righe per pagina, che dice subito quante pagine verranno
- [x] Impaginazione: a capo tra le parole, rientro dei paragrafi, riga vuota prima dei titoli
- [x] File BRF (ASCII Braille, salto pagina tra le pagine) e Visualizza > Anteprima Braille
- [x] Visualizza > Riga in Braille (Ctrl+Maiusc+R): striscia in basso con la riga dello schermo dove
      è il cursore, in celle Braille; scorre col cursore se la riga non ci sta e segue lo zoom.
      Spenta all'inizio, non prende il focus e non viene annunciata
- [ ] Stampa diretta (invio RAW alla stampante): scritta ma non provata, serve una stampante Braille vera
- **Traguardo:** stampa di una pagina di prova su una stampante Braille reale.

### Fase 4 — PDF e OCR (settimane 10-12)
- [x] File > Apri accetta anche PDF e immagini: Eleu capisce da solo se il PDF ha il testo (PdfPig)
      o è una scansione (pagine disegnate da Windows e lette con l'OCR italiano di Windows).
      Il risultato è un documento nuovo: il PDF non viene mai sovrascritto
- [x] Pulizia automatica: parole divise dal trattino ricucite, righe unite in paragrafi,
      numeri di pagina e intestazioni/piè di pagina ripetuti tolti
- [x] Correzione assistita: F8 / Maiusc+F8 vanno all'errore probabile e lo annunciano con un
      suggerimento, Ctrl+F8 lo corregge e passa al successivo. Errori: parole fuori dal dizionario
      italiano di Windows, cifre al posto di lettere ("c0me"), caratteri spuri ("ca|sa"), "II" per "Il".
      I suggerimenti preferiscono gli scambi tipici dell'OCR ("dlvano" → "divano")
- **Traguardo:** un capitolo scansionato diventa un testo pulito e leggibile.

### Fase 5 — Carattere e paragrafo
Una finestra per gruppo (Formato > Carattere..., Formato > Paragrafo...), scorciatoie per l'uso quotidiano.
- [x] Formato > Carattere (Ctrl+Maiusc+F, anche Ctrl+D): tipo, dimensione, grassetto, corsivo, sottolineato, barrato,
      apice e pedice, colore del testo e dello sfondo scelti per nome, con il contrasto W3C calcolato
      subito ("6:1, buono") e un'anteprima, spaziatura tra i caratteri. Applica solo ciò che si cambia
- [x] Formato > Paragrafo: allineamento, rientri in centimetri (sinistro, destro, prima riga rientrata o
      sporgente), interlinea, spazio prima e dopo in punti, elenco
- [ ] Tabulazioni personalizzate
- [x] Elenchi numerati (Ctrl+Maiusc+N), che ripartono da 1 a ogni elenco
- [x] Interlinea rapida: Ctrl+1 singola, Ctrl+5 una e mezza, Ctrl+2 doppia
- [x] Formato > Maiuscole/minuscole (Ctrl+Maiusc+A alterna, Ctrl++ e Ctrl+- maiuscolo e minuscolo): MAIUSCOLO, minuscolo, Iniziali Maiuscole,
      senza perdere la formattazione e con un solo passo di annullamento
- [ ] Mostra i caratteri speciali: il campo di testo di Windows non lo prevede, va studiato
- [x] Tutto conservato in DOCX e RTF (validato con lo schema di Word) e letto da Maiusc+F2,
      che dice solo ciò che si discosta dal normale
- [x] Strumenti > Dettagli del punto in cui sei (Alt+Invio) dice la formattazione del punto in cui si è

### Fase 6 — Pagina e stampa
- [x] File > Imposta pagina, in due schede: Margini (foglio, orientamento, margini, distanza di
      intestazione e piè di pagina dal bordo) e Intestazioni (testi con variabili {pagina}, {pagine},
      {titolo}, {data} inserite dal tasto Applicazioni, prima pagina esclusa); "Predefinito" per i nuovi documenti
- [x] Inserisci > Interruzioni di pagina: aggiungi (Ctrl+Invio), elimina, precedente e successiva,
      elenco per andarci o eliminarle
- [x] Modifica > Cerca > Vai a (Ctrl+Maiusc+G): pagina, riga o paragrafo; F2 e Maiusc+F2 ripetono il salto
- [x] File > Stampa (Ctrl+P): stampante e sue proprietà, da pagina a pagina, copie; anteprima di stampa
- [x] Intestazione e piè di pagina conservati in DOCX e RTF come campi di Word
- [x] F6 dice anche la pagina ("pagina 2 di 5"); la barra di stato mostra colonna, riga, carattere e paragrafo
- [ ] Linee orizzontali di separazione

### Per chi vede poco: barra dei pulsanti e righello
- [x] Colonna dei pulsanti a sinistra, divisi in gruppi con il titolo (Visualizza > Barra degli strumenti): pulsanti grandi con il nome scritto, colori di
      sistema (contrasto elevato), Grassetto e allineamento accesi secondo il testo, si ingrandiscono con lo zoom;
      da tastiera Ctrl+Tab, frecce, Invio, Esc per tornare al testo; nome e scorciatoia letti dallo screen reader
- [x] Righello (Visualizza > Righello) in centimetri con i rientri del paragrafo, spostabili con il mouse
- [x] Modifica > Copia braille: la selezione in Braille negli appunti
- [x] Copia di recupero per finestra: due Eleu aperti insieme non si scambiano il documento

### Fase 7 — Struttura del documento
- [x] Inserisci > Titoli: aggiungi (Ctrl+Alt+T), elimina, precedente e successivo, elenco dei titoli; 9 livelli
- [x] Inserisci > Titoli > Inserisci sommario: un paragrafo per titolo con il numero di pagina, rientrato
      per livello; l'elenco navigabile dei titoli è in Elenco dei titoli
- [x] Inserisci > Segnalibri: aggiungi (Ctrl+Maiusc+F5), elimina, precedente e successivo, elenco per andarci
      o eliminarli; seguono il testo mentre si scrive e sono conservati in RTF e DOCX
- [x] Riaprendo un documento si riparte dal punto in cui si era (ultimi 50 documenti)
- [x] Inserisci > Note a piè di pagina: aggiungi (Ctrl+Alt+F), modifica, elimina, precedente e successiva,
      elenco; il richiamo è il numero in apice, rinumerato da solo; Alt+Invio legge la nota; conservate in
      RTF e DOCX; in Braille e negli audiolibri vanno in un capitolo "Note" in fondo
- [ ] Note stampate a piè di pagina sulla carta (oggi non si stampano); note di fine documento
- [x] Navigazione per pagine, righe e paragrafi (Vai a, F2)
- [ ] Navigazione per tabelle, colori e stili
- [x] Più documenti aperti (vedi fase 11)

### Fase 8 — Tabelle
- [x] Inserisci > Tabelle: aggiungi (colonne e righe), elimina, inserisci riga sopra o sotto e colonna a sinistra
      o a destra, elimina riga o colonna, tabella precedente e successiva, elenco delle tabelle
- [x] Ctrl+Alt+frecce per passare da una cella all'altra; entrando in una cella Eleu dice riga, colonna e
      nome della colonna (la prima riga fa da intestazione); F6 dice anche la cella
- [x] Conservate in DOCX e RTF (tabelle semplici; celle unite e tabelle annidate vengono segnalate)
- [x] Larghezza della colonna in centimetri; ordinamento delle righe dalla A alla Z o al contrario (i numeri
      come numeri, la prima riga resta in cima)
- [x] Tabelle incollate da Word ed Excel restano tabelle; copiate da Eleu arrivano come tabelle
- [x] In Braille e negli audiolibri la tabella si legge riga per riga: "Nome: Anna; Età: 40"
- [ ] Celle unite; tabelle dentro le tabelle

### Fase 9 — Immagini
- [x] Inserisci > Immagini: aggiungi da file (PNG, JPEG, GIF, BMP, TIFF), modifica descrizione e larghezza
      (l'altezza segue), elimina, precedente e successiva, salva con nome, elenco delle immagini
- [x] Descrizione per lo screen reader chiesta all'inserimento; Eleu la dice arrivando sull'immagine e con
      Alt+Invio; in Braille e negli audiolibri diventa "[Immagine: descrizione]"
- [x] Conservate in DOCX (con il testo alternativo di Word) e in RTF (anche quelle scritte da Word)
- [ ] Immagini mobili (intorno al testo): oggi diventano in linea col testo

### Fase 10 — Strumenti di scrittura
- [x] Strumenti > Correzione > Controllo ortografico (Ctrl+Maiusc+F7): un errore alla volta con la frase, i
      suggerimenti e Cambia, Cambia tutti, Ignora, Ignora tutti, Aggiungi al dizionario; F7 e Maiusc+F7 per
      passare da un errore all'altro, Ctrl+F7 per correggere con il suggerimento
- [x] Controllo della punteggiatura e degli errori tipici: spazi prima e dopo la punteggiatura, spazi doppi,
      punteggiatura ripetuta, minuscola dopo il punto, parola ripetuta, apostrofo e spazio, "perchè", "pò",
      "qual'è", parentesi non chiuse; spazi e segni detti a parole ("spazio virgola")
- [x] Dizionario personale (parole da non segnalare)
- [ ] Errori sottolineati in rosso mentre si scrive: attivati, da verificare sul desktop principale
- [x] Ricerca con caratteri jolly (? * #); Sostituzioni multiple da un elenco che resta, in un colpo
- [x] Pulizia del testo: spazi doppi e ai bordi, righe vuote ripetute, parole divise dal trattino a fine riga,
      paragrafi spezzati a metà frase; anche solo nella selezione; le tabelle non si toccano
- [x] Ordinamento alfabetico dei paragrafi selezionati (numeri come numeri)
- [x] Statistiche complete (Ctrl+Maiusc+F6): lettere, cifre, spazi, segni, frasi, medie e indice Gulpease
- [x] Confronto con un altro documento: paragrafi cambiati (parola per parola), in più, in meno; Vai al punto

### Fase 11 — Formati, libri e voce
- [x] Più documenti aperti insieme: Nuovo e Apri lavorano nella finestra in cui si è; File > Apri in un'altra
      finestra e Finestre > Nuova finestra danno una finestra per documento (Alt+Tab), menu Finestre con l'elenco,
      finestra successiva e precedente (Ctrl+Alt+F6, Ctrl+Alt+Maiusc+F6), Chiudi documento (Ctrl+F4);
      un file già aperto porta alla sua finestra
- [x] HTML in apertura e salvataggio (pagina accessibile: titoli, elenchi, tabelle con intestazione, immagini
      col testo alternativo, note collegate)
- [x] EPUB 3 in apertura e salvataggio: capitoli, indice navigabile, note, immagini, metadati di accessibilità
- [x] File > Proprietà: titolo, autore, oggetto, parole chiave, conservati in DOCX, RTF, HTML ed EPUB;
      il titolo vale per {titolo} in intestazione e piè di pagina
- [x] Libro DAISY 2.02 (Voce > Crea un libro DAISY): testo e audio sincronizzati, una sezione per titolo
- [x] Voce > Pronunce: come dire parole e sigle, nella lettura e negli audiolibri

### Fase 12 — Braille avanzato
- [x] Numero di pagina Braille (in alto o in basso a destra), titolo del documento in cima a ogni pagina,
      margine sinistro per la rilegatura
- [x] Pagine a stampa: una riga di trattini con il numero dove comincia una nuova pagina del documento in nero
- [x] Sommario all'inizio con i numeri delle pagine Braille; intervallo di pagine da stampare o salvare
- [x] Tabelle (riga per riga, con il nome delle colonne), note (in fondo) e immagini (descrizione) in Braille
- [x] Scrittura Braille dalla tastiera a 6 tasti (F D S J K L, Strumenti > Scrittura Braille a 6 tasti,
      Ctrl+Maiusc+6): lettere, accenti, maiuscole e numeri, ogni carattere annunciato
- [ ] Poesie (versi e strofe) e sillabazione nel Braille
- [ ] Altre lingue e Braille a 8 punti (servono altre tabelle di liblouis)

### Fase 13 — Rilascio 1.0
- [x] Guida utente dentro Eleu (Aiuto > Guida di Eleu, F1): un documento da leggere con gli stessi tasti, per titoli
- [x] Icona; versione 1.0.0; Aiuto > Informazioni con le licenze usate
- [x] Installer .msi (WiX 5): per l'utente, senza permessi di amministratore, con .NET incluso; menu Start ed Eleu
      in "Apri con" per DOCX, RTF, TXT, HTML, EPUB e PDF senza cambiare i programmi predefiniti
- [x] L'installer si costruisce a ogni modifica su GitHub (build-installer.ps1); con un tag vX.Y.Z va in una release
- [ ] Test con 3-5 utenti ciechi e correzione dei problemi trovati
- [x] Prima release pubblicata (tag v1.0.0)

### Fase 14 — Versione 1.1: accessibilità e rilievo
- [x] Aiuto > Informazioni su Eleu con l'autore e il link alla pagina GitHub
- [x] Menu contestuale nel documento (tasto Applicazioni): correzioni della parola sbagliata, comandi di tabella,
      nota e immagine
- [x] Prova automatica di accessibilità di tutte le finestre (nome di ogni controllo, Esc chiude)
- [x] Segnali sonori su titoli, segnalibri, note, interruzioni di pagina e parole sbagliate; Strumenti > Opzioni
- [x] Selezione a blocchi (F8), salva selezione, simboli, cerca sul web, parole più usate
- [x] Immagini in rilievo: grafica a punti per la stampante Braille, disegno per la carta a microcapsule
- [ ] Prova con NVDA, JAWS e Assistente vocale da parte di utenti
- [ ] Prova con una stampante Braille e un fornetto per carta a microcapsule veri
- [x] Formato ODT (LibreOffice) e commenti di Word e LibreOffice
- [x] Aggiornamenti automatici dalle release di GitHub, con verifica SHA-256

## Da valutare dopo la 1.0
Disegno di grafica tattile da zero, acquisizione diretta da scanner o webcam,
mappe di tastiera per greco antico e altre lingue, scrittura da destra a sinistra, calcolatrice,
anagrammi e rime, interfaccia in altre lingue,
barre degli strumenti personalizzabili (scelta dei pulsanti), documenti protetti da password.
