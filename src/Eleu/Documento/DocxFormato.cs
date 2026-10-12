using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;

namespace Eleu.Documento;

/// <summary>
/// Lettura e scrittura dei documenti Word (.docx).
/// Si conserva la formattazione semplice: grassetto, corsivo, sottolineato, dimensione,
/// allineamento, titoli ed elenchi. Tabelle e immagini diventano testo semplice o vengono ignorate.
/// </summary>
public static partial class DocxFormato
{
    /// <summary>Separatore delle righe nel testo di una nota.</summary>
    private const string ACapo = "\n";

    private const int IdElencoPuntato = 1;
    private const int IdElencoNumerato = 2;

    public static ContenutoDocumento Leggi(Stream flusso)
    {
        using var documento = WordprocessingDocument.Open(flusso, false);
        var principale = documento.MainDocumentPart;
        var corpo = principale?.Document?.Body;
        var contenuto = new ContenutoDocumento();
        if (principale is null || corpo is null)
            return contenuto;

        // Le tabelle semplici sì; celle unite e tabelle dentro le tabelle no.
        if (corpo.Descendants<GridSpan>().Any(g => g.Val?.Value > 1) || corpo.Descendants<VerticalMerge>().Any()
            || corpo.Descendants<Table>().Any(t => t.Ancestors<Table>().Any()))
            contenuto.ElementiNonConservati.Add("celle unite o tabelle dentro le tabelle");
        var numeriTabelle = new Dictionary<Table, int>();
        // Le immagini di oggi (Drawing) sì; quelle nel formato vecchio (VML) e le forme senza immagine no.
        if (corpo.Descendants<Picture>().Any())
            contenuto.ElementiNonConservati.Add("immagini nel formato vecchio di Word");
        bool disegniPersi = false;
        if (principale.EndnotesPart?.Endnotes?.Elements<Endnote>().Any(n => n.Id?.Value > 0) == true)
            contenuto.ElementiNonConservati.Add("note di chiusura");
        var testiNote = principale.FootnotesPart?.Footnotes?.Elements<Footnote>()
            .Where(n => n.Id?.Value > 0)
            .ToDictionary(n => n.Id!.Value, n => string.Join(ACapo, n.Elements<Paragraph>().Select(TestoParagrafo)).Trim()) ?? [];
        // Le modifiche tracciate si leggono già accettate (il testo cancellato non c'è, quello aggiunto sì).
        if (corpo.Descendants<InsertedRun>().Any() || corpo.Descendants<DeletedRun>().Any() || corpo.Descendants<MoveFromRun>().Any())
            contenuto.ElementiNonConservati.Add("revisioni (modifiche tracciate), che Eleu mostra già accettate");
        var datiCommenti = principale.WordprocessingCommentsPart?.Comments?.Elements<Comment>()
            .Where(c => c.Id?.Value is not null)
            .GroupBy(c => c.Id!.Value!).ToDictionary(g => g.Key, g => g.First()) ?? [];
        var indiciCommenti = new Dictionary<string, int>();
        var commentiChiusi = new HashSet<string>();
        contenuto.Pagina = LeggiPagina(principale, corpo);
        var proprieta = documento.PackageProperties;
        contenuto.Metadati = new Metadati(proprieta.Title ?? "", proprieta.Creator ?? "", proprieta.Subject ?? "", proprieta.Keywords ?? "", proprieta.Created);

        var stili = principale.StyleDefinitionsPart?.Styles;
        var numerazione = principale.NumberingDefinitionsPart?.Numbering;
        bool saltoInSospeso = false;

        foreach (var p in corpo.Descendants<Paragraph>())
        {
            var cella = CellaDi(p, numeriTabelle);
            int primoParagrafo = contenuto.Paragrafi.Count;
            LeggiParagrafo(p);
            // Anche i paragrafi nati da un a capo dentro la cella restano nella cella.
            for (int i = primoParagrafo; i < contenuto.Paragrafi.Count; i++)
                contenuto.Paragrafi[i].Cella = cella;
        }
        if (disegniPersi)
            contenuto.ElementiNonConservati.Add("immagini o disegni che Eleu non sa leggere");
        return contenuto;

        // Il commento con quel numero di Word diventa il prossimo dell'elenco; null se manca o se sono troppi.
        int? NuovoCommento(string? id)
        {
            if (id is null || indiciCommenti.ContainsKey(id) || contenuto.Commenti.Count >= Commenti.Massimo || !datiCommenti.TryGetValue(id, out var dati))
                return null;
            contenuto.Commenti.Add(new Commento(
                string.Join(ACapo, dati.Elements<Paragraph>().Select(TestoParagrafo)).Trim(),
                dati.Author?.Value ?? "",
                dati.Date?.Value));
            indiciCommenti[id] = contenuto.Commenti.Count - 1;
            return contenuto.Commenti.Count - 1;
        }

        void LeggiParagrafo(Paragraph p)
        {
            var proprieta = p.ParagraphProperties;
            var paragrafo = new Paragrafo
            {
                LivelloTitolo = LivelloTitolo(proprieta, stili),
                Allineamento = LeggiAllineamento(proprieta?.Justification),
                SaltoPagina = saltoInSospeso || Attivo(proprieta?.PageBreakBefore),
            };
            saltoInSospeso = false;
            LeggiSpaziERientri(proprieta, paragrafo);

            switch (LeggiElenco(proprieta?.NumberingProperties, numerazione))
            {
                case TipoElenco.Puntato:
                    paragrafo.Elenco = true;
                    break;
                case TipoElenco.Numerato:
                    paragrafo.Numerato = true;
                    break;
            }
            if (paragrafo.Elenco || paragrafo.Numerato)
            {
                // Negli elenchi il rientro lo decide la numerazione, non l'utente.
                paragrafo.RientroSinistro = 0;
                paragrafo.RientroPrimaRiga = 0;
            }

            // Solo le run (e i segnalibri) di questo paragrafo, non quelle delle caselle di testo annidate.
            foreach (var parte in p.Descendants().Where(e => e is Run or BookmarkStart or CommentRangeStart or CommentRangeEnd && e.Ancestors<Paragraph>().First() == p))
            {
                if (parte is BookmarkStart segnalibro)
                {
                    if (segnalibro.Name?.Value is string nome && !Segnalibri.Nascosto(nome) && contenuto.Segnalibri.Count < Segnalibri.Massimo)
                    {
                        contenuto.Segnalibri.Add(Segnalibri.NomeDaWord(nome));
                        paragrafo.Aggiungi(new Segmento(Segnalibri.Segnaposto(contenuto.Segnalibri.Count - 1).ToString()));
                    }
                    continue;
                }
                if (parte is CommentRangeStart inizioCommento)
                {
                    if (NuovoCommento(inizioCommento.Id?.Value) is int indice)
                        paragrafo.Aggiungi(new Segmento(Commenti.Inizio(indice).ToString()));
                    continue;
                }
                if (parte is CommentRangeEnd fineCommento)
                {
                    if (fineCommento.Id?.Value is string id && indiciCommenti.TryGetValue(id, out int indice) && commentiChiusi.Add(id))
                        paragrafo.Aggiungi(new Segmento(Commenti.Fine(indice).ToString()));
                    continue;
                }
                var run = (Run)parte;
                var formato = LeggiFormato(run.RunProperties);
                foreach (var elemento in run.ChildElements)
                {
                    switch (elemento)
                    {
                        case Text t:
                            paragrafo.Aggiungi(formato with { Testo = t.Text });
                            break;
                        case TabChar:
                            paragrafo.Aggiungi(formato with { Testo = "\t" });
                            break;
                        case Drawing disegno:
                            if (contenuto.Immagini.Count < Immagini.Massimo && LeggiDisegno(disegno, principale) is { } immagine)
                            {
                                contenuto.Immagini.Add(immagine);
                                paragrafo.Aggiungi(new Segmento(Immagini.Segnaposto(contenuto.Immagini.Count - 1).ToString()));
                            }
                            else
                            {
                                disegniPersi = true;
                            }
                            break;
                        case FootnoteReference richiamo when contenuto.Note.Count < Note.Massimo:
                            contenuto.Note.Add(richiamo.Id?.Value is long id && testiNote.TryGetValue(id, out var testoNota) ? testoNota : "");
                            paragrafo.Aggiungi(new Segmento(Note.Segnaposto(contenuto.Note.Count - 1).ToString()));
                            break;
                        case CommentReference richiamoCommento when richiamoCommento.Id?.Value is string idRichiamo && !indiciCommenti.ContainsKey(idRichiamo):
                            // Un commento su un punto, senza testo commentato: inizio e fine nello stesso posto.
                            if (NuovoCommento(idRichiamo) is int puntuale)
                            {
                                commentiChiusi.Add(idRichiamo);
                                paragrafo.Aggiungi(new Segmento(Commenti.Inizio(puntuale).ToString() + Commenti.Fine(puntuale)));
                            }
                            break;
                        case NoBreakHyphen:
                            paragrafo.Aggiungi(formato with { Testo = "-" });
                            break;
                        case Break interruzione when interruzione.Type?.Value == BreakValues.Page:
                            // Interruzione di pagina: ciò che segue inizia su una pagina nuova.
                            if (paragrafo.Segmenti.Count == 0)
                            {
                                paragrafo.SaltoPagina = true;
                            }
                            else
                            {
                                contenuto.Paragrafi.Add(paragrafo);
                                paragrafo = paragrafo.CopiaProprieta();
                                paragrafo.SaltoPagina = true;
                            }
                            break;
                        case Break or CarriageReturn:
                            contenuto.Paragrafi.Add(paragrafo);
                            paragrafo = paragrafo.CopiaProprieta();
                            break;
                    }
                }
            }
            // Un paragrafo che contiene solo l'interruzione di pagina la passa al paragrafo seguente.
            if (paragrafo.Segmenti.Count == 0 && paragrafo.SaltoPagina)
                saltoInSospeso = true;
            else
                contenuto.Paragrafi.Add(paragrafo);
        }
    }

