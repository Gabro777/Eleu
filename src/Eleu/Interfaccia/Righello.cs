namespace Eleu.Interfaccia;

/// <summary>
/// Righello sopra il testo, per chi vede: centimetri e rientri del paragrafo corrente, spostabili con il mouse.
/// Non prende il focus: chi usa la tastiera imposta gli stessi rientri da Formato > Paragrafo.
/// I rientri sono in punti, come in <see cref="Editor.FormatoParagrafo"/>.
/// </summary>
public sealed class Righello : Control
{
    private const float PuntiPerCm = 72f / 2.54f;
    private const int Lato = 5; // metà della base dei triangolini

    private enum Segno { Nessuno, PrimaRiga, Sinistro, Destro }

    private Segno _trascinato;
    private float _sinistro, _primaRiga, _destro;

    /// <summary>Ascissa, in pixel del righello, dove inizia il testo senza rientri.</summary>
    public int Origine { get; private set; }

    /// <summary>Ascissa dove il testo va a capo senza rientro destro; null se il testo non va a capo da solo.</summary>
    public int? Fine { get; private set; }

    /// <summary>Pixel per punto, zoom compreso.</summary>
    public float Scala { get; private set; } = 96f / 72f;

    /// <summary>Rientri cambiati con il mouse: sinistro, prima riga (rispetto al sinistro), destro, in punti.</summary>
    public event Action<float, float, float>? RientriCambiati;

    public Righello()
    {
        SetStyle(ControlStyles.Selectable, false);
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
        TabStop = false;
        Dock = DockStyle.Top;
        Height = 30;
        Font = new Font("Segoe UI", 8f);
        AccessibleName = "Righello";
        AccessibleRole = AccessibleRole.Grouping;
    }

    public void Imposta(int origine, int? fine, float scala, float sinistro, float primaRiga, float destro)
    {
        if (_trascinato != Segno.Nessuno)
            return; // durante il trascinamento comanda il mouse
        if (origine == Origine && fine == Fine && scala == Scala && sinistro == _sinistro && primaRiga == _primaRiga && destro == _destro)
            return;
        (Origine, Fine, Scala, _sinistro, _primaRiga, _destro) = (origine, fine, scala, sinistro, primaRiga, destro);
        AccessibleDescription = $"Rientro sinistro {Cm(sinistro)}, prima riga {Cm(primaRiga)}, destro {Cm(destro)}";
        Invalidate();
    }

    private static string Cm(float punti) => $"{punti / PuntiPerCm:0.##} cm";

    private int X(float punti) => Origine + (int)Math.Round(punti * Scala);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var testo = SystemColors.ControlText;
        var bianco = SystemColors.Window;
        g.Clear(SystemColors.Control);

        // Fascia bianca: la zona dove sta il testo.
        int fine = Fine ?? Width;
        int alto = 6, basso = Height - 8;
        using (var sfondo = new SolidBrush(bianco))
            g.FillRectangle(sfondo, Origine, alto, Math.Max(0, fine - Origine), basso - alto);

        // Tacche ogni quarto di centimetro, numeri ogni centimetro.
        using var penna = new Pen(testo);
        using var pennello = new SolidBrush(testo);
        float cm = PuntiPerCm * Scala;
        int centro = (alto + basso) / 2;
        for (int quarto = 1; Origine + quarto * cm / 4 < Width; quarto++)
        {
            int x = Origine + (int)Math.Round(quarto * cm / 4);
            if (quarto % 4 == 0)
            {
                var numero = (quarto / 4).ToString();
                var misura = g.MeasureString(numero, Font);
                g.DrawString(numero, Font, pennello, x - misura.Width / 2, centro - misura.Height / 2);
            }
            else
            {
                int lunghezza = quarto % 2 == 0 ? 3 : 1;
                g.DrawLine(penna, x, centro - lunghezza, x, centro + lunghezza);
            }
        }

        // Rientri: prima riga in alto, sinistro e destro in basso.
        using var segno = new SolidBrush(SystemColors.Highlight);
        Triangolo(g, segno, X(_sinistro + _primaRiga), alto, giu: true);
        Triangolo(g, segno, X(_sinistro), basso, giu: false);
        if (Fine is int destra)
            Triangolo(g, segno, destra - (int)Math.Round(_destro * Scala), basso, giu: false);
    }

    private static void Triangolo(Graphics g, Brush pennello, int x, int y, bool giu)
    {
        int punta = giu ? y + Lato + 1 : y - Lato - 1;
        int baseY = giu ? y - 2 : y + 2;
        g.FillPolygon(pennello, [new Point(x - Lato, baseY), new Point(x + Lato, baseY), new Point(x, punta)]);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left)
            return;
        bool sopra = e.Y < Height / 2;
        var vicini = new List<(Segno Segno, int X)>();
        if (sopra)
            vicini.Add((Segno.PrimaRiga, X(_sinistro + _primaRiga)));
        else
        {
            vicini.Add((Segno.Sinistro, X(_sinistro)));
            if (Fine is int destra)
                vicini.Add((Segno.Destro, destra - (int)Math.Round(_destro * Scala)));
        }
        var preso = vicini.Where(v => Math.Abs(v.X - e.X) <= Lato + 3).OrderBy(v => Math.Abs(v.X - e.X)).FirstOrDefault();
        _trascinato = preso.Segno;
        if (_trascinato != Segno.Nessuno)
            Capture = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_trascinato == Segno.Nessuno)
            return;
        // Si procede a scatti di un quarto di centimetro, come le tacche.
        float punti = (float)Math.Round((e.X - Origine) / Scala / (PuntiPerCm / 4)) * (PuntiPerCm / 4);
        switch (_trascinato)
        {
            case Segno.PrimaRiga:
                _primaRiga = Math.Max(punti, 0) - _sinistro;
                break;
            case Segno.Sinistro:
                _sinistro = Math.Max(punti, 0); // la prima riga segue: si sposta tutto il paragrafo
                _primaRiga = Math.Max(_primaRiga, -_sinistro);
                break;
            case Segno.Destro when Fine is int destra:
                float dalBordo = (float)Math.Round((destra - e.X) / Scala / (PuntiPerCm / 4)) * (PuntiPerCm / 4);
                _destro = Math.Max(dalBordo, 0);
                break;
        }
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_trascinato == Segno.Nessuno)
            return;
        _trascinato = Segno.Nessuno;
        Capture = false;
        RientriCambiati?.Invoke(_sinistro, _primaRiga, _destro);
    }
}
