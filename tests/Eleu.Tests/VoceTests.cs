using Eleu.Documento;
using Eleu.Voce;
using NAudio.Wave;

namespace Eleu.Tests;

public class BraniTests
{
    [Fact]
    public void UnBranoPerParagrafo_SaltandoLeRigheVuote()
    {
        const string testo = "Primo paragrafo.\n\n  Secondo.\nTerzo";
        var brani = Brani.DaTesto(testo, 0, testo.Length);
        Assert.Equal([new Brano("Primo paragrafo.", 0), new Brano("Secondo.", 20), new Brano("Terzo", 29)], brani);
        Assert.All(brani, b => Assert.Equal(b.Testo, testo.Substring(b.Inizio, b.Testo.Length)));
    }

    [Fact]
    public void DalCursore_ODallaSelezione()
    {
        const string testo = "uno due tre\nquattro";
        Assert.Equal([new Brano("due tre", 4), new Brano("quattro", 12)], Brani.DaTesto(testo, 4, testo.Length));
        Assert.Equal([new Brano("due", 4)], Brani.DaTesto(testo, 4, 7));
    }

    [Fact]
    public void ParagrafoLungo_DivisoInFrasiConPosizioniGiuste()
    {
        var frase = "Questa è una frase di prova abbastanza lunga da riempire spazio. ";
        var testo = string.Concat(Enumerable.Repeat(frase, 15)).TrimEnd();
        var brani = Brani.DaTesto(testo, 0, testo.Length);

        Assert.True(brani.Count > 1);
        Assert.All(brani, b => Assert.True(b.Testo.Length <= Brani.LunghezzaMassima));
        Assert.All(brani, b => Assert.EndsWith(".", b.Testo));
        Assert.All(brani, b => Assert.Equal(b.Testo, testo.Substring(b.Inizio, b.Testo.Length)));
        Assert.Equal(testo.Replace(" ", ""), string.Concat(brani.Select(b => b.Testo)).Replace(" ", ""));
    }

    [Fact]
    public void Capitoli_SuiTitoli1_ConIntroduzione()
    {
        var contenuto = Documento(("Premessa breve.", 0), ("Primo", 1), ("Testo uno.", 0), ("Sotto", 2), ("Testo due.", 0), ("Secondo", 1), ("Fine.", 0));
        var capitoli = Brani.Capitoli(contenuto, "Libro");

        Assert.Equal(["Introduzione", "Primo", "Secondo"], capitoli.Select(c => c.Titolo));
        Assert.Equal(["Primo", "Testo uno.", "Sotto", "Testo due."], capitoli[1].Paragrafi);
        Assert.False(capitoli[0].IniziaConTitolo);
        Assert.True(capitoli[1].IniziaConTitolo);
    }

    [Fact]
    public void Capitoli_SoloTitoli2_SiDivideSuQuelli()
    {
        var contenuto = Documento(("A", 2), ("testo", 0), ("B", 2), ("altro", 0));
        Assert.Equal(["A", "B"], Brani.Capitoli(contenuto, "Libro").Select(c => c.Titolo));
    }

    [Fact]
    public void Capitoli_SenzaTitoli_UnoSoloColNomeDelDocumento()
    {
        var capitoli = Brani.Capitoli(Documento(("uno", 0), ("", 0), ("due", 0)), "Lettera");
        Assert.Equal("Lettera", Assert.Single(capitoli).Titolo);
        Assert.Equal(["uno", "due"], capitoli[0].Paragrafi);
    }

    [Theory]
    [InlineData(3, 12, "Il ritorno", "03 - Il ritorno.mp3")]
    [InlineData(7, 150, "Domanda: perché?", "007 - Domanda perché.mp3")]
    [InlineData(1, 1, "a/b\\c*d", "01 - a b c d.mp3")]
    [InlineData(1, 1, "...", "01 - Capitolo.mp3")]
    public void NomeFile_NumeratoESenzaCaratteriVietati(int numero, int totale, string titolo, string atteso)
    {
        Assert.Equal(atteso, Brani.NomeFile(numero, totale, titolo));
    }

    [Theory]
    [InlineData(100, 0)]
    [InlineData(300, 10)]
    [InlineData(50, -6)]
    [InlineData(1000, 10)]
    public void VelocitaSapi_DallaPercentuale(int percentuale, int atteso)
    {
        Assert.Equal(atteso, MotoreSapi.VelocitaSapi(percentuale));
    }

    [Fact]
    public void PosizioneAl_TrovaLaParolaInCorso()
    {
        var audio = new AudioSintetizzato([], new WaveFormat(22050, 16, 1),
            [new ConfineParola(0, TimeSpan.Zero), new ConfineParola(5, TimeSpan.FromSeconds(0.4)), new ConfineParola(11, TimeSpan.FromSeconds(0.9))]);
        Assert.Equal(0, audio.PosizioneAl(TimeSpan.FromSeconds(0.2)));
        Assert.Equal(5, audio.PosizioneAl(TimeSpan.FromSeconds(0.5)));
        Assert.Equal(11, audio.PosizioneAl(TimeSpan.FromSeconds(3)));
    }

    private static ContenutoDocumento Documento(params (string Testo, int Livello)[] paragrafi)
    {
        var contenuto = new ContenutoDocumento();
        foreach (var (testo, livello) in paragrafi)
        {
            var p = new Paragrafo { LivelloTitolo = livello };
            p.Aggiungi(new Segmento(testo));
            contenuto.Paragrafi.Add(p);
        }
        return contenuto;
    }
}

