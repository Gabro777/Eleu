using Eleu.Documento;

namespace Eleu.Tests;

internal static class Supporto
{
    private static readonly object UnaFinestraAllaVolta = new();

    /// <summary>
    /// I controlli WinForms vanno usati da un thread STA. Uno alla volta: alcune parti di Windows Forms condividono
    /// dati tra i thread e, con più prove che creano finestre insieme, ogni tanto si pestano i piedi.
    /// </summary>
    public static void EseguiSta(Action azione)
    {
        lock (UnaFinestraAllaVolta)
            EseguiStaDaSolo(azione);
    }

    private static void EseguiStaDaSolo(Action azione)
    {
        Exception? errore = null;
        var thread = new Thread(() =>
        {
            try { azione(); }
            catch (Exception e) { errore = e; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (errore is not null)
            throw new Exception("Errore nel thread STA", errore);
    }

    /// <summary>Rappresentazione leggibile del documento, per confrontarlo nei test.</summary>
    public static string Descrivi(ContenutoDocumento contenuto) =>
        string.Join("\n", contenuto.Paragrafi.Select(p =>
            $"[T{p.LivelloTitolo}{(p.SaltoPagina ? " nuova-pagina" : "")}{(p.Elenco ? " elenco" : "")}{(p.Numerato ? " numerato" : "")} {p.Allineamento}" +
            $" rientri {p.RientroSinistro}/{p.RientroDestro}/{p.RientroPrimaRiga} spazi {p.SpazioPrima}/{p.SpazioDopo} interlinea {p.Interlinea}] " +
            string.Concat(p.Segmenti.Select(s =>
                $"<{(s.Grassetto ? "b" : "")}{(s.Corsivo ? "i" : "")}{(s.Sottolineato ? "u" : "")}{(s.Barrato ? "s" : "")}" +
                $" {s.Dimensione} {s.Carattere} {s.Posizione} colore={s.Colore:X6} sfondo={s.Sfondo:X6} spaz={s.Spaziatura}>{s.Testo}"))));

    /// <summary>Documento di esempio con tutto ciò che Eleu sa conservare, con dimensioni esplicite.</summary>
    public static ContenutoDocumento Esempio()
    {
        var contenuto = new ContenutoDocumento();

        var titolo = new Paragrafo { LivelloTitolo = 1 };
        titolo.Aggiungi(new Segmento("Capitolo primo", Grassetto: true, Dimensione: 18));
        contenuto.Paragrafi.Add(titolo);

        var testo = new Paragrafo { Allineamento = Allineamento.Giustificato };
        testo.Aggiungi(new Segmento("Testo ", Dimensione: 12));
        testo.Aggiungi(new Segmento("grassetto", Grassetto: true, Dimensione: 12));
        testo.Aggiungi(new Segmento(" e ", Dimensione: 12));
        testo.Aggiungi(new Segmento("corsivo sottolineato", Corsivo: true, Sottolineato: true, Dimensione: 12));
        contenuto.Paragrafi.Add(testo);

        var punto1 = new Paragrafo { Elenco = true };
        punto1.Aggiungi(new Segmento("primo punto", Dimensione: 12));
        contenuto.Paragrafi.Add(punto1);

        var punto2 = new Paragrafo { Elenco = true };
        punto2.Aggiungi(new Segmento("àèìòù € “virgolette” {graffe} \\ barra\tdopo tab", Dimensione: 12));
        contenuto.Paragrafi.Add(punto2);

        var sezione = new Paragrafo { LivelloTitolo = 2, Allineamento = Allineamento.Centro };
        sezione.Aggiungi(new Segmento("Sezione", Grassetto: true, Dimensione: 15));
        contenuto.Paragrafi.Add(sezione);

        var destra = new Paragrafo { Allineamento = Allineamento.Destra };
        destra.Aggiungi(new Segmento("Firma", Dimensione: 20));
        contenuto.Paragrafi.Add(destra);

        // Formattazione del carattere: tipo, colori, barrato, apice, pedice, spaziatura.
        var carattere = new Paragrafo();
        carattere.Aggiungi(new Segmento("Times ", Dimensione: 12, Carattere: "Times New Roman"));
        carattere.Aggiungi(new Segmento("rosso", Dimensione: 12, Colore: 0xC00000));
        carattere.Aggiungi(new Segmento(" evidenziato", Dimensione: 12, Sfondo: 0xFFFF00));
        carattere.Aggiungi(new Segmento(" barrato", Dimensione: 12, Barrato: true));
        carattere.Aggiungi(new Segmento(" m", Dimensione: 12));
        carattere.Aggiungi(new Segmento("2", Dimensione: 12, Posizione: PosizioneTesto.Apice));
        carattere.Aggiungi(new Segmento(" H", Dimensione: 12));
        carattere.Aggiungi(new Segmento("2", Dimensione: 12, Posizione: PosizioneTesto.Pedice));
        carattere.Aggiungi(new Segmento("O spaziato", Dimensione: 12, Spaziatura: 2));
        contenuto.Paragrafi.Add(carattere);

        // Formattazione del paragrafo: rientri, spazi, interlinea.
        var rientrato = new Paragrafo
        {
            RientroSinistro = 36, RientroDestro = 18, RientroPrimaRiga = 18,
            SpazioPrima = 6, SpazioDopo = 12, Interlinea = 1.5f,
        };
        rientrato.Aggiungi(new Segmento("Paragrafo rientrato con interlinea una e mezza.", Dimensione: 12));
        contenuto.Paragrafi.Add(rientrato);

        var sporgente = new Paragrafo { RientroSinistro = 36, RientroPrimaRiga = -36, Interlinea = 2 };
        sporgente.Aggiungi(new Segmento("Prima riga sporgente, interlinea doppia.", Dimensione: 12));
        contenuto.Paragrafi.Add(sporgente);

        var nuovaPagina = new Paragrafo { SaltoPagina = true };
        nuovaPagina.Aggiungi(new Segmento("Questo paragrafo inizia su una pagina nuova.", Dimensione: 12));
        contenuto.Paragrafi.Add(nuovaPagina);

        foreach (var voce in new[] { "uno", "due", "tre" })
        {
            var numerato = new Paragrafo { Numerato = true };
            numerato.Aggiungi(new Segmento(voce, Dimensione: 12));
            contenuto.Paragrafi.Add(numerato);
        }

        // Il campo di testo scrive sempre il tipo di carattere: nell'esempio è esplicito ovunque.
        for (int i = 0; i < contenuto.Paragrafi.Count; i++)
        {
            var ricostruito = contenuto.Paragrafi[i].CopiaProprieta();
            foreach (var segmento in contenuto.Paragrafi[i].Segmenti)
                ricostruito.Aggiungi(segmento with { Carattere = segmento.Carattere ?? "Segoe UI" });
            contenuto.Paragrafi[i] = ricostruito;
        }

        return contenuto;
    }
}
