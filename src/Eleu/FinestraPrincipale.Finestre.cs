namespace Eleu;

// Più documenti aperti (menu Finestre): ogni documento ha la sua finestra; da qui si passa dall'uno all'altro.
public sealed partial class FinestraPrincipale
{
    /// <summary>Porta in primo piano questa finestra e dice dove si è.</summary>
    public void Attiva(string? prefisso = null)
    {
        if (WindowState == FormWindowState.Minimized)
            WindowState = _impostazioni.Ingrandita ? FormWindowState.Maximized : FormWindowState.Normal;
        Activate();
        _testo.Focus();
        var finestre = ContestoEleu.Finestre;
        int numero = finestre.ToList().IndexOf(this) + 1;
        var dove = finestre.Count > 1 ? $"{NomeDocumento}, finestra {numero} di {finestre.Count}" : NomeDocumento;
        AnnunciaTraPoco(prefisso is null ? dove : $"{prefisso}. {dove}");
    }

    private void FinestraVicina(int passo)
    {
        var finestre = ContestoEleu.Finestre;
        if (finestre.Count < 2)
        {
            _annunciatore.Annuncia("C'è una sola finestra aperta");
            return;
        }
        int qui = finestre.ToList().IndexOf(this);
        finestre[(qui + passo + finestre.Count) % finestre.Count].Attiva();
    }

    /// <summary>Il menu Finestre si riempie quando si apre: le voci fisse e un documento per riga, numerati.</summary>
    private void AggiornaMenuFinestre(ToolStripMenuItem menu, ToolStripItem[] fisse)
    {
        menu.DropDownItems.Clear();
        menu.DropDownItems.AddRange(fisse);
        var finestre = ContestoEleu.Finestre;
        if (finestre.Count == 0)
            return;
        menu.DropDownItems.Add(new ToolStripSeparator());
        for (int i = 0; i < finestre.Count; i++)
        {
            var finestra = finestre[i];
            var voce = new ToolStripMenuItem($"{(i < 9 ? $"&{i + 1} " : "")}{finestra.NomeDocumento}{(finestra._testo.Modified ? " (modificato)" : "")}",
                null, (_, _) => BeginInvoke(() => finestra.Attiva()))
            {
                Checked = finestra == this,
            };
            menu.DropDownItems.Add(voce);
        }
    }

    private ToolStripMenuItem CreaMenuFinestre()
    {
        ToolStripItem[] fisse =
        [
            Voce("&Nuova finestra", Keys.None, () => ContestoEleu.Apri()),
            Voce("Finestra &successiva", Keys.Control | Keys.Alt | Keys.F6, () => FinestraVicina(+1), "Ctrl+Alt+F6"),
            Voce("Finestra &precedente", Keys.Control | Keys.Alt | Keys.Shift | Keys.F6, () => FinestraVicina(-1), "Ctrl+Alt+Maiusc+F6"),
            Voce("&Chiudi documento", Keys.Control | Keys.F4, Close, "Ctrl+F4"),
        ];
        var menu = new ToolStripMenuItem("Fi&nestre");
        menu.DropDownItems.AddRange(fisse);
        menu.DropDownOpening += (_, _) => AggiornaMenuFinestre(menu, fisse);
        return menu;
    }
}