    /// <summary>
    /// La cella in cui sta il paragrafo (della tabella più esterna, se sono una dentro l'altra), o null.
    /// Le tabelle sono numerate nell'ordine in cui si incontrano.
    /// </summary>
    private static PosizioneCella? CellaDi(Paragraph p, Dictionary<Table, int> numeriTabelle)
    {
        if (p.Ancestors<TableCell>().LastOrDefault() is not { } cella || cella.Parent is not TableRow riga || riga.Parent is not Table tabella)
            return null;
        if (!numeriTabelle.TryGetValue(tabella, out int numero))
            numeriTabelle[tabella] = numero = numeriTabelle.Count;
        int indiceRiga = tabella.Elements<TableRow>().TakeWhile(r => r != riga).Count();
        int colonna = riga.Elements<TableCell>().TakeWhile(c => c != cella).Count();
        float larghezza = cella.TableCellProperties?.TableCellWidth is { Type.Value: var tipo, Width.Value: string w }
            && tipo == TableWidthUnitValues.Dxa && int.TryParse(w, out int twip) ? twip / 20f : 0;
        if (larghezza == 0 && tabella.GetFirstChild<TableGrid>()?.Elements<GridColumn>().ElementAtOrDefault(colonna)?.Width?.Value is string g
            && int.TryParse(g, out int twipGriglia))
            larghezza = twipGriglia / 20f;
        bool intestazione = riga.TableRowProperties?.GetFirstChild<TableHeader>() is not null;
        return new PosizioneCella(numero, indiceRiga, colonna, larghezza, intestazione);
    }

    private static bool Attivo(OnOffType? valore) => valore is not null && (valore.Val is null || valore.Val.Value);

