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
        const int WmContextMenu = 0x007B;
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
