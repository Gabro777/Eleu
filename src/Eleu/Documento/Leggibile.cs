namespace Eleu.Documento;

/// <summary>
/// Il documento da leggere di seguito, per il Braille e gli audiolibri: i segnalibri spariscono, il richiamo
/// di una nota diventa "[nota 1]", un'immagine "[Immagine: descrizione]", le tabelle si leggono riga per riga
/// (<see cref="Griglia.Leggibile"/>) e in fondo c'è il capitolo "Note".
/// </summary>
public static class Leggibile
{
    public static ContenutoDocumento Da(ContenutoDocumento contenuto)
    {
        var risultato = new ContenutoDocumento { Pagina = contenuto.Pagina };
        foreach (var paragrafo in contenuto.Paragrafi)
        {
            var nuovo = paragrafo.CopiaProprieta();
            nuovo.Cella = paragrafo.Cella;
            nuovo.SaltoPagina = paragrafo.SaltoPagina;
            nuovo.PaginaStampa = paragrafo.PaginaStampa;
            foreach (var segmento in paragrafo.Segmenti)
                nuovo.Aggiungi(segmento with { Testo = Testo(segmento.Testo, contenuto) });
            risultato.Paragrafi.Add(nuovo);
        }
        risultato = Griglia.Leggibile(risultato);

        if (contenuto.Note.Count > 0)
        {
            var titolo = new Paragrafo { LivelloTitolo = 1, SaltoPagina = true };
            titolo.Aggiungi(new Segmento("Note"));
            risultato.Paragrafi.Add(titolo);
            for (int i = 0; i < contenuto.Note.Count; i++)
            {
                var nota = new Paragrafo();
                nota.Aggiungi(new Segmento($"{i + 1}. {contenuto.Note[i].ReplaceLineEndings(" ")}"));
                risultato.Paragrafi.Add(nota);
            }
        }
        return risultato;
    }

    private static string Testo(string testo, ContenutoDocumento contenuto)
    {
        var risultato = new System.Text.StringBuilder();
        foreach (var c in testo)
        {
            if (Segnalibri.Indice(c) >= 0 || Commenti.Segnaposto(c))
                continue;
            if (Note.Indice(c) is var nota and >= 0)
                risultato.Append($" [nota {nota + 1}]");
            else if (Immagini.Indice(c) is var immagine and >= 0)
                risultato.Append(immagine < contenuto.Immagini.Count && contenuto.Immagini[immagine].Descrizione.Length > 0
                    ? $" [Immagine: {contenuto.Immagini[immagine].Descrizione}] "
                    : " [Immagine senza descrizione] ");
            else
                risultato.Append(c);
        }
        return risultato.ToString();
    }
}