    private static ImpostazioniPagina LeggiPagina(MainDocumentPart principale, Body corpo)
    {
        var predefinite = new ImpostazioniPagina();
        var sezione = corpo.Elements<SectionProperties>().LastOrDefault();
        var foglio = sezione?.GetFirstChild<PageSize>();
        var margini = sezione?.GetFirstChild<PageMargin>();
        bool orizzontale = foglio?.Orient?.Value == PageOrientationValues.Landscape;
        float larghezza = foglio?.Width?.Value / 20f ?? (orizzontale ? predefinite.Altezza : predefinite.Larghezza);
        float altezza = foglio?.Height?.Value / 20f ?? (orizzontale ? predefinite.Larghezza : predefinite.Altezza);

        string Testo(OpenXmlCompositeElement? radice) =>
            radice is null ? "" : string.Join(" ", radice.Elements<Paragraph>().Select(TestoConVariabili).Where(t => t.Length > 0));

        var intestazione = sezione?.Elements<HeaderReference>().FirstOrDefault(r => r.Type is null || r.Type.Value == HeaderFooterValues.Default);
        var piede = sezione?.Elements<FooterReference>().FirstOrDefault(r => r.Type is null || r.Type.Value == HeaderFooterValues.Default);

        return new ImpostazioniPagina(
            orizzontale ? altezza : larghezza,
            orizzontale ? larghezza : altezza,
            orizzontale,
            margini?.Left?.Value / 20f ?? predefinite.MargineSinistro,
            margini?.Right?.Value / 20f ?? predefinite.MargineDestro,
            margini?.Top?.Value / 20f ?? predefinite.MargineSopra,
            margini?.Bottom?.Value / 20f ?? predefinite.MargineSotto,
            margini?.Header?.Value / 20f ?? predefinite.DistanzaIntestazione,
            margini?.Footer?.Value / 20f ?? predefinite.DistanzaPiede,
            intestazione?.Id?.Value is string idI && principale.GetPartById(idI) is HeaderPart parteI ? Testo(parteI.Header) : "",
            piede?.Id?.Value is string idP && principale.GetPartById(idP) is FooterPart parteP ? Testo(parteP.Footer) : "",
            sezione?.GetFirstChild<TitlePage>() is TitlePage primaPagina && Attivo(primaPagina));
    }

    /// <summary>Il testo di un paragrafo di intestazione, con i campi di Word (PAGE, NUMPAGES, ...) come variabili.</summary>
    private static string TestoConVariabili(Paragraph paragrafo)
    {
        var testo = new StringBuilder();
        bool dentroRisultato = false; // tra "separate" ed "end" di un campo c'è il risultato calcolato: non è testo
        foreach (var elemento in paragrafo.Descendants())
        {
            switch (elemento)
            {
                case SimpleField campo:
                    testo.Append(ImpostazioniPagina.SegnapostoDelCampo(campo.Instruction?.Value ?? ""));
                    break;
                case FieldCode codice:
                    testo.Append(ImpostazioniPagina.SegnapostoDelCampo(codice.Text));
                    break;
                case FieldChar carattere when carattere.FieldCharType?.Value == FieldCharValues.Separate:
                    dentroRisultato = true;
                    break;
                case FieldChar carattere when carattere.FieldCharType?.Value == FieldCharValues.End:
                    dentroRisultato = false;
                    break;
                case Text t when !dentroRisultato && !t.Ancestors<SimpleField>().Any():
                    testo.Append(t.Text);
                    break;
            }
        }
        return testo.ToString().Trim();
    }

    private static SectionProperties CreaSezione(MainDocumentPart principale, ImpostazioniPagina pagina)
    {
        var sezione = new SectionProperties();

        // Intestazione e piè di pagina: le variabili diventano campi di Word, che si aggiornano da soli.
        Paragraph Bordo(string modello)
        {
            var p = new Paragraph(new ParagraphProperties(new Justification { Val = JustificationValues.Center }));
            foreach (var (testo, campo) in ImpostazioniPagina.Pezzi(modello))
            {
                var run = new Run(new Text(campo is null ? testo : "1") { Space = SpaceProcessingModeValues.Preserve });
                p.Append(campo is null ? run : new SimpleField(run) { Instruction = $" {campo} " });
            }
            return p;
        }

        if (pagina.Intestazione.Length > 0)
        {
            var parte = principale.AddNewPart<HeaderPart>();
            parte.Header = new Header(Bordo(pagina.Intestazione));
            sezione.Append(new HeaderReference { Type = HeaderFooterValues.Default, Id = principale.GetIdOfPart(parte) });
        }
        if (pagina.EscludiPrimaPagina)
        {
            var parte = principale.AddNewPart<HeaderPart>();
            parte.Header = new Header(new Paragraph());
            sezione.Append(new HeaderReference { Type = HeaderFooterValues.First, Id = principale.GetIdOfPart(parte) });
        }
        if (pagina.PiePagina.Length > 0)
        {
            var parte = principale.AddNewPart<FooterPart>();
            parte.Footer = new Footer(Bordo(pagina.PiePagina));
            sezione.Append(new FooterReference { Type = HeaderFooterValues.Default, Id = principale.GetIdOfPart(parte) });
        }
        if (pagina.EscludiPrimaPagina)
        {
            var parte = principale.AddNewPart<FooterPart>();
            parte.Footer = new Footer(new Paragraph());
            sezione.Append(new FooterReference { Type = HeaderFooterValues.First, Id = principale.GetIdOfPart(parte) });
        }

        var (larghezza, altezza) = pagina.Effettive;
        var foglio = new PageSize { Width = (uint)RtfScrittore.Twip(larghezza), Height = (uint)RtfScrittore.Twip(altezza) };
        if (pagina.Orizzontale)
            foglio.Orient = PageOrientationValues.Landscape;
        sezione.Append(foglio);
        sezione.Append(new PageMargin
        {
            Left = (uint)RtfScrittore.Twip(pagina.MargineSinistro),
            Right = (uint)RtfScrittore.Twip(pagina.MargineDestro),
            Top = RtfScrittore.Twip(pagina.MargineSopra),
            Bottom = RtfScrittore.Twip(pagina.MargineSotto),
            Header = (uint)RtfScrittore.Twip(pagina.DistanzaIntestazione),
            Footer = (uint)RtfScrittore.Twip(pagina.DistanzaPiede),
            Gutter = 0,
        });
        if (pagina.EscludiPrimaPagina)
            sezione.Append(new TitlePage());
        return sezione;
    }

