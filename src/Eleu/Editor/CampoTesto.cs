using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace Eleu.Editor;

/// <summary>
/// Il campo di testo del documento.
/// <para>
/// Windows Forms, quando un RichTextBox riceve il focus, manda agli screen reader una notifica con
/// <em>tutto</em> il testo del campo (pensata per l'Assistente vocale). Con NVDA questo significa
/// sentirsi leggere l'intero documento ogni volta che si torna al testo, per esempio chiudendo Trova.
/// La notifica legge la proprietà <see cref="Text"/>: durante la presa del focus la si fa risultare vuota,
/// così la notifica non dice nulla e lo screen reader legge solo la riga corrente, come in ogni editor.
/// </para>
/// <para>
/// <see cref="Text"/> è il testo "grezzo", un carattere per ogni posizione del campo: così ogni indice nel
/// testo è anche una posizione valida per Select. Il testo normale di Windows nasconde i caratteri che
/// reggono le tabelle e con una tabella le posizioni non tornerebbero più. Gli a capo sono '\n'; in tabella
/// ogni riga inizia con <see cref="InizioRiga"/> e finisce con <see cref="FineRiga"/>, ciascuno seguito da
/// un a capo, e ogni cella finisce con <see cref="FineCella"/>.
/// </para>
/// </summary>
public class CampoTesto : RichTextBox
{
    public const char InizioRiga = Documento.StrutturaTesto.InizioRiga;
    public const char FineRiga = Documento.StrutturaTesto.FineRiga;
    public const char FineCella = '\a';

    private bool _presaFocusInCorso;

    [AllowNull]
    public override string Text
    {
        get => _presaFocusInCorso ? "" : IsHandleCreated ? TestoGrezzo() : base.Text;
        set => base.Text = value;
    }

    private string TestoGrezzo()
    {
        var lunghezza = new GetTextLengthEx { Flags = GtlPrecise | GtlNumChars, Codepage = Utf16 };
        int caratteri = (int)SendMessage(Handle, EmGetTextLengthEx, ref lunghezza, IntPtr.Zero);
        if (caratteri <= 0)
            return "";
        int byteBuffer = (caratteri + 1) * 2;
        var richiesta = new GetTextEx { Cb = byteBuffer, Flags = GtRawText, Codepage = Utf16 };
        var buffer = Marshal.AllocHGlobal(byteBuffer);
        try
        {
            int letti = (int)SendMessage(Handle, EmGetTextEx, ref richiesta, buffer);
            return Marshal.PtrToStringUni(buffer, letti).Replace('\r', '\n');
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    protected override void OnGotFocus(EventArgs e)
    {
        _presaFocusInCorso = true;
        try
        {
            base.OnGotFocus(e);
        }
        finally
        {
            _presaFocusInCorso = false;
        }
    }

    /// <summary>
    /// Il menu contestuale: con il mouse dove si è cliccato, con il tasto Applicazioni o Maiusc+F10 accanto al
    /// cursore (Windows Forms lo aprirebbe nell'angolo del campo, lontano da dove si sta scrivendo).
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
            var punto = GetPositionFromCharIndex(SelectionStart);
            punto.Y += Math.Max(Font.Height, 16);
            if (!ClientRectangle.Contains(punto))
                punto = new Point(ClientSize.Width / 4, ClientSize.Height / 4);
            menu.Show(this, punto);
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
    private static extern IntPtr SendMessage(IntPtr finestra, int messaggio, ref GetTextEx w, IntPtr l);

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern IntPtr SendMessage(IntPtr finestra, int messaggio, ref GetTextLengthEx w, IntPtr l);
}
