using System.Text.RegularExpressions;
using Eleu.Documento;
using static Eleu.Tests.Supporto;

namespace Eleu.Tests;

public class GuidaTests
{
    private static string PercorsoGuida()
    {
        // Dalla cartella dei test si risale fino al progetto.
        for (var cartella = new DirectoryInfo(AppContext.BaseDirectory); cartella is not null; cartella = cartella.Parent)
        {
            var guida = Path.Combine(cartella.FullName, "src", "Eleu", "Guida", "Guida.html");
            if (File.Exists(guida))
                return guida;
        }
        throw new FileNotFoundException("Guida.html non trovata");
    }

    [Fact]
    public void Guida_SiApreConITitoli()
    {
        var guida = HtmlFormato.Leggi(File.ReadAllText(PercorsoGuida()));
        Assert.Equal("Guida di Eleu", guida.Metadati.Titolo);
        var titoli = guida.Paragrafi.Where(p => p.LivelloTitolo > 0).ToList();
        Assert.True(titoli.Count >= 10, $"solo {titoli.Count} titoli");
        Assert.Equal(1, titoli[0].LivelloTitolo);
    }

    /// <summary>Ogni scorciatoia con Ctrl, Alt o un tasto F scritta nella guida esiste davvero in Eleu.</summary>
    [Fact]
    public void Guida_LeScorciatoieCitateEsistono()
    {
        var testo = string.Join("\n", HtmlFormato.Leggi(File.ReadAllText(PercorsoGuida())).Paragrafi.Select(p => p.Testo));
        var citate = Regex.Matches(testo, @"\b(?:(?:Ctrl|Alt|Maiusc)\+)+(?:F\d+|[A-Z0-9]|Invio|Tab)\b|\bF\d{1,2}\b")
            .Select(m => m.Value).Distinct().ToList();
        Assert.NotEmpty(citate);

        var note = new HashSet<string>();
        EseguiSta(() =>
        {
            using var finestra = new FinestraPrincipale();
            void Raccogli(ToolStripItemCollection voci)
            {
                foreach (var voce in voci.OfType<ToolStripMenuItem>())
                {
                    if (voce.ShortcutKeys != Keys.None)
                        note.Add(Nome(voce.ShortcutKeys));
                    if (voce.ShortcutKeyDisplayString is string mostrata)
                        foreach (var parte in mostrata.Split(" oppure "))
                            note.Add(parte.Trim());
                    Raccogli(voce.DropDownItems);
                }
            }
            Raccogli(finestra.MainMenuStrip!.Items);
        });
        // Tasti fuori dai menu: i sinonimi (Ctrl+F, Ctrl+H, Ctrl+G, Ctrl+D), Ctrl+Tab per la barra, Maiusc+F10 per il menu contestuale.
        note.UnionWith(["Ctrl+F", "Ctrl+H", "Ctrl+G", "Ctrl+D", "Ctrl+Tab", "Alt+Tab", "Maiusc+F10", "Alt+F", "Alt+M", "Alt+V",
            "Alt+I", "Alt+O", "Alt+S", "Alt+C", "Alt+N", "Alt+A"]);
        var mancanti = citate.Where(c => !note.Contains(c)).ToList();
        Assert.True(mancanti.Count == 0, "Nella guida ma non in Eleu: " + string.Join(", ", mancanti));

        static string Nome(Keys tasti)
        {
            var parti = new List<string>();
            if (tasti.HasFlag(Keys.Control)) parti.Add("Ctrl");
            if (tasti.HasFlag(Keys.Alt)) parti.Add("Alt");
            if (tasti.HasFlag(Keys.Shift)) parti.Add("Maiusc");
            var tasto = tasti & Keys.KeyCode;
            parti.Add(tasto switch
            {
                Keys.Enter => "Invio",
                >= Keys.D0 and <= Keys.D9 => ((int)(tasto - Keys.D0)).ToString(),
                _ => tasto.ToString(),
            });
            return string.Join("+", parti);
        }
    }
}