    public static void Scrivi(ContenutoDocumento contenuto, Stream flusso, string carattere = "Calibri", float dimensioneBase = ContenutoDocumento.DimensioneBase)
    {
        using var documento = WordprocessingDocument.Create(flusso, WordprocessingDocumentType.Document);
        var principale = documento.AddMainDocumentPart();
        var corpo = new Body();
        var metadati = contenuto.Metadati;
        if (metadati.Titolo.Length > 0)
            documento.PackageProperties.Title = metadati.Titolo;
        if (metadati.Autore.Length > 0)
            documento.PackageProperties.Creator = metadati.Autore;
        if (metadati.Oggetto.Length > 0)
            documento.PackageProperties.Subject = metadati.Oggetto;
        if (metadati.ParoleChiave.Length > 0)
            documento.PackageProperties.Keywords = metadati.ParoleChiave;
        documento.PackageProperties.Created = metadati.Creato ?? DateTime.Now;
        documento.PackageProperties.Modified = DateTime.Now;
        principale.Document = new Document(corpo);

        var parteStili = principale.AddNewPart<StyleDefinitionsPart>();
        parteStili.Styles = CreaStili(carattere, dimensioneBase);

        // Elenchi: un'istanza per i puntati, e una per ogni elenco numerato così che ognuno riparta da 1.
        var numerazione = new Numbering(CreaDefinizioneElenco(IdElencoPuntato, NumberFormatValues.Bullet, "•"),
            CreaDefinizioneElenco(IdElencoNumerato, NumberFormatValues.Decimal, "%1."));
        numerazione.Append(new NumberingInstance(new AbstractNumId { Val = IdElencoPuntato }) { NumberID = IdElencoPuntato });
        int istanzaNumerata = 0;
        bool precedenteNumerato = false;
        var nomiWord = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var commenti = CommentiCompleti(contenuto);
        Table? tabella = null;
        TableRow? riga = null;
        TableCell? cellaWord = null;
        PosizioneCella? ultimaCella = null;

        foreach (var paragrafo in contenuto.Paragrafi)
        {
            if (paragrafo.Numerato && !precedenteNumerato)
            {
                istanzaNumerata = IdElencoNumerato + 1 + numerazione.Elements<NumberingInstance>().Count();
                numerazione.Append(new NumberingInstance(
                    new AbstractNumId { Val = IdElencoNumerato },
                    new LevelOverride(new StartOverrideNumberingValue { Val = 1 }) { LevelIndex = 0 })
                { NumberID = istanzaNumerata });
            }
            precedenteNumerato = paragrafo.Numerato;

            var proprieta = new ParagraphProperties();
            if (paragrafo.LivelloTitolo > 0)
                proprieta.Append(new ParagraphStyleId { Val = $"Heading{paragrafo.LivelloTitolo}" });
            if (paragrafo.SaltoPagina)
                proprieta.Append(new PageBreakBefore());
            if (paragrafo.Elenco || paragrafo.Numerato)
                proprieta.Append(new NumberingProperties(
                    new NumberingLevelReference { Val = 0 },
                    new NumberingId { Val = paragrafo.Numerato ? istanzaNumerata : IdElencoPuntato }));
            if (paragrafo.SpazioPrima != 0 || paragrafo.SpazioDopo != 0 || paragrafo.Interlinea != 1f)
            {
                var spazi = new SpacingBetweenLines();
                if (paragrafo.SpazioPrima != 0)
                    spazi.Before = Twip(paragrafo.SpazioPrima);
                if (paragrafo.SpazioDopo != 0)
                    spazi.After = Twip(paragrafo.SpazioDopo);
                if (paragrafo.Interlinea != 1f)
                {
                    spazi.Line = ((int)Math.Round(paragrafo.Interlinea * 240)).ToString(CultureInfo.InvariantCulture);
                    spazi.LineRule = LineSpacingRuleValues.Auto;
                }
                proprieta.Append(spazi);
            }
            if (paragrafo.RientroSinistro != 0 || paragrafo.RientroDestro != 0 || paragrafo.RientroPrimaRiga != 0)
            {
                var rientri = new Indentation();
                if (paragrafo.RientroSinistro != 0)
                    rientri.Left = Twip(paragrafo.RientroSinistro);
                if (paragrafo.RientroDestro != 0)
                    rientri.Right = Twip(paragrafo.RientroDestro);
                if (paragrafo.RientroPrimaRiga > 0)
                    rientri.FirstLine = Twip(paragrafo.RientroPrimaRiga);
                else if (paragrafo.RientroPrimaRiga < 0)
                    rientri.Hanging = Twip(-paragrafo.RientroPrimaRiga);
                proprieta.Append(rientri);
            }
            if (paragrafo.Allineamento != Allineamento.Sinistra)
                proprieta.Append(new Justification
                {
                    Val = paragrafo.Allineamento switch
                    {
                        Allineamento.Centro => JustificationValues.Center,
                        Allineamento.Destra => JustificationValues.Right,
                        _ => JustificationValues.Both,
                    },
                });

            var p = new Paragraph();
            if (proprieta.HasChildren)
                p.Append(proprieta);
            foreach (var segmento in paragrafo.Segmenti)
                AggiungiRun(p, segmento, contenuto, nomiWord, principale, commenti);

            // In tabella il paragrafo va nella sua cella: tabella, riga e cella si aprono quando cambiano.
            if (paragrafo.Cella is not { } cella)
            {
                corpo.Append(p);
                tabella = null;
                continue;
            }
            if (tabella is null || cella.Tabella != ultimaCella!.Tabella)
            {
                tabella = CreaTabella(contenuto, cella.Tabella);
                corpo.Append(tabella);
                riga = null;
            }
            if (riga is null || !cella.StessaRiga(ultimaCella))
            {
                riga = new TableRow();
                if (cella.Intestazione)
                    riga.Append(new TableRowProperties(new TableHeader()));
                tabella.Append(riga);
                cellaWord = null;
            }
            if (cellaWord is null || !cella.StessaCella(ultimaCella))
            {
                cellaWord = new TableCell(new TableCellProperties(new TableCellWidth
                {
                    Type = TableWidthUnitValues.Dxa,
                    Width = (cella.Larghezza > 0 ? Twip(cella.Larghezza) : LarghezzaColonnaPredefinita),
                }));
                riga.Append(cellaWord);
            }
            cellaWord.Append(p);
            ultimaCella = cella;
        }

        // Word vuole un paragrafo dopo l'ultima tabella del documento.
        if (corpo.LastChild is Table)
            corpo.Append(new Paragraph());

        if (contenuto.Note.Count > 0)
            principale.AddNewPart<FootnotesPart>().Footnotes = CreaNote(contenuto.Note);

        if (commenti.Count > 0)
            principale.AddNewPart<WordprocessingCommentsPart>().Comments = CreaCommenti(contenuto.Commenti, commenti);

        if (contenuto.Paragrafi.Any(p => p.Elenco || p.Numerato))
            principale.AddNewPart<NumberingDefinitionsPart>().Numbering = numerazione;

        // Foglio, margini, intestazione e piè di pagina: le impostazioni di sezione vanno in fondo al corpo.
        corpo.Append(CreaSezione(principale, contenuto.Pagina));
    }

