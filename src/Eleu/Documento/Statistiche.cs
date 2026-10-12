namespace Eleu.Documento;

public readonly record struct Statistiche(int Parole, int Caratteri, int Paragrafi)
{
    public static Statistiche Calcola(string testo)
    {
        int parole = 0;
        bool inParola = false;
        int caratteri = 0;
        foreach (char c in testo)
        {
            // Il salto pagina e i caratteri che reggono le tabelle non sono caratteri da contare.
            if (c is not ('\n' or '\r' or '\f') && !StrutturaTesto.Tecnico(c))
                caratteri++;

            if (char.IsWhiteSpace(c) || StrutturaTesto.Tecnico(c))
            {
                inParola = false;
            }
            else if (!inParola && char.IsLetterOrDigit(c))
            {
                parole++;
                inParola = true;
            }
        }

        int paragrafi = StrutturaTesto.Paragrafi(testo).Count(p => !string.IsNullOrWhiteSpace(testo[p.Inizio..p.Fine]));
        return new Statistiche(parole, caratteri, paragrafi);
    }

    public string Descrizione() =>
        $"{Conta(Parole, "parola", "parole")}, {Conta(Caratteri, "carattere", "caratteri")}, {Conta(Paragrafi, "paragrafo", "paragrafi")}";

    private static string Conta(int n, string singolare, string plurale) => $"{n} {(n == 1 ? singolare : plurale)}";
}
