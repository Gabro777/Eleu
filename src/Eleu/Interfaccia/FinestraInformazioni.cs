using System.Diagnostics;

namespace Eleu.Interfaccia;

/// <summary>
/// Aiuto > Informazioni su Eleu: chi lo ha sviluppato, la versione e i componenti usati. Tutto il testo sta in
/// un campo di sola lettura che prende il focus all'apertura: lo screen reader lo legge subito e lo si rilegge con
/// le frecce. Il pulsante apre la pagina GitHub dell'autore nel browser.
/// </summary>
public sealed class FinestraInformazioni : Form
{
    public const string Autore = "Gabriele Cusimano";
    public const string IndirizzoGitHub = "https://github.com/Gabro777";

    public FinestraInformazioni(string versione)
    {
        Text = "Informazioni su Eleu";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Font = new Font("Segoe UI", 11f);
        Padding = new Padding(16);

        var griglia = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Fill };
        griglia.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        griglia.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var icona = new PictureBox
        {
            Size = new Size(72, 72),
            SizeMode = PictureBoxSizeMode.Zoom,
            Margin = new Padding(0, 0, 16, 0),
            AccessibleName = "Icona di Eleu",
            AccessibleRole = AccessibleRole.Graphic,
        };
        try
        {
            icona.Image = new Icon(Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application, 64, 64).ToBitmap();
        }
        catch (Exception e) when (e is ArgumentException or IOException)
        {
        }
        var titolo = new Label
        {
            Text = $"Eleu {versione}",
            Font = new Font("Segoe UI", 20f, FontStyle.Bold),
            AutoSize = true,
            Anchor = AnchorStyles.Left,
        };
        griglia.Controls.Add(icona);
        griglia.Controls.Add(titolo);

        var testo = string.Join(Environment.NewLine,
        [
            $"Eleu {versione}",
            "Programma di scrittura accessibile per persone cieche e ipovedenti.",
            "",
            $"Sviluppato da {Autore}.",
            $"Su GitHub: {IndirizzoGitHub.Replace("https://", "")}",
            "",
            "Eleu è software libero e gratuito, con licenza GNU GPL versione 3:",
            "puoi usarlo, modificarlo e ridistribuirlo, e le versioni modificate restano libere.",
            "Il testo della licenza è nel file LICENSE.txt, nella cartella del programma.",
            "",
            "Usa liblouis per il Braille (licenza LGPL), Open XML SDK, NAudio, PdfPig e Html Agility Pack,",
            "e le voci, il riconoscimento dei caratteri e il controllo ortografico di Windows.",
        ]);
        var informazioni = new TextBox
        {
            Text = testo,
            ReadOnly = true,
            Multiline = true,
            BorderStyle = BorderStyle.None,
            BackColor = SystemColors.Control,
            Width = 520,
            Height = 190,
            TabIndex = 0,
            AccessibleName = "Informazioni",
            Margin = new Padding(0, 16, 0, 0),
        };
        griglia.Controls.Add(informazioni);
        griglia.SetColumnSpan(informazioni, 2);

        var pulsanti = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 12, 0, 0) };
        var github = new Button
        {
            Text = $"Apri la pagina &GitHub di {Autore}",
            AutoSize = true,
            Padding = new Padding(8, 2, 8, 2),
            AccessibleDescription = $"Apre {IndirizzoGitHub} nel browser",
        };
        github.Click += (_, _) => ApriPagina();
        var chiudi = new Button { Text = "&Chiudi", AutoSize = true, DialogResult = DialogResult.Cancel, Padding = new Padding(8, 2, 8, 2) };
        pulsanti.Controls.AddRange([github, chiudi]);
        griglia.Controls.Add(pulsanti);
        griglia.SetColumnSpan(pulsanti, 2);

        Controls.Add(griglia);
        AcceptButton = chiudi;
        CancelButton = chiudi;
        ActiveControl = informazioni;
        // Il cursore all'inizio, non tutto selezionato: lo screen reader legge la prima riga, poi si scorre.
        Shown += (_, _) => informazioni.Select(0, 0);
    }

    private void ApriPagina()
    {
        try
        {
            Process.Start(new ProcessStartInfo(IndirizzoGitHub) { UseShellExecute = true });
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            MessageBox.Show(this, $"Impossibile aprire il browser. L'indirizzo è {IndirizzoGitHub}", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
