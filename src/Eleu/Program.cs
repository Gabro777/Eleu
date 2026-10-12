namespace Eleu;

static class Program
{
    [STAThread]
    static void Main(string[] argomenti)
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new ContestoEleu(argomenti.FirstOrDefault()));
    }
}

/// <summary>
/// Eleu resta aperto finché c'è almeno una finestra: ogni documento ha la sua, come in Word, così Alt+Tab e gli
/// screen reader le trattano come finestre normali. Le impostazioni sono le stesse per tutte.
/// </summary>
public sealed class ContestoEleu : ApplicationContext
{
    private static ContestoEleu? _corrente;
    private readonly List<FinestraPrincipale> _finestre = [];

    public ContestoEleu(string? file)
    {
        _corrente = this;
        // Aperto senza un file: se le Opzioni lo chiedono, si riprende l'ultimo documento usato.
        var impostazioni = Impostazioni.Condivise;
        if (file is null && impostazioni.RiapriUltimo && impostazioni.FileRecenti.FirstOrDefault() is string ultimo && File.Exists(ultimo))
            file = ultimo;
        Aggiungi(new FinestraPrincipale(file));
    }

    /// <summary>Le finestre aperte, nell'ordine in cui sono state aperte.</summary>
    public static IReadOnlyList<FinestraPrincipale> Finestre => _corrente?._finestre ?? [];

    /// <summary>Eleu sta girando come programma (e non, per esempio, dentro le prove automatiche).</summary>
    public static bool Attivo => _corrente is not null;

    /// <summary>Apre un'altra finestra, vuota o con un file.</summary>
    public static FinestraPrincipale Apri(string? file = null, bool guida = false)
    {
        var finestra = new FinestraPrincipale(file, guida);
        if (_corrente is null)
            finestra.Show(); // nei test, senza contesto
        else
            _corrente.Aggiungi(finestra);
        return finestra;
    }

    private void Aggiungi(FinestraPrincipale finestra)
    {
        _finestre.Add(finestra);
        finestra.FormClosed += (_, _) =>
        {
            _finestre.Remove(finestra);
            if (_finestre.Count == 0)
                ExitThread();
        };
        finestra.Show();
    }
}
