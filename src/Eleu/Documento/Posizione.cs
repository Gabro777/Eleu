namespace Eleu.Documento;

/// <summary>
/// Posizione del cursore espressa in righe logiche (paragrafi; in tabella ogni cella è una riga),
/// non nelle righe visive che dipendono dalla larghezza della finestra.
/// Riga e colonna partono da 1.
/// </summary>
public readonly record struct Posizione(int Riga, int Colonna, int TotaleRighe)
{
    public static Posizione Calcola(string testo, int indice)
    {
        indice = Math.Clamp(indice, 0, testo.Length);
        var paragrafi = StrutturaTesto.Paragrafi(testo);
        int numero = StrutturaTesto.ParagrafoDi(paragrafi, indice);
        int inizioRiga = paragrafi[numero].Inizio;

        // Il salto pagina a inizio riga è invisibile: non conta come colonna.
        int invisibili = inizioRiga < indice && testo[inizioRiga] == '\f' ? 1 : 0;
        return new Posizione(numero + 1, Math.Max(0, indice - inizioRiga) + 1 - invisibili, paragrafi.Count);
    }

    public string Descrizione() => $"Riga {Riga} di {TotaleRighe}, colonna {Colonna}";
}
