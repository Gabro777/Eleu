using static Eleu.Tests.Supporto;

namespace Eleu.Tests;

public class MenuTests
{
    /// <summary>
    /// Chi usa la tastiera apre i menu con Alt+lettera: due voci con la stessa lettera nello stesso menu
    /// non si raggiungono direttamente, e due comandi con la stessa scorciatoia si contendono il tasto.
    /// </summary>
    [Fact]
    public void Menu_SenzaLettereNeScorciatoieDoppie()
    {
        var problemi = new List<string>();
        EseguiSta(() =>
        {
            using var finestra = new FinestraPrincipale();
            var scorciatoie = new Dictionary<Keys, string>();
            Controlla(finestra.MainMenuStrip!.Items, "barra dei menu");

            void Controlla(ToolStripItemCollection voci, string menu)
            {
                var lettere = new Dictionary<char, string>();
                foreach (var voce in voci.OfType<ToolStripMenuItem>())
                {
                    var testo = voce.Text ?? "";
                    int e = testo.IndexOf('&');
                    if (e >= 0 && e + 1 < testo.Length)
                    {
                        char lettera = char.ToLowerInvariant(testo[e + 1]);
                        if (!lettere.TryAdd(lettera, testo))
                            problemi.Add($"{menu}: la lettera '{lettera}' è sia in \"{lettere[lettera]}\" sia in \"{testo}\"");
                    }
                    else if (voce.Enabled)
                    {
                        problemi.Add($"{menu}: \"{testo}\" non ha una lettera di scelta rapida");
                    }

                    if (voce.ShortcutKeys != Keys.None && !scorciatoie.TryAdd(voce.ShortcutKeys, testo))
                        problemi.Add($"la scorciatoia {voce.ShortcutKeys} è sia in \"{scorciatoie[voce.ShortcutKeys]}\" sia in \"{testo}\"");

                    Controlla(voce.DropDownItems, testo.Replace("&", ""));
                }
            }
        });
        Assert.Empty(problemi);
    }

    /// <summary>Il documento ha il menu contestuale (tasto Applicazioni), con i comandi base e lettere tutte diverse.</summary>
    [Fact]
    public void MenuContestuale_ComandiBaseSenzaLettereDoppie()
    {
        List<string> voci = [];
        EseguiSta(() =>
        {
            using var finestra = new FinestraPrincipale();
            var documento = finestra.Controls.OfType<Editor.CampoTesto>().Single();
            var menu = documento.ContextMenuStrip!;
            menu.Show(Point.Empty);
            voci = [.. menu.Items.OfType<ToolStripMenuItem>().Select(v => v.Text!)];
            menu.Close();
        });
        Assert.Contains("&Copia", voci);
        Assert.Contains("&Incolla", voci);
        var lettere = voci.Where(v => v.Contains('&')).Select(v => char.ToLowerInvariant(v[v.IndexOf('&') + 1])).ToList();
        Assert.Equal(lettere.Count, lettere.Distinct().Count());
    }

    [Fact]
    public void Finestre_SenzaLettereDoppie()
    {
        var problemi = new List<string>();
        EseguiSta(() =>
        {
            foreach (var finestra in TutteLeFinestre())
            {
                var lettere = new Dictionary<char, string>();
                foreach (var controllo in Tutti(finestra))
                {
                    var testo = controllo.Text ?? "";
                    int e = testo.IndexOf('&');
                    if (e < 0 || e + 1 >= testo.Length || controllo is ComboBox or TextBox or NumericUpDown)
                        continue;
                    char lettera = char.ToLowerInvariant(testo[e + 1]);
                    if (!lettere.TryAdd(lettera, testo))
                        problemi.Add($"{finestra.Text}: la lettera '{lettera}' è sia in \"{lettere[lettera]}\" sia in \"{testo}\"");
                }
                finestra.Dispose();
            }
        });
        Assert.Empty(problemi);
    }

