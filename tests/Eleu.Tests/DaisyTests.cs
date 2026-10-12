using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Eleu.Documento;
using Eleu.Voce;
using NAudio.Wave;

namespace Eleu.Tests;

public class DaisyTests
{
    /// <summary>Una voce finta: silenzio lungo 40 millisecondi per carattere.</summary>
    private sealed class MotoreFinto : IMotoreSintesi
    {
        public TipoMotore Tipo => TipoMotore.Sapi;

        public IReadOnlyList<VoceDisponibile> Voci() => [new("finta", "Voce finta", "it-IT", TipoMotore.Sapi)];

        public Task<AudioSintetizzato> SintetizzaAsync(string testo, VoceDisponibile voce, ParametriVoce parametri, CancellationToken annulla)
        {
            var formato = new WaveFormat(22050, 16, 1);
            int byteAudio = (int)(formato.AverageBytesPerSecond * 0.04 * testo.Length) / 2 * 2;
            return Task.FromResult(new AudioSintetizzato(new byte[byteAudio], formato, []));
        }
    }

    private static ContenutoDocumento Esempio()
    {
        var c = new ContenutoDocumento { Metadati = new Metadati("Ricette", "Anna") };
        void P(string testo, int titolo = 0)
        {
            var p = new Paragrafo { LivelloTitolo = titolo };
            p.Aggiungi(new Segmento(testo));
            c.Paragrafi.Add(p);
        }
        P("Introduzione breve.");
        P("Primi", 1);
        P("Servono uova e farina.");
        P("Pasta fresca", 2);
        P(string.Join(" ", Enumerable.Repeat("Si impasta con calma.", 30))); // lungo: diventa più frasi
        return c;
    }

    [Fact]
    public void Sezioni_UnaPerTitoloEParagrafiLunghiDivisi()
    {
        var sezioni = EsportatoreDaisy.Sezioni(Esempio(), "Ricette");
        Assert.Equal(["Ricette", "Primi", "Pasta fresca"], sezioni.Select(s => s.Titolo));
        Assert.Equal([1, 1, 2], sezioni.Select(s => s.Livello));
        Assert.True(sezioni[2].Unita.Count > 2, "il paragrafo lungo deve diventare più unità");
        Assert.Equal(1, sezioni[1].Unita[0].LivelloTitolo); // il titolo è la prima unità della sua sezione
    }

    [Fact]
    public async Task Esporta_FileCollegatiETempiCoerenti()
    {
        var cartella = Path.Combine(Path.GetTempPath(), $"eleu-daisy-test-{Guid.NewGuid():N}");
        try
        {
            var catalogo = new CatalogoVoci(new MotoreFinto());
            var esportatore = new EsportatoreDaisy(catalogo, (wav, mp3) => File.Copy(wav, mp3));
            int file = await esportatore.EsportaAsync(Esempio(), cartella, catalogo.Voci[0], new ParametriVoce(), null, CancellationToken.None);

            var nomi = Directory.GetFiles(cartella).Select(Path.GetFileName).Order().ToList();
            Assert.Equal(file, nomi.Count);
            Assert.Equal(["a001.mp3", "a002.mp3", "a003.mp3", "content.html", "ncc.html", "s001.smil", "s002.smil", "s003.smil"], nomi);

            // Tutti i file sono XML ben formato, e i collegamenti puntano a id che esistono.
            var ncc = XDocument.Load(Path.Combine(cartella, "ncc.html"));
            var contenuto = XDocument.Load(Path.Combine(cartella, "content.html"));
            var idTesto = contenuto.Descendants().Select(e => (string?)e.Attribute("id")).OfType<string>().ToHashSet();
            Assert.Equal("Daisy 2.02", Meta(ncc, "dc:format"));
            Assert.Equal("3", Meta(ncc, "ncc:tocItems"));
            Assert.Equal("audioFullText", Meta(ncc, "ncc:multimediaType"));
            Assert.Equal("title", ncc.Descendants().First(e => e.Name.LocalName == "h1").Attribute("class")?.Value);

            var totale = TimeSpan.Zero;
            foreach (var smilFile in nomi.Where(n => n!.EndsWith(".smil")))
            {
                var testoSmil = File.ReadAllText(Path.Combine(cartella, smilFile!));
                var smil = XDocument.Parse(testoSmil.Replace("<!DOCTYPE smil PUBLIC \"-//W3C//DTD SMIL 1.0//EN\" \"http://www.w3.org/TR/REC-smil/SMIL10.dtd\">", ""));
                double fineAttesa = 0;
                foreach (var audio in smil.Descendants("audio"))
                {
                    double inizio = Secondi(audio.Attribute("clip-begin")!.Value), fine = Secondi(audio.Attribute("clip-end")!.Value);
                    Assert.True(inizio >= fineAttesa - 0.001 && fine > inizio, $"{smilFile}: clip {inizio}-{fine}");
                    fineAttesa = fine;
                }
                foreach (var testo in smil.Descendants("text"))
                    Assert.Contains(testo.Attribute("src")!.Value.Split('#')[1], idTesto);
                var durata = TimeSpan.FromSeconds(Secondi("npt=" + smil.Descendants("seq").First().Attribute("dur")!.Value));
                // L'ultimo clip finisce prima della fine della sezione (dopo c'è la pausa).
                Assert.True(fineAttesa <= durata.TotalSeconds);
                totale += durata;
            }
            Assert.Equal($"{(int)totale.TotalHours:00}:{totale.Minutes:00}:{totale.Seconds:00}", Meta(ncc, "ncc:totalTime"));
        }
        finally
        {
            if (Directory.Exists(cartella))
                Directory.Delete(cartella, true);
        }

        static string? Meta(XDocument ncc, string nome) =>
            ncc.Descendants().FirstOrDefault(e => e.Name.LocalName == "meta" && (string?)e.Attribute("name") == nome)?.Attribute("content")?.Value;

        static double Secondi(string npt) =>
            double.Parse(Regex.Match(npt, @"npt=([\d.]+)s").Groups[1].Value, CultureInfo.InvariantCulture);
    }
}
