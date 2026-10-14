using System.Runtime.InteropServices;
using Eleu.Braille;
using Eleu.Documents;
using Eleu.Editor;
using Eleu.UI;

namespace Eleu;

// Button column on the left and ruler, for sighted mouse users; Copy Braille.
public sealed partial class MainWindow
{
    private ButtonBar _bar = null!;
    private Ruler _ruler = null!;
    private ToolStripButton _bold = null!, _italic = null!, _underline = null!;
    private readonly Dictionary<Alignment, ToolStripButton> _alignments = [];

    private ButtonBar CreateBar()
    {
        var bar = new ButtonBar { Visible = _settings.Toolbar };
        // The command runs after the click and the text takes the focus back: you keep typing where you were.
        Action Command(Action action) => () => BeginInvoke(() => { _text.Focus(); RunCommand(action); });

        bar.Group("Documento");
        bar.Button("Nuovo", "Ctrl+N", Command(New));
        bar.Button("Apri", "Ctrl+O", Command(Open));
        bar.Button("Salva", "Ctrl+S", Command(() => Save()));
        bar.Button("Stampa", "Ctrl+P", Command(Print));
        bar.Button("Stampa braille", "Ctrl+Alt+P", Command(() => Braille(toFile: false)));
        bar.Group("Modifica");
        bar.Button("Taglia", "Ctrl+X", Command(Cut));
        bar.Button("Copia", "Ctrl+C", Command(Copy));
        bar.Button("Copia braille", null, Command(CopyBraille));
        bar.Button("Incolla", "Ctrl+V", Command(Paste));
        bar.Group("Carattere");
        bar.Button("Carattere...", "Ctrl+Maiusc+F", Command(OpenCharacter));
        _bold = bar.Button("Grassetto", "Ctrl+B", Command(() => ToggleEffect(NativeRichEdit.Effect.Bold, "Grassetto")), toggle: true);
        _italic = bar.Button("Corsivo", "Ctrl+I", Command(() => ToggleEffect(NativeRichEdit.Effect.Italic, "Corsivo")), toggle: true);
        _underline = bar.Button("Sottolineato", "Ctrl+U", Command(() => ToggleEffect(NativeRichEdit.Effect.Underline, "Sottolineato")), toggle: true);
        bar.Button("Rimpicciolisci", "Maiusc+Ctrl+-", Command(() => ChangeSize(-1)));
        bar.Button("Ingrandisci", "Maiusc+Ctrl++", Command(() => ChangeSize(1)));
        bar.Group("Paragrafo");
        bar.Button("Paragrafo...", "Ctrl+Maiusc+P", Command(OpenParagraph));
        _alignments[Alignment.Left] = bar.Button("Sinistra", "Ctrl+L", Command(() => Align(Alignment.Left, "Allineato a sinistra")), toggle: true);
        _alignments[Alignment.Center] = bar.Button("Centra", "Ctrl+E", Command(() => Align(Alignment.Center, "Centrato")), toggle: true);
        _alignments[Alignment.Right] = bar.Button("Destra", "Ctrl+R", Command(() => Align(Alignment.Right, "Allineato a destra")), toggle: true);
        _alignments[Alignment.Justified] = bar.Button("Giustifica", "Ctrl+J", Command(() => Align(Alignment.Justified, "Giustificato")), toggle: true);

        bar.Align();
        bar.Exited += () => _text.Focus();
        return bar;
    }

    private Ruler CreateRuler()
    {
        var ruler = new Ruler { Visible = _settings.Ruler };
        ruler.IndentsChanged += (left, firstLine, right) =>
        {
            NativeRichEdit.SetParagraphFormat(_text, new ParagraphFormat(
                LeftIndent: left, FirstLineIndent: firstLine, RightIndent: right));
            _text.Focus();
            UpdateBars();
            _announcer.Announce($"Rientri: sinistro {Cm(left)}, prima riga {Cm(firstLine)}, destro {Cm(right)}");
        };
        return ruler;

        static string Cm(float points) => $"{points * 2.54f / 72f:0.##} cm";
    }

    private void ToggleToolbar()
    {
        _settings.Toolbar = !_settings.Toolbar;
        _bar.Visible = _settings.Toolbar;
        _announcer.Announce(_settings.Toolbar ? "Barra degli strumenti visibile" : "Barra degli strumenti nascosta");
    }

    private void ToggleRuler()
    {
        _settings.Ruler = !_settings.Ruler;
        _ruler.Visible = _settings.Ruler;
        BeginInvoke(UpdateBars);
        _announcer.Announce(_settings.Ruler ? "Righello visibile" : "Righello nascosto");
    }

    /// <summary>Buttons lit according to the text at the cursor (Bold, alignment) and indents on the ruler.</summary>
    private void UpdateBars()
    {
        if (!_text.IsHandleCreated)
            return;
        if (_bar.Visible)
        {
            _bold.Checked = NativeRichEdit.EffectState(_text, NativeRichEdit.Effect.Bold) == true;
            _italic.Checked = NativeRichEdit.EffectState(_text, NativeRichEdit.Effect.Italic) == true;
            _underline.Checked = NativeRichEdit.EffectState(_text, NativeRichEdit.Effect.Underline) == true;
            var alignment = NativeRichEdit.ReadAlignment(_text);
            foreach (var (kind, button) in _alignments)
                button.Checked = kind == alignment;
        }
        if (_ruler.Visible)
        {
            // The text area says where zero is and where lines wrap; horizontal scrolling is taken into account.
            SendMessage(_text.Handle, EmGetRect, 0, out Rectangle area);
            SendMessage(_text.Handle, EmGetScrollPos, 0, out Point scrolling);
            var paragraph = NativeRichEdit.ReadParagraphFormat(_text);
            _ruler.Set(
                area.Left - scrolling.X,
                _text.WordWrap ? area.Right : null,
                DeviceDpi / 72f * _settings.Zoom,
                paragraph.LeftIndent ?? 0,
                paragraph.FirstLineIndent ?? 0,
                paragraph.RightIndent ?? 0);
        }
    }

    /// <summary>The selection translated into Braille (Unicode Braille characters), to paste elsewhere.</summary>
    private void CopyBraille()
    {
        if (_text.SelectionLength == 0)
        {
            _announcer.Announce("Nessun testo selezionato");
            return;
        }
        try
        {
            var rows = _text.SelectedText.Replace("\r", "").Split('\n', Pagination.PageBreak);
            Clipboard.SetText(string.Join(Environment.NewLine, rows.Select(BrailleTranslator.Translate)));
            _announcer.Announce("Copiato in Braille");
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or InvalidOperationException)
        {
            _announcer.Announce("Braille non disponibile");
        }
    }

    /// <summary>A bit of space between the window edge and the text: easier to read and the ruler has room.</summary>
    private void SetTextMargins()
    {
        int margin = LogicalToDeviceUnits(16);
        SendMessage(_text.Handle, EmSetMargins, EcLeftMargin | EcRightMargin, margin | (margin << 16));
    }

    private const int EmSetMargins = 0x00D3, EcLeftMargin = 1, EcRightMargin = 2;
    private const int EmGetRect = 0x00B2;
    private const int EmGetScrollPos = 0x0400 + 221;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rectangle { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X, Y; }

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr w, out Rectangle l);

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr w, out Point l);

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr w, IntPtr l);
}
