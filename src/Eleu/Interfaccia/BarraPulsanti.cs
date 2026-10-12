using System.Drawing.Drawing2D;

namespace Eleu.Interfaccia;

/// <summary>
/// Colonna dei pulsanti a sinistra del testo, pensata per chi vede poco e usa il mouse: pulsanti grandi con il nome
/// scritto (niente icone da indovinare), angoli arrotondati, colori di sistema (così rispetta il contrasto
/// elevato di Windows) e un bordo spesso sul pulsante che ha il focus.
/// Anche da tastiera: Ctrl+Tab dal testo la raggiunge, le frecce scorrono i pulsanti, Invio o Spazio li
/// premono, Esc torna al testo. Ogni pulsante dice nome e scorciatoia allo screen reader.
/// </summary>
public sealed class BarraPulsanti : ToolStrip
{
    /// <summary>Esc premuto sulla barra: chi la usa vuole tornare al testo.</summary>
    public event Action? Uscita;

    public BarraPulsanti()
    {
        // In colonna, tutti larghi uguale; se la finestra è bassa si apre una seconda colonna, nessuno sparisce.
        Dock = DockStyle.Left;
        LayoutStyle = ToolStripLayoutStyle.Flow;
        ((FlowLayoutSettings)LayoutSettings).FlowDirection = FlowDirection.TopDown;
        ((FlowLayoutSettings)LayoutSettings).WrapContents = true;
        GripStyle = ToolStripGripStyle.Hidden;
        Padding = new Padding(8, 6, 8, 6);
        TabStop = true;
        AccessibleName = "Barra degli strumenti";
        AccessibleRole = AccessibleRole.ToolBar;
        ShowItemToolTips = true;
        Renderer = new Disegno();
        Font = new Font("Segoe UI", 10.5f);
    }

    /// <summary>Pulsante con il nome scritto; la scorciatoia è nel suggerimento e nella descrizione accessibile.</summary>
    public ToolStripButton Pulsante(string testo, string? scorciatoia, Action azione, bool interruttore = false)
    {
        var pulsante = new ToolStripButton(testo)
        {
            DisplayStyle = ToolStripItemDisplayStyle.Text,
            AutoToolTip = false,
            ToolTipText = scorciatoia is null ? testo : $"{testo} ({scorciatoia})",
            AccessibleName = testo,
            AccessibleDescription = scorciatoia,
            AccessibleRole = interruttore ? AccessibleRole.CheckButton : AccessibleRole.PushButton,
            Margin = new Padding(0, 1, 0, 1),
            Padding = new Padding(12, 2, 12, 2),
            TextAlign = ContentAlignment.MiddleLeft,
        };
        pulsante.Click += (_, _) => azione();
        Items.Add(pulsante);
        return pulsante;
    }

    /// <summary>Titolo di un gruppo di pulsanti (Documento, Modifica...): solo da leggere, le frecce lo saltano.</summary>
    public void Gruppo(string titolo) => Items.Add(new ToolStripLabel(titolo)
    {
        ForeColor = SystemColors.GrayText,
        Margin = new Padding(4, Items.Count == 0 ? 0 : 6, 0, 1),
        AccessibleRole = AccessibleRole.StaticText,
    });

    /// <summary>Tutti i pulsanti larghi quanto il più largo, così la colonna è ordinata.</summary>
    public void Allinea()
    {
        var pulsanti = Items.OfType<ToolStripButton>().ToList();
        foreach (var p in pulsanti)
            p.AutoSize = true;
        int larghezza = pulsanti.Max(p => p.GetPreferredSize(Size.Empty).Width);
        foreach (var p in pulsanti)
        {
            var altezza = p.GetPreferredSize(Size.Empty).Height;
            p.AutoSize = false;
            p.Size = new Size(larghezza, altezza);
        }
    }

    /// <summary>Testo più grande insieme allo zoom del documento, fino al doppio.</summary>
    public void Ingrandisci(float zoom)
    {
        Font = new Font("Segoe UI", 10.5f * Math.Clamp(zoom, 1f, 2f));
        Allinea();
    }

    protected override bool ProcessDialogKey(Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            Uscita?.Invoke();
            return true;
        }
        return base.ProcessDialogKey(keyData);
    }

    /// <summary>Disegno dei pulsanti: a pillola, pieni quando sono attivi (per esempio Grassetto acceso).</summary>
    private sealed class Disegno : ToolStripSystemRenderer
    {
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using var sfondo = new SolidBrush(SystemColors.Control);
            e.Graphics.FillRectangle(sfondo, e.AffectedBounds);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using var linea = new Pen(SystemColors.ControlDark);
            e.Graphics.DrawLine(linea, e.ToolStrip.Width - 1, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height); // bordo verso il testo
        }

        protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e)
        {
            var pulsante = (ToolStripButton)e.Item;
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var area = new Rectangle(1, 1, e.Item.Width - 3, e.Item.Height - 3);
            using var forma = Arrotondato(area, area.Height / 2);

            bool pieno = pulsante.Checked || pulsante.Pressed;
            var colore = pieno ? SystemColors.Highlight
                : pulsante.Selected ? Mescola(SystemColors.Window, SystemColors.Highlight, 0.18f)
                : SystemColors.Window;
            using (var sfondo = new SolidBrush(colore))
                g.FillPath(sfondo, forma);

            // Il pulsante raggiunto con la tastiera ha un bordo spesso, ben visibile.
            bool focus = pulsante.Selected && e.ToolStrip!.Focused;
            using var bordo = new Pen(focus ? SystemColors.Highlight : SystemColors.ControlDark, focus ? 3f : 1f);
            g.DrawPath(bordo, forma);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            if (e.Item is ToolStripButton { Checked: true } or ToolStripButton { Pressed: true })
                e.TextColor = SystemColors.HighlightText;
            else
                e.TextColor = e.Item.Enabled ? SystemColors.WindowText : SystemColors.GrayText;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            using var linea = new Pen(SystemColors.ControlDark);
            int x = e.Item.Width / 2;
            e.Graphics.DrawLine(linea, x, 4, x, e.Item.Height - 4);
        }

        private static GraphicsPath Arrotondato(Rectangle r, int raggio)
        {
            int d = Math.Max(2, Math.Min(raggio * 2, Math.Min(r.Width, r.Height)));
            var percorso = new GraphicsPath();
            percorso.AddArc(r.Left, r.Top, d, d, 180, 90);
            percorso.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            percorso.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            percorso.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            percorso.CloseFigure();
            return percorso;
        }

        private static Color Mescola(Color a, Color b, float quanto) => Color.FromArgb(
            (int)(a.R + (b.R - a.R) * quanto), (int)(a.G + (b.G - a.G) * quanto), (int)(a.B + (b.B - a.B) * quanto));
    }
}
