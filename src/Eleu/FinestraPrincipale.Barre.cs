using System.Runtime.InteropServices;
using Eleu.Braille;
using Eleu.Documento;
using Eleu.Editor;
using Eleu.Interfaccia;

namespace Eleu;

// Colonna dei pulsanti a sinistra e righello, per chi vede e usa il mouse; Copia braille.
public sealed partial class FinestraPrincipale
{
    private BarraPulsanti _barra = null!;
    private Righello _righello = null!;
    private ToolStripButton _grassetto = null!, _corsivo = null!, _sottolineato = null!;
    private readonly Dictionary<Allineamento, ToolStripButton> _allineamenti = [];

    private BarraPulsanti CreaBarra()
    {
        var barra = new BarraPulsanti { Visible = _impostazioni.BarraStrumenti };
        // Il comando parte dopo il clic e il testo riprende il focus: si continua a scrivere dov'eri.
        Action Comando(Action azione) => () => BeginInvoke(() => { _testo.Focus(); azione(); });

        barra.Gruppo("Documento");
        barra.Pulsante("Nuovo", "Ctrl+N", Comando(Nuovo));
        barra.Pulsante("Apri", "Ctrl+O", Comando(Apri));
        barra.Pulsante("Salva", "Ctrl+S", Comando(() => Salva()));
        barra.Pulsante("Stampa", "Ctrl+P", Comando(Stampa));
        barra.Pulsante("Stampa braille", "Ctrl+Alt+P", Comando(() => Braille(suFile: false)));
        barra.Gruppo("Modifica");
        barra.Pulsante("Taglia", "Ctrl+X", Comando(Taglia));
        barra.Pulsante("Copia", "Ctrl+C", Comando(Copia));
        barra.Pulsante("Copia braille", null, Comando(CopiaBraille));
        barra.Pulsante("Incolla", "Ctrl+V", Comando(Incolla));
        barra.Gruppo("Carattere");
        barra.Pulsante("Carattere...", "Ctrl+Maiusc+F", Comando(ApriCarattere));
        _grassetto = barra.Pulsante("Grassetto", "Ctrl+B", Comando(() => CambiaEffetto(RichEditNativo.Effetto.Grassetto, "Grassetto")), interruttore: true);
        _corsivo = barra.Pulsante("Corsivo", "Ctrl+I", Comando(() => CambiaEffetto(RichEditNativo.Effetto.Corsivo, "Corsivo")), interruttore: true);
        _sottolineato = barra.Pulsante("Sottolineato", "Ctrl+U", Comando(() => CambiaEffetto(RichEditNativo.Effetto.Sottolineato, "Sottolineato")), interruttore: true);
        barra.Pulsante("Rimpicciolisci", "Maiusc+Ctrl+-", Comando(() => CambiaDimensione(-1)));
        barra.Pulsante("Ingrandisci", "Maiusc+Ctrl++", Comando(() => CambiaDimensione(1)));
        barra.Gruppo("Paragrafo");
        barra.Pulsante("Paragrafo...", "Ctrl+Maiusc+P", Comando(ApriParagrafo));
        _allineamenti[Allineamento.Sinistra] = barra.Pulsante("Sinistra", "Ctrl+L", Comando(() => Allinea(Allineamento.Sinistra, "Allineato a sinistra")), interruttore: true);
        _allineamenti[Allineamento.Centro] = barra.Pulsante("Centra", "Ctrl+E", Comando(() => Allinea(Allineamento.Centro, "Centrato")), interruttore: true);
        _allineamenti[Allineamento.Destra] = barra.Pulsante("Destra", "Ctrl+R", Comando(() => Allinea(Allineamento.Destra, "Allineato a destra")), interruttore: true);
        _allineamenti[Allineamento.Giustificato] = barra.Pulsante("Giustifica", "Ctrl+J", Comando(() => Allinea(Allineamento.Giustificato, "Giustificato")), interruttore: true);

        barra.Allinea();
        barra.Uscita += () => _testo.Focus();
        return barra;
    }

    private Righello CreaRighello()
    {
        var righello = new Righello { Visible = _impostazioni.Righello };
        righello.RientriCambiati += (sinistro, primaRiga, destro) =>
        {
            RichEditNativo.ImpostaFormatoParagrafo(_testo, new FormatoParagrafo(
                RientroSinistro: sinistro, RientroPrimaRiga: primaRiga, RientroDestro: destro));
            _testo.Focus();
            AggiornaBarre();
            _annunciatore.Annuncia($"Rientri: sinistro {Cm(sinistro)}, prima riga {Cm(primaRiga)}, destro {Cm(destro)}");
        };
        return righello;

        static string Cm(float punti) => $"{punti * 2.54f / 72f:0.##} cm";
    }

