namespace Eleu.UI;

/// <summary>
/// Ruler above the text, for sighted users: centimeters and the current paragraph's indents, movable with the mouse.
/// It does not take the focus: keyboard users set the same indents from Formato > Paragrafo.
/// Indents are in points, as in <see cref="Editor.ParagraphFormat"/>.
/// </summary>
public sealed class Ruler : Control
{
    private const float PointsPerCm = 72f / 2.54f;
    private const int Side = 5; // half the base of the small triangles

    private enum Marker { None, FirstLine, Left, Right }

    private Marker _dragged;
    private float _left, _firstLine, _right;

    /// <summary>X coordinate, in ruler pixels, where the text without indents starts.</summary>
    public int Origin { get; private set; }

    /// <summary>X coordinate where the text wraps without a right indent; null if the text does not wrap by itself.</summary>
    public int? End { get; private set; }

    /// <summary>Pixels per point, zoom included.</summary>
    public float ScaleFactor { get; private set; } = 96f / 72f;

    /// <summary>Indents changed with the mouse: left, first line (relative to the left one), right, in points.</summary>
    public event Action<float, float, float>? IndentsChanged;

    public Ruler()
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

    public void Set(int source, int? end, float scale, float left, float firstLine, float right)
    {
        if (_dragged != Marker.None)
            return; // while dragging the mouse is in charge
        if (source == Origin && end == End && scale == ScaleFactor && left == _left && firstLine == _firstLine && right == _right)
            return;
        (Origin, End, ScaleFactor, _left, _firstLine, _right) = (source, end, scale, left, firstLine, right);
        AccessibleDescription = $"Rientro sinistro {Cm(left)}, prima riga {Cm(firstLine)}, destro {Cm(right)}";
        Invalidate();
    }

    private static string Cm(float points) => $"{points / PointsPerCm:0.##} cm";

    private int X(float points) => Origin + (int)Math.Round(points * ScaleFactor);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var text = SystemColors.ControlText;
        var white = SystemColors.Window;
        g.Clear(SystemColors.Control);

        // White band: the area where the text is.
        int end = End ?? Width;
        int top = 6, bottom = Height - 8;
        using (var background = new SolidBrush(white))
            g.FillRectangle(background, Origin, top, Math.Max(0, end - Origin), bottom - top);

        // Ticks every quarter centimeter, numbers every centimeter.
        using var pen = new Pen(text);
        using var brush = new SolidBrush(text);
        float cm = PointsPerCm * ScaleFactor;
        int center = (top + bottom) / 2;
        for (int quarter = 1; Origin + quarter * cm / 4 < Width; quarter++)
        {
            int x = Origin + (int)Math.Round(quarter * cm / 4);
            if (quarter % 4 == 0)
            {
                var number = (quarter / 4).ToString();
                var measure = g.MeasureString(number, Font);
                g.DrawString(number, Font, brush, x - measure.Width / 2, center - measure.Height / 2);
            }
            else
            {
                int length = quarter % 2 == 0 ? 3 : 1;
                g.DrawLine(pen, x, center - length, x, center + length);
            }
        }

        // Indents: first line at the top, left and right at the bottom.
        using var marker = new SolidBrush(SystemColors.Highlight);
        Triangle(g, marker, X(_left + _firstLine), top, down: true);
        Triangle(g, marker, X(_left), bottom, down: false);
        if (End is int right)
            Triangle(g, marker, right - (int)Math.Round(_right * ScaleFactor), bottom, down: false);
    }

    private static void Triangle(Graphics g, Brush brush, int x, int y, bool down)
    {
        int tip = down ? y + Side + 1 : y - Side - 1;
        int baseY = down ? y - 2 : y + 2;
        g.FillPolygon(brush, [new Point(x - Side, baseY), new Point(x + Side, baseY), new Point(x, tip)]);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left)
            return;
        bool top = e.Y < Height / 2;
        var nearby = new List<(Marker Marker, int X)>();
        if (top)
            nearby.Add((Marker.FirstLine, X(_left + _firstLine)));
        else
        {
            nearby.Add((Marker.Left, X(_left)));
            if (End is int right)
                nearby.Add((Marker.Right, right - (int)Math.Round(_right * ScaleFactor)));
        }
        var taken = nearby.Where(v => Math.Abs(v.X - e.X) <= Side + 3).OrderBy(v => Math.Abs(v.X - e.X)).FirstOrDefault();
        _dragged = taken.Marker;
        if (_dragged != Marker.None)
            Capture = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragged == Marker.None)
            return;
        // Moves in steps of a quarter centimeter, like the ticks.
        float points = (float)Math.Round((e.X - Origin) / ScaleFactor / (PointsPerCm / 4)) * (PointsPerCm / 4);
        switch (_dragged)
        {
            case Marker.FirstLine:
                _firstLine = Math.Max(points, 0) - _left;
                break;
            case Marker.Left:
                _left = Math.Max(points, 0); // the first line follows: the whole paragraph moves
                _firstLine = Math.Max(_firstLine, -_left);
                break;
            case Marker.Right when End is int right:
                float fromEdge = (float)Math.Round((right - e.X) / ScaleFactor / (PointsPerCm / 4)) * (PointsPerCm / 4);
                _right = Math.Max(fromEdge, 0);
                break;
        }
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_dragged == Marker.None)
            return;
        _dragged = Marker.None;
        Capture = false;
        IndentsChanged?.Invoke(_left, _firstLine, _right);
    }
}
