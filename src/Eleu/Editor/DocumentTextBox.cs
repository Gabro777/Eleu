using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace Eleu.Editor;

/// <summary>
/// The document text box.
/// <para>
/// When a RichTextBox gets the focus, Windows Forms sends screen readers a notification with
/// <em>all</em> the text of the box (meant for Narrator). With NVDA this means
/// hearing the whole document every time you return to the text, for example when closing Find.
/// The notification reads the <see cref="Text"/> property: while taking the focus it is made to look empty,
/// so the notification says nothing and the screen reader reads only the current line, as in any editor.
/// </para>
/// <para>
/// <see cref="Text"/> is the "raw" text, one character per position of the box: so every index in the
/// text is also a valid position for Select. The normal Windows text hides the characters that
/// hold tables together and with a table the positions would no longer match. Line breaks are '\n'; in a table
/// each row starts with <see cref="RowStart"/> and ends with <see cref="RowEnd"/>, each followed by
/// a line break, and each cell ends with <see cref="CellEnd"/>.
/// </para>
/// </summary>
public class DocumentTextBox : RichTextBox
{
    public const char RowStart = Documents.TextStructure.RowStart;
    public const char RowEnd = Documents.TextStructure.RowEnd;
    public const char CellEnd = '\a';

    private bool _gettingFocus;

    [AllowNull]
    public override string Text
    {
        get => _gettingFocus ? "" : IsHandleCreated ? RawText() : base.Text;
        set => base.Text = value;
    }

    private string RawText()
    {
        var length = new GetTextLengthEx { Flags = GtlPrecise | GtlNumChars, Codepage = Utf16 };
        int characters = (int)SendMessage(Handle, EmGetTextLengthEx, ref length, IntPtr.Zero);
        if (characters <= 0)
            return "";
        int bufferBytes = (characters + 1) * 2;
        var request = new GetTextEx { Cb = bufferBytes, Flags = GtRawText, Codepage = Utf16 };
        var buffer = Marshal.AllocHGlobal(bufferBytes);
        try
        {
            int read = (int)SendMessage(Handle, EmGetTextEx, ref request, buffer);
            return Marshal.PtrToStringUni(buffer, read).Replace('\r', '\n');
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    protected override void OnGotFocus(EventArgs e)
    {
        _gettingFocus = true;
        try
        {
            base.OnGotFocus(e);
        }
        finally
        {
            _gettingFocus = false;
        }
    }

    /// <summary>
    /// The context menu: with the mouse where you clicked, with the Applications key or Shift+F10 next to the
    /// cursor (Windows Forms would open it in the corner of the box, far from where you are typing).
    /// </summary>
    protected override void WndProc(ref Message m)
    {
        const int WmContextMenu = 0x007B, WmPaint = 0x000F;
        if (m.Msg == WmPaint)
        {
            base.WndProc(ref m);
            PaintReadingWord();
            return;
        }
        if (m.Msg != WmContextMenu || ContextMenuStrip is not { } menu)
        {
            base.WndProc(ref m);
            return;
        }
        long l = m.LParam.ToInt64();
        if (l == -1 || l == 0xFFFFFFFF)
        {
            var point = GetPositionFromCharIndex(SelectionStart);
            point.Y += Math.Max(Font.Height, 16);
            if (!ClientRectangle.Contains(point))
                point = new Point(ClientSize.Width / 4, ClientSize.Height / 4);
            menu.Show(this, point);
        }
        else
            menu.Show(new Point((short)(l & 0xFFFF), (short)((l >> 16) & 0xFFFF)));
    }

    // ---------- The word being read aloud ----------

    private (int Start, int End)? _readingWord;
    private dynamic? _tom;

    /// <summary>
    /// The word being read aloud, highlighted for low-vision and dyslexic readers: a translucent band drawn over the
    /// text, so neither the document nor the cursor changes (the screen reader is not disturbed). Null = none.
    /// </summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public (int Start, int End)? ReadingWord
    {
        get => _readingWord;
        set
        {
            if (value == _readingWord)
                return;
            var old = WordRectangle(_readingWord);
            _readingWord = value;
            if (old is { } o)
                Invalidate(Rectangle.Inflate(o, 4, 4));
            if (WordRectangle(_readingWord) is { } n)
                Invalidate(Rectangle.Inflate(n, 4, 4));
        }
    }

    /// <summary>Scrolls the text, without moving the cursor, so that the word being read is on screen.</summary>
    public void ShowReadingWord()
    {
        if (_readingWord is not { } word || !IsHandleCreated)
            return;
        const int EmGetFirstVisibleLine = 0x00CE, EmLineFromChar = 0x00C9, EmLineScroll = 0x00B6;
        var area = WordRectangle(word);
        if (area is { } r && r.Top >= 0 && r.Bottom <= ClientSize.Height)
            return;
        int first = (int)SendMessage(Handle, EmGetFirstVisibleLine, IntPtr.Zero, IntPtr.Zero);
        int line = (int)SendMessage(Handle, EmLineFromChar, word.Start, IntPtr.Zero);
        // The word goes on the second line from the top: what comes next is on screen.
        SendMessage(Handle, EmLineScroll, IntPtr.Zero, (IntPtr)(line - first - 1));
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        _tom = null;
        base.OnHandleCreated(e);
    }

    private Rectangle? WordRectangle((int Start, int End)? word)
    {
        if (word is not var (start, end) || end <= start || !IsHandleCreated)
            return null;
        try
        {
            _tom ??= NativeRichEdit.TomDocument(this);
            if (_tom is null)
                return null;
            const int TomStart = 32, TaBottom = 8, TaRight = 2;
            var range = _tom.Range(start, end);
            int left, top, right, bottom;
            range.GetPoint(TomStart, out left, out top);
            range.GetPoint(TaBottom | TaRight, out right, out bottom);
            var topLeft = PointToClient(new Point(left, top));
            var bottomRight = PointToClient(new Point(right, bottom));
            if (bottomRight.X <= topLeft.X || bottomRight.Y <= topLeft.Y)
                return null; // the word wraps onto two lines: no band
            return Rectangle.FromLTRB(topLeft.X, topLeft.Y, bottomRight.X, bottomRight.Y);
        }
        catch (Exception e) when (e is COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException or ArgumentException)
        {
            return null; // the word is off screen
        }
    }

    private void PaintReadingWord()
    {
        if (WordRectangle(_readingWord) is not { } area)
            return;
        using var g = Graphics.FromHwnd(Handle);
        area.Inflate(2, 1);
        using var band = new SolidBrush(Color.FromArgb(110, SystemColors.Highlight));
        using var frame = new Pen(SystemColors.Highlight, 2);
        g.FillRectangle(band, area);
        g.DrawRectangle(frame, area);
    }

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, IntPtr lParam);

    private const int EmGetTextEx = 0x0400 + 94;
    private const int EmGetTextLengthEx = 0x0400 + 95;
    private const int GtRawText = 4;
    private const int GtlPrecise = 2, GtlNumChars = 8;
    private const int Utf16 = 1200;

    [StructLayout(LayoutKind.Sequential)]
    private struct GetTextEx
    {
        public int Cb;
        public int Flags;
        public int Codepage;
        public IntPtr DefaultChar;
        public IntPtr UsedDefChar;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GetTextLengthEx
    {
        public int Flags;
        public int Codepage;
    }

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern IntPtr SendMessage(IntPtr window, int message, ref GetTextEx w, IntPtr l);

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern IntPtr SendMessage(IntPtr window, int message, ref GetTextLengthEx w, IntPtr l);
}
