using System.Drawing.Printing;
using Eleu.Braille;

namespace Eleu.Interfaccia;

/// <summary>
/// Inserisci > Immagini > Grafica in rilievo: prepara l'immagine per essere toccata. Si sceglie il tratto
/// (contorni o zone piene), quanto scuro deve essere il nero e la grandezza; l'anteprima mostra le celle Braille
/// (si legge anche con il display Braille). Poi: stampa Braille, file BRF, oppure il disegno da stampare su
/// carta a microcapsule, con la didascalia in punti Braille.
/// </summary>
public sealed class FinestraGraficaTattile : Modulo
{
    private static readonly string[] Tratti = ["Solo i contorni (consigliato)", "Zone scure piene"];
    private static readonly (string Nome, int Valore)[] Soglie =
    [
        ("Automatica", 0), ("Altissima: più punti", 230), ("Alta", 200), ("Media", 150), ("Bassa", 100), ("Bassissima: meno punti", 50),
    ];

    private readonly Image _immagine;
    private readonly int _righePagina;
    private readonly Func<string, string?> _traduci;
    private readonly ComboBox _tratto, _soglia, _stampante;
    private readonly CheckBox _inverti;
    private readonly NumericUpDown _celle, _righe;
    private readonly TextBox _didascalia, _anteprima;
    private readonly Label _riepilogo;
    private readonly System.Windows.Forms.Timer _attesa = new() { Interval = 300 };
    private List<string> _celleImmagine = [];

    /// <param name="traduci">Traduce in Braille la didascalia; null se il Braille non è disponibile.</param>
    public FinestraGraficaTattile(Image immagine, string descrizione, IReadOnlyList<string> stampanti, string? stampanteScelta,
        int celle, int righePagina, Func<string, string?> traduci) : base("Grafica in rilievo", 560)
    {
        _immagine = immagine;
        _righePagina = righePagina;
        _traduci = traduci;
        _tratto = Campo("&Tratto:", Scelta(Tratti, 0));
        _soglia = Campo("S&ensibilità:", Scelta(Soglie.Select(s => s.Nome), 0));
        _inverti = RigaIntera(new CheckBox { Text = "&Inverti: in rilievo lo sfondo invece della figura", AutoSize = true });
        _celle = Campo("&Larghezza massima (celle):", Numero(celle, 10, 80, 1, 0));
        _righe = Campo("&Altezza massima (righe):", Numero(Math.Max(3, righePagina - 3), 3, 250, 1, 0));
        _didascalia = Campo("&Didascalia in cima:", new TextBox { Text = descrizione });
        _anteprima = Campo("Anteprima &Braille:", new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Height = 320,
            Font = new Font("Segoe UI Symbol", 12f),
        });
        _riepilogo = RigaIntera(new Label { AutoSize = true, MaximumSize = new Size(760, 0), Margin = new Padding(0, 4, 0, 4) });
        _stampante = Campo("Stampa&nte Braille:", Scelta(stampanti.Prepend("Nessuna"), Math.Max(0, stampanti.ToList().IndexOf(stampanteScelta ?? "") + 1)));

