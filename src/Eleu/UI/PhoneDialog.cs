using QRCoder;

namespace Eleu.UI;

/// <summary>
/// File > Foto dal telefono: the QR code to frame with the phone (and the same address written, for the screen reader
/// and for typing it), and how many photos have arrived. "Riconosci il testo" goes on when there is at least one.
/// </summary>
public sealed class PhoneDialog : Form
{
    private readonly Label _count;
    private readonly Button _recognize;
    private readonly PictureBox _code;
    private int _photos;

    /// <param name="publicNetwork">Windows treats the network as public: the firewall may block the phone, say how to fix it.</param>
    public PhoneDialog(string url, bool publicNetwork = false)
    {
        Text = "Foto dal telefono";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Font = new Font("Segoe UI", 11f);
        Padding = new Padding(12);

        var panel = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Dock = DockStyle.Fill };
        var steps = new Label
        {
            Text = "1. Collega il telefono alla stessa rete Wi-Fi del computer.\n"
                + "2. Inquadra il codice con la fotocamera del telefono, oppure scrivi l'indirizzo qui sotto nel browser del telefono.\n"
                + "3. Sul telefono premi \"Fotografa una pagina\". Puoi mandare più pagine, una dopo l'altra.\n"
                + "La prima volta Windows può chiedere se Eleu può usare la rete: scegli Consenti, per le reti private.",
            AutoSize = true,
            MaximumSize = new Size(560, 0),
        };
        _code = new PictureBox { Size = new Size(300, 300), SizeMode = PictureBoxSizeMode.Zoom, AccessibleName = "Codice QR per il telefono" };
        using (var data = new QRCodeGenerator().CreateQrCode(url, QRCodeGenerator.ECCLevel.M))
        using (var png = new MemoryStream(new PngByteQRCode(data).GetGraphic(12)))
            _code.Image = Image.FromStream(png);
        var address = new TextBox { Text = url, ReadOnly = true, Width = 560, AccessibleName = "Indirizzo per il telefono" };
        _count = new Label { Text = "Nessuna foto ricevuta.", AutoSize = true, Margin = new Padding(3, 8, 3, 3) };

        var buttons = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 12, 0, 0) };
        _recognize = new Button { Text = "&Riconosci il testo", AutoSize = true, DialogResult = DialogResult.OK, Enabled = false };
        var cancel = new Button { Text = "Annulla", AutoSize = true, DialogResult = DialogResult.Cancel };
        buttons.Controls.AddRange([_recognize, cancel]);

        panel.Controls.Add(steps);
        if (publicNetwork)
            panel.Controls.Add(new Label
            {
                Text = "Attenzione: Windows considera pubblica la rete di questo computer, e di solito su una rete pubblica "
                    + "blocca il telefono. Se la pagina non si apre sul telefono, e questa è la rete di casa, impostala come privata: "
                    + "Impostazioni di Windows, Rete e Internet, la tua rete (Wi-Fi o Ethernet), Tipo di profilo di rete: Privata.",
                AutoSize = true,
                MaximumSize = new Size(560, 0),
                Margin = new Padding(3, 8, 3, 3),
                Font = new Font(Font, FontStyle.Bold),
            });
        panel.Controls.AddRange([_code, new Label { Text = "&Indirizzo:", AutoSize = true }, address, _count, buttons]);
        Controls.Add(panel);
        AcceptButton = _recognize;
        CancelButton = cancel;
        ActiveControl = address;
    }

    public int Photos => _photos;

    /// <summary>One more photo arrived (call on the UI thread).</summary>
    public void PhotoArrived()
    {
        _photos++;
        _count.Text = _photos == 1 ? "1 foto ricevuta." : $"{_photos} foto ricevute.";
        _recognize.Enabled = true;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _code.Image?.Dispose();
        base.Dispose(disposing);
    }
}
