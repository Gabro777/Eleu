using System.Diagnostics;

namespace Eleu.UI;

/// <summary>
/// Aiuto > Informazioni su Eleu: who developed it, the version and the components used. All the text is in
/// a read-only box that takes the focus on opening: the screen reader reads it right away and it can be reread with
/// the arrows. The button opens the author's GitHub page in the browser.
/// </summary>
public sealed class AboutDialog : Form
{
    public const string Author = "Gabriele Cusimano";
    public const string GitHubUrl = "https://github.com/Gabro777";

    public AboutDialog(string version)
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

        var grid = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Fill };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var icon = new PictureBox
        {
            Size = new Size(72, 72),
            SizeMode = PictureBoxSizeMode.Zoom,
            Margin = new Padding(0, 0, 16, 0),
            AccessibleName = "Icona di Eleu",
            AccessibleRole = AccessibleRole.Graphic,
        };
        try
        {
            icon.Image = new Icon(Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application, 64, 64).ToBitmap();
        }
        catch (Exception e) when (e is ArgumentException or IOException)
        {
        }
        var heading = new Label
        {
            Text = $"Eleu {version}",
            Font = new Font("Segoe UI", 20f, FontStyle.Bold),
            AutoSize = true,
            Anchor = AnchorStyles.Left,
        };
        grid.Controls.Add(icon);
        grid.Controls.Add(heading);

        var text = string.Join(Environment.NewLine,
        [
            $"Eleu {version}",
            "Programma di scrittura accessibile per persone cieche e ipovedenti.",
            "",
            $"Sviluppato da {Author}.",
            $"Su GitHub: {GitHubUrl.Replace("https://", "")}",
            "",
            "Eleu è software libero e gratuito, con licenza GNU GPL versione 3:",
            "puoi usarlo, modificarlo e ridistribuirlo, e le versioni modificate restano libere.",
            "Il testo della licenza è nel file LICENSE.txt, nella cartella del programma.",
            "",
            "Usa liblouis per il Braille (licenza LGPL), Open XML SDK, NAudio, PdfPig, Html Agility Pack e QRCoder,",
            "e le voci, il riconoscimento dei caratteri e il controllo ortografico di Windows.",
        ]);
        var info = new TextBox
        {
            Text = text,
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
        grid.Controls.Add(info);
        grid.SetColumnSpan(info, 2);

        var buttons = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 12, 0, 0) };
        var github = new Button
        {
            Text = $"Apri la pagina &GitHub di {Author}",
            AutoSize = true,
            Padding = new Padding(8, 2, 8, 2),
            AccessibleDescription = $"Apre {GitHubUrl} nel browser",
        };
        github.Click += (_, _) => OpenPage();
        var close = new Button { Text = "&Chiudi", AutoSize = true, DialogResult = DialogResult.Cancel, Padding = new Padding(8, 2, 8, 2) };
        buttons.Controls.AddRange([github, close]);
        grid.Controls.Add(buttons);
        grid.SetColumnSpan(buttons, 2);

        Controls.Add(grid);
        AcceptButton = close;
        CancelButton = close;
        ActiveControl = info;
        // The cursor at the start, not everything selected: the screen reader reads the first line, then you scroll.
        Shown += (_, _) => info.Select(0, 0);
    }

    private void OpenPage()
    {
        try
        {
            Process.Start(new ProcessStartInfo(GitHubUrl) { UseShellExecute = true });
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            MessageBox.Show(this, $"Impossibile aprire il browser. L'indirizzo è {GitHubUrl}", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