        var pulsanti = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 12, 0, 0) };
        var stampa = new Button { Text = "Stam&pa Braille", AutoSize = true };
        var salvaBrf = new Button { Text = "Salva &file BRF...", AutoSize = true };
        var microcapsule = new Button { Text = "Stampa su carta a &microcapsule...", AutoSize = true };
        var salvaDisegno = new Button { Text = "Salva il dise&gno...", AutoSize = true };
        var chiudi = new Button { Text = "&Chiudi", AutoSize = true, DialogResult = DialogResult.Cancel };
        pulsanti.Controls.AddRange([stampa, salvaBrf, microcapsule, salvaDisegno, chiudi]);
        RigaIntera(pulsanti);
        CancelButton = chiudi;

        stampa.Click += (_, _) => StampaBraille();
        salvaBrf.Click += (_, _) => SalvaBrf();
        microcapsule.Click += (_, _) => StampaMicrocapsule();
        salvaDisegno.Click += (_, _) => SalvaDisegno();

        _attesa.Tick += (_, _) => { _attesa.Stop(); Aggiorna(); };
        _didascalia.TextChanged += (_, _) => Rimanda();
        _tratto.SelectedIndexChanged += (_, _) => Rimanda();
        _soglia.SelectedIndexChanged += (_, _) => Rimanda();
        _inverti.CheckedChanged += (_, _) => Rimanda();
        _celle.ValueChanged += (_, _) => Rimanda();
        _righe.ValueChanged += (_, _) => Rimanda();

        Aggiorna();
        ActiveControl = _tratto;
    }

    public string? StampanteScelta => _stampante.SelectedIndex > 0 ? (string)_stampante.SelectedItem! : null;

    private OpzioniGrafica Opzioni => new(
        _tratto.SelectedIndex == 1 ? TrattoTattile.Riempito : TrattoTattile.Contorni,
        Soglie[Math.Max(0, _soglia.SelectedIndex)].Valore,
        _inverti.Checked);

    private int Celle => (int)_celle.Value;

    private void Rimanda()
    {
        _attesa.Stop();
        _attesa.Start();
    }

    private void Aggiorna()
    {
        _celleImmagine = GraficaTattile.InCelle(_immagine, Celle, (int)_righe.Value, Opzioni);
        _anteprima.Text = string.Join(Environment.NewLine, _celleImmagine);
        int punti = _celleImmagine.Sum(r => r.Sum(c => int.PopCount(c - Braille.TraduttoreBraille.CellaVuota)));
        int pagine = Pagine().Count;
        _riepilogo.Text = $"L'immagine occupa {_celleImmagine.Count} righe di {Celle} celle, {punti} punti in rilievo, " +
                          $"{(pagine == 1 ? "1 pagina" : $"{pagine} pagine")} Braille.";
        _anteprima.AccessibleDescription = _riepilogo.Text;
    }

    private string? DidascaliaBraille() =>
        _didascalia.Text.Trim() is { Length: > 0 } testo ? _traduci(testo) : null;

    private List<List<string>> Pagine() =>
        GraficaTattile.Pagine(_celleImmagine, Celle, _righePagina, DidascaliaBraille());

    private void StampaBraille()
    {
        if (StampanteScelta is not string stampante)
        {
            MessageBox.Show(this, "Scegli prima la stampante Braille, oppure salva il file BRF.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            _stampante.Focus();
            return;
        }
        try
        {
            StampanteBraille.Stampa(stampante, FormatoBrf.Byte(Pagine()), "Grafica in rilievo");
            MessageBox.Show(this, $"Grafica inviata a {stampante}.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (IOException e)
        {
            MessageBox.Show(this, $"{e.Message}\n\nControlla che la stampante sia accesa e collegata.", Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void SalvaBrf()
    {
        using var dialogo = new SaveFileDialog { Title = "Salva il file Braille", Filter = "File Braille (*.brf)|*.brf", FileName = "Grafica", AddExtension = true, OverwritePrompt = true };
        if (dialogo.ShowDialog(this) != DialogResult.OK)
            return;
        Scrivi(dialogo.FileName, () => File.WriteAllBytes(dialogo.FileName, FormatoBrf.Byte(Pagine())));
    }

    /// <summary>Il disegno per un foglio A4 con i margini di 1 cm: 190 per 277 millimetri.</summary>
    private Bitmap Disegno(float larghezzaMm = 190, float altezzaMm = 277)
    {
        var didascalia = DidascaliaBraille() is string braille
            ? GraficaTattile.AndaACapo(braille, (int)(larghezzaMm / GraficaTattile.PassoCella))
            : null;
        return GraficaTattile.PerCartaARilievo(_immagine, larghezzaMm, altezzaMm, Opzioni, didascalia);
    }

    private void SalvaDisegno()
    {
        using var dialogo = new SaveFileDialog { Title = "Salva il disegno da stampare", Filter = "Immagine PNG (*.png)|*.png", FileName = "Grafica in rilievo", AddExtension = true, OverwritePrompt = true };
        if (dialogo.ShowDialog(this) != DialogResult.OK)
            return;
        Scrivi(dialogo.FileName, () =>
        {
            using var disegno = Disegno();
            disegno.Save(dialogo.FileName, System.Drawing.Imaging.ImageFormat.Png);
        });
    }

    private void StampaMicrocapsule()
    {
        using var documento = new PrintDocument { DocumentName = "Grafica in rilievo" };
        using var dialogo = new PrintDialog { Document = documento, UseEXDialog = true, AllowSomePages = false };
        if (dialogo.ShowDialog(this) != DialogResult.OK)
            return;
        documento.PrintPage += (_, e) =>
        {
            var area = e.MarginBounds; // centesimi di pollice
            using var disegno = Disegno(area.Width * 0.254f, area.Height * 0.254f);
            e.Graphics!.DrawImage(disegno, area);
            e.HasMorePages = false;
        };
        try
        {
            documento.Print();
            MessageBox.Show(this, "Disegno inviato alla stampante. Dopo la stampa, passa il foglio nel fornetto per la carta a microcapsule.",
                Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (InvalidPrinterException e)
        {
            MessageBox.Show(this, e.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void Scrivi(string percorso, Action scrivi)
    {
        try
        {
            scrivi();
            MessageBox.Show(this, $"Salvato {Path.GetFileName(percorso)}.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.ExternalException)
        {
            MessageBox.Show(this, $"Impossibile salvare {Path.GetFileName(percorso)}.\n\n{e.Message}", Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _attesa.Dispose();
        base.Dispose(disposing);
    }
}