    /// <summary>
    /// Lo screen reader legge un nome per ogni controllo raggiungibile con Tab, e ogni finestra si chiude con Esc
    /// (un pulsante Annulla o Chiudi come CancelButton).
    /// </summary>
    [Fact]
    public void Finestre_OgniControlloHaUnNomeEEscChiude()
    {
        var problemi = new List<string>();
        EseguiSta(() =>
        {
            foreach (var finestra in TutteLeFinestre())
            {
                finestra.ShowInTaskbar = false;
                finestra.StartPosition = FormStartPosition.Manual;
                finestra.Location = new Point(-5000, -5000);
                finestra.Show();
                Application.DoEvents();
                foreach (var controllo in Tutti(finestra).Where(c => c.Visible && c.CanSelect && c.TabStop))
                {
                    var nome = controllo.AccessibilityObject.Name;
                    if (string.IsNullOrWhiteSpace(nome))
                        problemi.Add($"{finestra.Text}: {controllo.GetType().Name} \"{controllo.Text}\" senza nome");
                }
                if (finestra.CancelButton is null)
                    problemi.Add($"{finestra.Text}: Esc non la chiude");
                finestra.Close();
                finestra.Dispose();
            }
        });
        Assert.True(problemi.Count == 0, string.Join(Environment.NewLine, problemi));
    }

    private static Form[] TutteLeFinestre() =>
    [
        new Interfaccia.FinestraCarattere(new Editor.FormatoCarattere(), ["Arial", "Segoe UI"]),
        new Interfaccia.FinestraParagrafo(new Editor.FormatoParagrafo()),
        new Interfaccia.FinestraTrova(true, "", "", new Documento.OpzioniRicerca()),
        new Interfaccia.FinestraVoce([], null, new Voce.ParametriVoce()),
        new Interfaccia.FinestraBraille([], null, new Braille.FormatoPagina(), _ => 1),
        new Interfaccia.FinestraPagina(new Documento.ImpostazioniPagina()),
        new Interfaccia.FinestraStampa(["Stampante"], null, 3),
        new Interfaccia.FinestraVaiA(0, 1),
        new FinestraLivello(1),
        new FinestraNome("Aggiungi segnalibro", "&Nome:", ""),
        new FinestraNota("Aggiungi nota", ""),
        new FinestraTabella(),
        new FinestraLarghezza(1, 4),
        new FinestraImmagine("Aggiungi immagine", Documento.Immagini.DaDati(ImmaginiTests.Bitmap(System.Drawing.Imaging.ImageFormat.Png), "")),
        new Interfaccia.FinestraOrtografia(),
        new Interfaccia.FinestraDizionario(["Eleu"]),
        new Interfaccia.FinestraPulizia(new Correzione.OpzioniPulizia(), true),
        new Interfaccia.FinestraSostituzioni([new("a", "b")], true),
        new Interfaccia.FinestraPronunce([new("NVDA", "enne vu di a")]),
        new Interfaccia.FinestraInformazioni("1.0.0"),
        new FinestraProprieta(new Documento.Metadati("Titolo", "Autore"), null, "10 parole"),
        new Interfaccia.FinestraElenco("Segnalibri", ["Nome", "Riga"], [new(["inizio", "1"], 0)]),
        new Interfaccia.FinestraTesto("Scorciatoie da tastiera", "Ctrl+N: nuovo"),
        new Interfaccia.FinestraOpzioni(new Impostazioni()),
        new Interfaccia.FinestraSimboli(),
        new Interfaccia.FinestraAggiornamento(Aggiornamenti.DaJson("""{"tag_name":"v9.0.0","body":"- novità","assets":[{"name":"a.msi","browser_download_url":"x"}]}""")!, new Version(1, 0, 0), installabile: true),
        new Interfaccia.FinestraGraficaTattile(new System.Drawing.Bitmap(50, 50), "Prova", ["Stampante"], null, 32, 27, _ => null),
    ];

    private static IEnumerable<Control> Tutti(Control radice) =>
        radice.Controls.Cast<Control>().SelectMany(c => Tutti(c).Prepend(c));
}
