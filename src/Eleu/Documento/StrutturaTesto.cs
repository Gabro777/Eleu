namespace Eleu.Documento;

/// <summary>Una tabella nel testo del campo: dove inizia e finisce e, riga per riga, dove sta il testo di ogni cella.</summary>
public sealed record TabellaNelTesto(int Inizio, int Fine, IReadOnlyList<IReadOnlyList<(int Inizio, int Fine)>> Righe);

/// <summary>La cella in cui sta una posizione: tabella, riga e colonna (da 0).</summary>
public sealed record CellaNelTesto(TabellaNelTesto Tabella, int NumeroTabella, int Riga, int Colonna)
{
    public (int Inizio, int Fine) Testo => Tabella.Righe[Riga][Colonna];
}

/// <summary>
/// Come è fatto il testo del campo (vedi Editor.CampoTesto): paragrafi separati da '\n' e, in tabella,
/// righe che iniziano con <see cref="InizioRiga"/> e finiscono con <see cref="FineRiga"/> (ognuno su una
/// "riga" tutta sua) e celle che finiscono con <see cref="FineCella"/>. Per chi legge, ogni cella è un
/// paragrafo e le righe tecniche non esistono.
/// </summary>
public static class StrutturaTesto
{
    public const char InizioRiga = (char)0xFFF9;
    public const char FineRiga = (char)0xFFFB;
    public const char FineCella = '\a';

    public static bool Tecnico(char c) => c is InizioRiga or FineRiga or FineCella;

    /// <summary>Il testo senza i caratteri tecnici delle tabelle; la fine di una cella diventa uno spazio.</summary>
    public static string Leggibile(string testo) =>
        testo.Replace(FineCella, ' ').Replace(InizioRiga.ToString(), "").Replace(FineRiga.ToString(), "");

    /// <summary>I paragrafi veri, come intervalli [Inizio, Fine) senza il separatore; ogni cella è un paragrafo.</summary>
    public static List<(int Inizio, int Fine)> Paragrafi(string testo)
    {
        var paragrafi = new List<(int, int)>();
        int inizio = 0;
        for (int i = 0; i <= testo.Length; i++)
        {
            if (i < testo.Length && testo[i] is not ('\n' or FineCella))
                continue;
            bool tecnico = i - inizio == 1 && testo[inizio] is InizioRiga or FineRiga;
            if (!tecnico)
                paragrafi.Add((inizio, i));
            inizio = i + 1;
        }
        return paragrafi;
    }

    /// <summary>Il numero (da 0) del paragrafo in cui sta la posizione; su una riga tecnica, il paragrafo seguente.</summary>
    public static int ParagrafoDi(IReadOnlyList<(int Inizio, int Fine)> paragrafi, int posizione)
    {
        for (int i = 0; i < paragrafi.Count; i++)
            if (posizione <= paragrafi[i].Fine)
                return i;
        return paragrafi.Count - 1;
    }

    public static List<TabellaNelTesto> Tabelle(string testo)
    {
        var tabelle = new List<TabellaNelTesto>();
        int i = 0;
        while (i < testo.Length)
        {
            if (!InizioRigaIn(testo, i))
            {
                i++;
                continue;
            }
            int inizio = i;
            var righe = new List<IReadOnlyList<(int, int)>>();
            while (InizioRigaIn(testo, i))
            {
                int fineRiga = testo.IndexOf(FineRiga, i + 2);
                if (fineRiga < 0)
                    break;
                var celle = new List<(int, int)>();
                int cella = i + 2;
                for (int j = cella; j < fineRiga; j++)
                {
                    if (testo[j] != FineCella)
                        continue;
                    celle.Add((cella, j));
                    cella = j + 1;
                }
                if (celle.Count == 0)
                    celle.Add((i + 2, fineRiga));
                righe.Add(celle);
                i = Math.Min(testo.Length, fineRiga + 2);
            }
            if (righe.Count == 0)
            {
                i++;
                continue;
            }
            tabelle.Add(new TabellaNelTesto(inizio, i, righe));
        }
        return tabelle;
    }

    private static bool InizioRigaIn(string testo, int i) =>
        i + 1 < testo.Length && testo[i] == InizioRiga && testo[i + 1] == '\n';

    /// <summary>La cella in cui sta la posizione, o null se non è in una tabella.</summary>
    public static CellaNelTesto? CellaDi(string testo, int posizione)
    {
        var tabelle = Tabelle(testo);
        for (int t = 0; t < tabelle.Count; t++)
        {
            var tabella = tabelle[t];
            if (posizione < tabella.Inizio || posizione >= tabella.Fine)
                continue;
            for (int r = 0; r < tabella.Righe.Count; r++)
            {
                var celle = tabella.Righe[r];
                int fineRiga = celle[^1].Fine + 3; // \a, FineRiga, \n
                if (posizione >= fineRiga && r < tabella.Righe.Count - 1)
                    continue;
                int colonna = celle.Count - 1;
                for (int c = 0; c < celle.Count; c++)
                {
                    if (posizione <= celle[c].Fine)
                    {
                        colonna = c;
                        break;
                    }
                }
                return new CellaNelTesto(tabella, t, r, colonna);
            }
        }
        return null;
    }
}