    private static string Twip(float punti) => ((int)Math.Round(punti * 20)).ToString(CultureInfo.InvariantCulture);

    private static string Esadecimale(int colore) => (colore & 0xFFFFFF).ToString("X6", CultureInfo.InvariantCulture);

    /// <summary>Il segmento come run, con segnalibri, richiami di nota e immagini al posto dei loro segnaposto.</summary>
    private static void AggiungiRun(Paragraph p, Segmento segmento, ContenutoDocumento contenuto, HashSet<string> nomiWord, MainDocumentPart principale,
        HashSet<int> commenti)
    {
        var segnalibri = contenuto.Segnalibri;
        int inizio = 0;
        for (int i = 0; i <= segmento.Testo.Length; i++)
        {
            char c = i < segmento.Testo.Length ? segmento.Testo[i] : '\0';
            int indice = i < segmento.Testo.Length ? Segnalibri.Indice(c) : -1;
            int nota = i < segmento.Testo.Length ? Note.Indice(c) : -1;
            int immagine = i < segmento.Testo.Length ? Immagini.Indice(c) : -1;
            int inizioCommento = i < segmento.Testo.Length ? Commenti.IndiceInizio(c) : -1;
            int fineCommento = i < segmento.Testo.Length ? Commenti.IndiceFine(c) : -1;
            if (i < segmento.Testo.Length && indice < 0 && nota < 0 && immagine < 0 && inizioCommento < 0 && fineCommento < 0)
                continue;
            if (i > inizio)
                p.Append(CreaRun(segmento with { Testo = segmento.Testo[inizio..i] }));
            if (indice >= 0 && indice < segnalibri.Count)
            {
                var id = (nomiWord.Count + 1).ToString(CultureInfo.InvariantCulture);
                p.Append(new BookmarkStart { Id = id, Name = Segnalibri.NomeWord(segnalibri[indice], nomiWord) });
                p.Append(new BookmarkEnd { Id = id });
            }
            if (nota >= 0 && nota < contenuto.Note.Count)
            {
                // Richiamo in apice; il testo della nota è nella parte note, con lo stesso numero.
                p.Append(new Run(
                    new RunProperties(new VerticalTextAlignment { Val = VerticalPositionValues.Superscript }),
                    new FootnoteReference { Id = nota + 1 }));
            }
            if (immagine >= 0 && immagine < contenuto.Immagini.Count)
                p.Append(new Run(CreaDisegno(principale, contenuto.Immagini[immagine], immagine + 1)));
            if (commenti.Contains(inizioCommento))
                p.Append(new CommentRangeStart { Id = inizioCommento.ToString(CultureInfo.InvariantCulture) });
            if (commenti.Contains(fineCommento))
            {
                var id = fineCommento.ToString(CultureInfo.InvariantCulture);
                p.Append(new CommentRangeEnd { Id = id });
                p.Append(new Run(new CommentReference { Id = id }));
            }
            inizio = i + 1;
        }
    }

