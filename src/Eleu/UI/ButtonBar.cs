using System.Drawing.Drawing2D;

namespace Eleu.UI;

/// <summary>
/// Button column on the left of the text, designed for low-vision mouse users: large buttons with the name
/// written (no icons to guess), rounded corners, system colors (so it honors Windows high
/// contrast) and a thick border on the button that has the focus.
/// Keyboard too: Ctrl+Tab from the text reaches it, the arrows move through the buttons, Enter or Space
/// press them, Esc goes back to the text. Each button tells the screen reader its name and shortcut.
/// </summary>
public sealed class ButtonBar : ToolStrip
{
    /// <summary>Esc pressed on the bar: the user wants to go back to the text.</summary>
    public event Action? Exited;

    public ButtonBar()
    {
        // In a column, all the same width; if the window is short a second column opens, none disappears.
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
        Renderer = new BarRenderer();
        Font = new Font("Segoe UI", 10.5f);
    }

    /// <summary>Button with its name written; the shortcut is in the tooltip and in the accessible description.</summary>
    public ToolStripButton Button(string text, string? shortcut, Action action, bool toggle = false)
    {
        var button = new ToolStripButton(text)
        {
            DisplayStyle = ToolStripItemDisplayStyle.Text,
            AutoToolTip = false,
            ToolTipText = shortcut is null ? text : $"{text} ({shortcut})",
            AccessibleName = text,
            AccessibleDescription = shortcut,
            AccessibleRole = toggle ? AccessibleRole.CheckButton : AccessibleRole.PushButton,
            Margin = new Padding(0, 1, 0, 1),
            Padding = new Padding(12, 2, 12, 2),
            TextAlign = ContentAlignment.MiddleLeft,
        };
        button.Click += (_, _) => action();
        Items.Add(button);
        return button;
    }

    /// <summary>Title of a group of buttons (Documento, Modifica...): read-only, the arrows skip it.</summary>
    public void Group(string heading) => Items.Add(new ToolStripLabel(heading)
    {
        ForeColor = SystemColors.GrayText,
        Margin = new Padding(4, Items.Count == 0 ? 0 : 6, 0, 1),
        AccessibleRole = AccessibleRole.StaticText,
    });

    /// <summary>All buttons as wide as the widest, so the column is tidy.</summary>
    public void Align()
    {
        var buttons = Items.OfType<ToolStripButton>().ToList();
        foreach (var p in buttons)
            p.AutoSize = true;
        int width = buttons.Max(p => p.GetPreferredSize(Size.Empty).Width);
        foreach (var p in buttons)
        {
            var height = p.GetPreferredSize(Size.Empty).Height;
            p.AutoSize = false;
            p.Size = new Size(width, height);
        }
    }

    /// <summary>Larger text along with the document zoom, up to double.</summary>
    public void Enlarge(float zoom)
    {
        Font = new Font("Segoe UI", 10.5f * Math.Clamp(zoom, 1f, 2f));
        Align();
    }

    protected override bool ProcessDialogKey(Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            Exited?.Invoke();
            return true;
        }
        return base.ProcessDialogKey(keyData);
    }

    /// <summary>Button rendering: pill-shaped, filled when active (for example Bold on).</summary>
    private sealed class BarRenderer : ToolStripSystemRenderer
    {
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using var background = new SolidBrush(SystemColors.Control);
            e.Graphics.FillRectangle(background, e.AffectedBounds);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using var line = new Pen(SystemColors.ControlDark);
            e.Graphics.DrawLine(line, e.ToolStrip.Width - 1, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height); // border toward the text
        }

        protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e)
        {
            var button = (ToolStripButton)e.Item;
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var area = new Rectangle(1, 1, e.Item.Width - 3, e.Item.Height - 3);
            using var shape = Rounded(area, area.Height / 2);

            bool full = button.Checked || button.Pressed;
            var color = full ? SystemColors.Highlight
                : button.Selected ? Mix(SystemColors.Window, SystemColors.Highlight, 0.18f)
                : SystemColors.Window;
            using (var background = new SolidBrush(color))
                g.FillPath(background, shape);

            // The button reached with the keyboard has a thick, clearly visible border.
            bool focus = button.Selected && e.ToolStrip!.Focused;
            using var border = new Pen(focus ? SystemColors.Highlight : SystemColors.ControlDark, focus ? 3f : 1f);
            g.DrawPath(border, shape);
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
            using var line = new Pen(SystemColors.ControlDark);
            int x = e.Item.Width / 2;
            e.Graphics.DrawLine(line, x, 4, x, e.Item.Height - 4);
        }

        private static GraphicsPath Rounded(Rectangle r, int radius)
        {
            int d = Math.Max(2, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));
            var path = new GraphicsPath();
            path.AddArc(r.Left, r.Top, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static Color Mix(Color a, Color b, float howMuch) => Color.FromArgb(
            (int)(a.R + (b.R - a.R) * howMuch), (int)(a.G + (b.G - a.G) * howMuch), (int)(a.B + (b.B - a.B) * howMuch));
    }
}