/// <summary>
/// Prove con le voci vere installate in Windows. Se una macchina non ha voci di un motore,
/// la prova di quel motore non ha niente da verificare e passa.
/// </summary>
public class SintesiRealeTests
{
    [Fact]
    public Task Sapi_ProduceAudioEConfiniDelleParole() => ProvaMotore(new MotoreSapi());

    [Fact]
    public Task OneCore_ProduceAudioEConfiniDelleParole() => ProvaMotore(new MotoreOneCore());

    private static async Task ProvaMotore(IMotoreSintesi motore)
    {
        var voce = motore.Voci().FirstOrDefault();
        if (voce is null)
            return;

        var audio = await motore.SintetizzaAsync("Ciao mondo, prova.", voce, new ParametriVoce(), CancellationToken.None);

        Assert.True(audio.Durata > TimeSpan.FromMilliseconds(300), $"Audio troppo corto: {audio.Durata}");
        Assert.Contains(audio.Parole, p => p.Posizione == 0);
        Assert.Contains(audio.Parole, p => p.Posizione == 5);
        Assert.Equal(audio.Parole.OrderBy(p => p.Inizio), audio.Parole);
    }

    [Fact]
    public async Task Daisy_ConLaVoceVera_AudioCheCorrispondeAiTempi()
    {
        var catalogo = CatalogoVoci.Predefinito();
        if (catalogo.Scegli(null) is not VoceDisponibile voce)
            return;

        var cartella = Path.Combine(Path.GetTempPath(), "eleu-daisy-" + Guid.NewGuid());
        try
        {
            var contenuto = new Documento.ContenutoDocumento { Metadati = new Documento.Metadati("Prova") };
            foreach (var (testo, titolo) in new[] { ("Primo", 1), ("Il gatto dorme.", 0), ("Secondo", 1), ("Il cane abbaia.", 0) })
            {
                var p = new Documento.Paragrafo { LivelloTitolo = titolo };
                p.Aggiungi(new Documento.Segmento(testo));
                contenuto.Paragrafi.Add(p);
            }
            int file = await new EsportatoreDaisy(catalogo).EsportaAsync(contenuto, cartella, voce, new ParametriVoce(), null, CancellationToken.None);

            Assert.Equal(6, file); // ncc, content, 2 smil, 2 mp3
            foreach (var mp3 in Directory.GetFiles(cartella, "*.mp3"))
            {
                // La durata dell'MP3 copre la fine dell'ultimo pezzo indicato nello SMIL della sua sezione.
                using var lettore = new NAudio.Wave.Mp3FileReader(mp3);
                var smil = File.ReadAllText(Path.Combine(cartella, "s" + Path.GetFileNameWithoutExtension(mp3)[1..] + ".smil"));
                var fine = System.Text.RegularExpressions.Regex.Matches(smil, "clip-end=\"npt=([0-9.]+)s\"").Select(m => double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)).Max();
                Assert.True(lettore.TotalTime.TotalSeconds >= fine - 0.1, $"{mp3}: {lettore.TotalTime.TotalSeconds}s < {fine}s");
                Assert.True(fine > 0.3);
            }
        }
        finally
        {
            if (Directory.Exists(cartella))
                Directory.Delete(cartella, true);
        }
    }

    [Fact]
    public async Task Audiolibro_UnMp3PerCapitolo()
    {
        var catalogo = CatalogoVoci.Predefinito();
        if (catalogo.Scegli(null) is not VoceDisponibile voce)
            return;

        var cartella = Path.Combine(Path.GetTempPath(), "eleu-audiolibro-" + Guid.NewGuid());
        try
        {
            var capitoli = new List<Capitolo>
            {
                new("Primo", ["Primo", "Uno."], IniziaConTitolo: true),
                new("Secondo", ["Secondo", "Due."], IniziaConTitolo: true),
            };
            var avanzamenti = new List<AvanzamentoEsportazione>();
            var creati = await new EsportatoreAudiolibro(catalogo).EsportaAsync(
                capitoli, cartella, voce, new ParametriVoce(), new ProgressoSincrono<AvanzamentoEsportazione>(avanzamenti.Add), CancellationToken.None);

            Assert.Equal(["01 - Primo.mp3", "02 - Secondo.mp3"], creati.Select(Path.GetFileName));
            Assert.Equal([1, 2], avanzamenti.Select(a => a.Capitolo));
            foreach (var file in creati)
            {
                var byte0 = File.ReadAllBytes(file);
                Assert.True(byte0.Length > 2000, $"{file}: solo {byte0.Length} byte");
                bool id3 = byte0[0] == 'I' && byte0[1] == 'D' && byte0[2] == '3';
                bool frameMp3 = byte0[0] == 0xFF && (byte0[1] & 0xE0) == 0xE0;
                Assert.True(id3 || frameMp3, $"{file} non sembra un MP3");
            }
            Assert.Empty(Directory.GetFiles(cartella, "*.eleu-tmp*"));
        }
        finally
        {
            if (Directory.Exists(cartella))
                Directory.Delete(cartella, true);
        }
    }

    private sealed class ProgressoSincrono<T>(Action<T> azione) : IProgress<T>
    {
        public void Report(T valore) => azione(valore);
    }
}