    /// <summary>Un'immagine in linea col testo, con la descrizione come testo alternativo (quello che legge lo screen reader in Word).</summary>
    private static Drawing CreaDisegno(MainDocumentPart principale, Immagine immagine, int numero)
    {
        var parte = principale.AddImagePart(immagine.Formato == "jpeg" ? ImagePartType.Jpeg : ImagePartType.Png);
        using (var dati = new MemoryStream(immagine.Dati))
            parte.FeedData(dati);
        string relazione = principale.GetIdOfPart(parte);
        long cx = (long)Math.Round(immagine.Larghezza * 12700), cy = (long)Math.Round(immagine.Altezza * 12700); // EMU
        var nome = $"Immagine {numero}";
        return new Drawing(new DW.Inline(
            new DW.Extent { Cx = cx, Cy = cy },
            new DW.EffectExtent { LeftEdge = 0, TopEdge = 0, RightEdge = 0, BottomEdge = 0 },
            new DW.DocProperties { Id = (uint)numero, Name = nome, Description = immagine.Descrizione },
            new DW.NonVisualGraphicFrameDrawingProperties(new A.GraphicFrameLocks { NoChangeAspect = true }),
            new A.Graphic(new A.GraphicData(
                new PIC.Picture(
                    new PIC.NonVisualPictureProperties(
                        new PIC.NonVisualDrawingProperties { Id = 0U, Name = nome, Description = immagine.Descrizione },
                        new PIC.NonVisualPictureDrawingProperties()),
                    new PIC.BlipFill(new A.Blip { Embed = relazione }, new A.Stretch(new A.FillRectangle())),
                    new PIC.ShapeProperties(
                        new A.Transform2D(new A.Offset { X = 0, Y = 0 }, new A.Extents { Cx = cx, Cy = cy }),
                        new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle })))
            { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" }))
        { DistanceFromTop = 0U, DistanceFromBottom = 0U, DistanceFromLeft = 0U, DistanceFromRight = 0U });
    }

    /// <summary>
    /// Un disegno di Word come immagine (in linea o mobile: in Eleu diventa in linea), con la descrizione;
    /// null se non contiene un'immagine che si sa leggere.
    /// </summary>
    private static Immagine? LeggiDisegno(Drawing disegno, MainDocumentPart principale)
    {
        if (disegno.Descendants<A.Blip>().FirstOrDefault()?.Embed?.Value is not string relazione
            || principale.GetPartById(relazione) is not ImagePart parte)
            return null;
        using var flusso = parte.GetStream();
        using var copia = new MemoryStream();
        flusso.CopyTo(copia);
        var estensione = disegno.Descendants<DW.Extent>().FirstOrDefault();
        var proprieta = disegno.Descendants<DW.DocProperties>().FirstOrDefault();
        var descrizione = proprieta?.Description?.Value ?? proprieta?.Title?.Value ?? "";
        try
        {
            var immagine = Immagini.DaDati(copia.ToArray(), descrizione.Trim());
            return estensione?.Cx?.Value is long cx && estensione.Cy?.Value is long cy && cx > 0 && cy > 0
                ? immagine with { Larghezza = cx / 12700f, Altezza = cy / 12700f }
                : immagine;
        }
        catch (Exception e) when (e is ArgumentException or System.Runtime.InteropServices.ExternalException or OutOfMemoryException)
        {
            return null;
        }
    }

    /// <summary>Larghezza di una colonna senza larghezza scelta: 4 cm, in twip (come nell'RTF).</summary>
    private const string LarghezzaColonnaPredefinita = "2268";

    /// <summary>Una tabella vuota con bordi sottili e la griglia delle colonne; righe e celle si aggiungono dopo.</summary>
    private static Table CreaTabella(ContenutoDocumento contenuto, int numero)
    {
        BorderType Bordo<T>() where T : BorderType, new() => new T { Val = BorderValues.Single, Size = 4, Space = 0, Color = "auto" };
        var colonne = contenuto.Paragrafi.Select(p => p.Cella).OfType<PosizioneCella>().Where(c => c.Tabella == numero)
            .GroupBy(c => c.Colonna).OrderBy(g => g.Key)
            .Select(g => g.Select(c => c.Larghezza).FirstOrDefault(l => l > 0)).ToList();
        var griglia = new TableGrid();
        foreach (var larghezza in colonne)
            griglia.Append(new GridColumn { Width = larghezza > 0 ? Twip(larghezza) : LarghezzaColonnaPredefinita });
        return new Table(
            new TableProperties(
                new TableWidth { Type = TableWidthUnitValues.Auto, Width = "0" },
                new TableBorders(Bordo<TopBorder>(), Bordo<LeftBorder>(), Bordo<BottomBorder>(), Bordo<RightBorder>(),
                    Bordo<InsideHorizontalBorder>(), Bordo<InsideVerticalBorder>())),
            griglia);
    }

    /// <summary>
    /// I commenti con sia l'inizio sia la fine nel testo: solo questi si scrivono (Word non accetta un intervallo
    /// aperto e mai chiuso).
    /// </summary>
    private static HashSet<int> CommentiCompleti(ContenutoDocumento contenuto)
    {
        var inizi = new HashSet<int>();
        var fini = new HashSet<int>();
        foreach (var c in contenuto.Paragrafi.SelectMany(p => p.Segmenti).SelectMany(s => s.Testo))
        {
            if (Commenti.IndiceInizio(c) is var i and >= 0 && i < contenuto.Commenti.Count)
                inizi.Add(i);
            else if (Commenti.IndiceFine(c) is var f and >= 0 && f < contenuto.Commenti.Count && inizi.Contains(f))
                fini.Add(f);
        }
        return fini;
    }

    /// <summary>La parte commenti di Word: autore, iniziali, data e testo, un paragrafo per riga.</summary>
    private static Comments CreaCommenti(IReadOnlyList<Commento> commenti, HashSet<int> usati)
    {
        var parte = new Comments();
        foreach (int i in usati.Order())
        {
            var commento = commenti[i];
            var word = new Comment { Id = i.ToString(CultureInfo.InvariantCulture), Author = commento.Autore.Length > 0 ? commento.Autore : "Eleu" };
            if (commento.Iniziali.Length > 0)
                word.Initials = commento.Iniziali;
            if (commento.Data is DateTime data)
                word.Date = data;
            var righe = commento.Testo.ReplaceLineEndings(ACapo).Split(ACapo);
            for (int r = 0; r < righe.Length; r++)
            {
                var paragrafo = new Paragraph();
                if (r == 0)
                    paragrafo.Append(new Run(new AnnotationReferenceMark()));
                paragrafo.Append(new Run(new Text(righe[r]) { Space = SpaceProcessingModeValues.Preserve }));
                word.Append(paragrafo);
            }
            parte.Append(word);
        }
        return parte;
    }

    /// <summary>La parte note di Word: i due separatori obbligatori e poi le note, numerate da 1.</summary>
    private static Footnotes CreaNote(IReadOnlyList<string> testi)
    {
        var note = new Footnotes(
            new Footnote(new Paragraph(new Run(new SeparatorMark()))) { Type = FootnoteEndnoteValues.Separator, Id = -1 },
            new Footnote(new Paragraph(new Run(new ContinuationSeparatorMark()))) { Type = FootnoteEndnoteValues.ContinuationSeparator, Id = 0 });
        for (int i = 0; i < testi.Count; i++)
        {
            var righe = testi[i].ReplaceLineEndings(ACapo).Split(ACapo);
            var nota = new Footnote { Id = i + 1 };
            for (int r = 0; r < righe.Length; r++)
            {
                var paragrafo = new Paragraph();
                if (r == 0)
                    paragrafo.Append(new Run(
                        new RunProperties(new VerticalTextAlignment { Val = VerticalPositionValues.Superscript }),
                        new FootnoteReferenceMark()));
                paragrafo.Append(new Run(new Text((r == 0 ? " " : "") + righe[r]) { Space = SpaceProcessingModeValues.Preserve }));
                nota.Append(paragrafo);
            }
            note.Append(nota);
        }
        return note;
    }

    private static string TestoParagrafo(Paragraph p) => string.Concat(p.Descendants<Text>().Select(t => t.Text));

    private static Run CreaRun(Segmento segmento)
    {
        var run = new Run();
        var proprieta = new RunProperties();
        // L'ordine degli elementi è quello richiesto dallo schema di Word.
        if (segmento.Carattere is string carattere)
            proprieta.Append(new RunFonts { Ascii = carattere, HighAnsi = carattere, ComplexScript = carattere });
        if (segmento.Grassetto)
            proprieta.Append(new Bold());
        if (segmento.Corsivo)
            proprieta.Append(new Italic());
        if (segmento.Barrato)
            proprieta.Append(new Strike());
        if (segmento.Colore is int colore)
            proprieta.Append(new DocumentFormat.OpenXml.Wordprocessing.Color { Val = Esadecimale(colore) });
        if (segmento.Spaziatura != 0)
            proprieta.Append(new Spacing { Val = (int)Math.Round(segmento.Spaziatura * 20) });
        if (segmento.Dimensione is float dimensione)
            proprieta.Append(new FontSize { Val = ((int)Math.Round(dimensione * 2)).ToString(CultureInfo.InvariantCulture) });
        if (segmento.Sottolineato)
            proprieta.Append(new Underline { Val = UnderlineValues.Single });
        if (segmento.Sfondo is int sfondo)
            proprieta.Append(new Shading { Val = ShadingPatternValues.Clear, Color = "auto", Fill = Esadecimale(sfondo) });
        if (segmento.Posizione != PosizioneTesto.Normale)
            proprieta.Append(new VerticalTextAlignment
            {
                Val = segmento.Posizione == PosizioneTesto.Apice ? VerticalPositionValues.Superscript : VerticalPositionValues.Subscript,
            });
        if (proprieta.HasChildren)
            run.Append(proprieta);

        var pezzi = segmento.Testo.Split('\t');
        for (int i = 0; i < pezzi.Length; i++)
        {
            if (i > 0)
                run.Append(new TabChar());
            if (pezzi[i].Length > 0)
                run.Append(new Text(pezzi[i]) { Space = SpaceProcessingModeValues.Preserve });
        }
        return run;
    }

    private static Styles CreaStili(string carattere, float dimensioneBase)
    {
        string MezziPunti(float punti) => ((int)Math.Round(punti * 2)).ToString(CultureInfo.InvariantCulture);

        var stili = new Styles(
            new DocDefaults(
                new RunPropertiesDefault(new RunPropertiesBaseStyle(
                    new RunFonts { Ascii = carattere, HighAnsi = carattere, ComplexScript = carattere },
                    new FontSize { Val = MezziPunti(dimensioneBase) },
                    new Languages { Val = "it-IT" })),
                new ParagraphPropertiesDefault(new ParagraphPropertiesBaseStyle(
                    new SpacingBetweenLines { After = "120" }))),
            new Style(new StyleName { Val = "Normal" }, new PrimaryStyle())
            {
                Type = StyleValues.Paragraph,
                StyleId = "Normal",
                Default = true,
            });

        for (int livello = 1; livello <= 9; livello++)
        {
            stili.Append(new Style(
                new StyleName { Val = $"heading {livello}" },
                new BasedOn { Val = "Normal" },
                new NextParagraphStyle { Val = "Normal" },
                new PrimaryStyle(),
                new StyleParagraphProperties(
                    new KeepNext(),
                    new SpacingBetweenLines { Before = "240", After = "60" },
                    new OutlineLevel { Val = livello - 1 }),
                new StyleRunProperties(
                    new Bold(),
                    new FontSize { Val = MezziPunti(ContenutoDocumento.DimensioneTitolo(livello)) }))
            {
                Type = StyleValues.Paragraph,
                StyleId = $"Heading{livello}",
            });
        }
        return stili;
    }

    private static AbstractNum CreaDefinizioneElenco(int id, NumberFormatValues formato, string testo) =>
        new(new Level(
                new StartNumberingValue { Val = 1 },
                new NumberingFormat { Val = formato },
                new LevelText { Val = testo },
                new LevelJustification { Val = LevelJustificationValues.Left },
                new PreviousParagraphProperties(new Indentation { Left = "720", Hanging = "360" }))
            { LevelIndex = 0 })
        { AbstractNumberId = id };

    // Colori dell'evidenziatore di Word (nome → 0xRRGGBB).
    private static readonly Dictionary<string, int> ColoriEvidenziatore = new(StringComparer.OrdinalIgnoreCase)
    {
        ["yellow"] = 0xFFFF00, ["green"] = 0x00FF00, ["cyan"] = 0x00FFFF, ["magenta"] = 0xFF00FF,
        ["blue"] = 0x0000FF, ["red"] = 0xFF0000, ["darkBlue"] = 0x000080, ["darkCyan"] = 0x008080,
        ["darkGreen"] = 0x008000, ["darkMagenta"] = 0x800080, ["darkRed"] = 0x800000, ["darkYellow"] = 0x808000,
        ["darkGray"] = 0x808080, ["lightGray"] = 0xC0C0C0, ["black"] = 0x000000, ["white"] = 0xFFFFFF,
    };

    private static int? LeggiColore(string? esadecimale) =>
        esadecimale is not null && !esadecimale.Equals("auto", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(esadecimale, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var colore)
            ? colore
            : null;

    private static float Punti(string? twip) =>
        float.TryParse(twip, NumberStyles.Float, CultureInfo.InvariantCulture, out var valore) ? valore / 20f : 0f;

    private static Segmento LeggiFormato(RunProperties? proprieta)
    {
        static bool Attivo(OnOffType? valore) => valore is not null && (valore.Val is null || valore.Val.Value);

        float? dimensione = null;
        if (float.TryParse(proprieta?.FontSize?.Val?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var mezziPunti))
            dimensione = mezziPunti / 2f;

        var sottolineato = proprieta?.Underline?.Val;
        var verticale = proprieta?.VerticalTextAlignment?.Val?.InnerText;
        var evidenziato = proprieta?.Highlight?.Val?.InnerText;
        int? sfondo = LeggiColore(proprieta?.Shading?.Fill?.Value);
        if (sfondo is null && evidenziato is not null && ColoriEvidenziatore.TryGetValue(evidenziato, out var coloreEvidenziatore))
            sfondo = coloreEvidenziatore;

        return new Segmento(
            "",
            Attivo(proprieta?.Bold),
            Attivo(proprieta?.Italic),
            sottolineato is not null && sottolineato.Value != UnderlineValues.None,
            dimensione,
            proprieta?.RunFonts?.Ascii?.Value ?? proprieta?.RunFonts?.HighAnsi?.Value,
            Attivo(proprieta?.Strike),
            verticale == "superscript" ? PosizioneTesto.Apice : verticale == "subscript" ? PosizioneTesto.Pedice : PosizioneTesto.Normale,
            LeggiColore(proprieta?.Color?.Val?.Value),
            sfondo,
            (proprieta?.Spacing?.Val?.Value ?? 0) / 20f);
    }

    private static void LeggiSpaziERientri(ParagraphProperties? proprieta, Paragrafo paragrafo)
    {
        var spazi = proprieta?.SpacingBetweenLines;
        paragrafo.SpazioPrima = Punti(spazi?.Before?.Value);
        paragrafo.SpazioDopo = Punti(spazi?.After?.Value);
        if (spazi?.Line?.Value is string riga && (spazi.LineRule is null || spazi.LineRule.InnerText == "auto")
            && float.TryParse(riga, NumberStyles.Float, CultureInfo.InvariantCulture, out var duecentoquarantesimi) && duecentoquarantesimi > 0)
            paragrafo.Interlinea = duecentoquarantesimi / 240f;

        var rientri = proprieta?.Indentation;
        paragrafo.RientroSinistro = Punti(rientri?.Left?.Value ?? rientri?.Start?.Value);
        paragrafo.RientroDestro = Punti(rientri?.Right?.Value ?? rientri?.End?.Value);
        paragrafo.RientroPrimaRiga = rientri?.Hanging?.Value is string sporgente ? -Punti(sporgente) : Punti(rientri?.FirstLine?.Value);
    }

    private static Allineamento LeggiAllineamento(Justification? giustificazione)
    {
        var valore = giustificazione?.Val?.Value;
        if (valore == JustificationValues.Center)
            return Allineamento.Centro;
        if (valore == JustificationValues.Right || valore == JustificationValues.End)
            return Allineamento.Destra;
        if (valore == JustificationValues.Both || valore == JustificationValues.Distribute)
            return Allineamento.Giustificato;
        return Allineamento.Sinistra;
    }

    private static int LivelloTitolo(ParagraphProperties? proprieta, Styles? stili)
    {
        if (proprieta?.OutlineLevel?.Val?.Value is int livelloDiretto && livelloDiretto < 9)
            return livelloDiretto + 1;

        var idStile = proprieta?.ParagraphStyleId?.Val?.Value;
        for (int passi = 0; idStile is not null && passi < 10; passi++)
        {
            var stile = stili?.Elements<Style>().FirstOrDefault(s => s.StyleId?.Value == idStile);
            var nome = stile?.StyleName?.Val?.Value ?? idStile;

            if (nome.Equals("Title", StringComparison.OrdinalIgnoreCase))
                return 1;
            var titolo = NomeTitolo().Match(nome);
            if (titolo.Success)
                return int.Parse(titolo.Groups[1].Value);
            if (stile?.StyleParagraphProperties?.OutlineLevel?.Val?.Value is int livelloStile && livelloStile < 9)
                return livelloStile + 1;

            idStile = stile?.BasedOn?.Val?.Value;
        }
        return 0;
    }

    private enum TipoElenco { Nessuno, Puntato, Numerato }

    private static TipoElenco LeggiElenco(NumberingProperties? proprieta, Numbering? numerazione)
    {
        int idNumero = proprieta?.NumberingId?.Val?.Value ?? 0;
        if (idNumero == 0)
            return TipoElenco.Nessuno;
        int livello = proprieta?.NumberingLevelReference?.Val?.Value ?? 0;

        var istanza = numerazione?.Elements<NumberingInstance>().FirstOrDefault(n => n.NumberID?.Value == idNumero);
        var idAstratto = istanza?.AbstractNumId?.Val?.Value;
        var astratto = numerazione?.Elements<AbstractNum>().FirstOrDefault(a => a.AbstractNumberId?.Value == idAstratto);
        var definizione = astratto?.Elements<Level>().FirstOrDefault(l => l.LevelIndex?.Value == livello);
        var formato = definizione?.NumberingFormat?.Val?.Value;

        return formato is null || formato == NumberFormatValues.Bullet || formato == NumberFormatValues.None
            ? TipoElenco.Puntato
            : TipoElenco.Numerato;
    }

    [GeneratedRegex(@"^(?:heading|titolo)\s*(\d)$", RegexOptions.IgnoreCase)]
    private static partial Regex NomeTitolo();
}