    private void CambiaBarraStrumenti()
    {
        _impostazioni.BarraStrumenti = !_impostazioni.BarraStrumenti;
        _barra.Visible = _impostazioni.BarraStrumenti;
        _annunciatore.Annuncia(_impostazioni.BarraStrumenti ? "Barra degli strumenti visibile" : "Barra degli strumenti nascosta");
    }

    private void CambiaRighello()
    {
        _impostazioni.Righello = !_impostazioni.Righello;
        _righello.Visible = _impostazioni.Righello;
        BeginInvoke(AggiornaBarre);
        _annunciatore.Annuncia(_impostazioni.Righello ? "Righello visibile" : "Righello nascosto");
    }

    /// <summary>Pulsanti accesi secondo il testo dove si è (Grassetto, allineamento) e rientri sul righello.</summary>
    private void AggiornaBarre()
    {
        if (!_testo.IsHandleCreated)
            return;
        if (_barra.Visible)
        {
            _grassetto.Checked = RichEditNativo.StatoEffetto(_testo, RichEditNativo.Effetto.Grassetto) == true;
            _corsivo.Checked = RichEditNativo.StatoEffetto(_testo, RichEditNativo.Effetto.Corsivo) == true;
            _sottolineato.Checked = RichEditNativo.StatoEffetto(_testo, RichEditNativo.Effetto.Sottolineato) == true;
            var allineamento = RichEditNativo.LeggiAllineamento(_testo);
            foreach (var (tipo, pulsante) in _allineamenti)
                pulsante.Checked = tipo == allineamento;
        }
        if (_righello.Visible)
        {
            // L'area del testo dice dove sta lo zero e dove si va a capo; si tiene conto dello scorrimento orizzontale.
            SendMessage(_testo.Handle, EmGetRect, 0, out Rettangolo area);
            SendMessage(_testo.Handle, EmGetScrollPos, 0, out Punto scorrimento);
            var paragrafo = RichEditNativo.LeggiFormatoParagrafo(_testo);
            _righello.Imposta(
                area.Sinistra - scorrimento.X,
                _testo.WordWrap ? area.Destra : null,
                DeviceDpi / 72f * _impostazioni.Zoom,
                paragrafo.RientroSinistro ?? 0,
                paragrafo.RientroPrimaRiga ?? 0,
                paragrafo.RientroDestro ?? 0);
        }
    }

    /// <summary>La selezione tradotta in Braille (caratteri Braille Unicode), da incollare altrove.</summary>
    private void CopiaBraille()
    {
        if (_testo.SelectionLength == 0)
        {
            _annunciatore.Annuncia("Nessun testo selezionato");
            return;
        }
        try
        {
            var righe = _testo.SelectedText.Replace("\r", "").Split('\n', Paginatore.SaltoPagina);
            Clipboard.SetText(string.Join(Environment.NewLine, righe.Select(TraduttoreBraille.Traduci)));
            _annunciatore.Annuncia("Copiato in Braille");
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or InvalidOperationException)
        {
            _annunciatore.Annuncia("Braille non disponibile");
        }
    }

    /// <summary>Un po' di spazio tra il bordo della finestra e il testo: si legge meglio e il righello ha dove stare.</summary>
    private void ImpostaMarginiTesto()
    {
        int margine = LogicalToDeviceUnits(16);
        SendMessage(_testo.Handle, EmSetMargins, EcLeftMargin | EcRightMargin, margine | (margine << 16));
    }

    private const int EmSetMargins = 0x00D3, EcLeftMargin = 1, EcRightMargin = 2;
    private const int EmGetRect = 0x00B2;
    private const int EmGetScrollPos = 0x0400 + 221;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rettangolo { public int Sinistra, Alto, Destra, Basso; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Punto { public int X, Y; }

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern IntPtr SendMessage(IntPtr finestra, int messaggio, IntPtr w, out Rettangolo l);

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern IntPtr SendMessage(IntPtr finestra, int messaggio, IntPtr w, out Punto l);

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern IntPtr SendMessage(IntPtr finestra, int messaggio, IntPtr w, IntPtr l);
}
