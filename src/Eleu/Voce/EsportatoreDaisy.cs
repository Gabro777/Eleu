using System.Globalization;
using System.Net;
using System.Text;
using Eleu.Documento;
using NAudio.MediaFoundation;
using NAudio.Wave;

namespace Eleu.Voce;

/// <summary>Un pezzo da leggere e sincronizzare: un titolo, un paragrafo o una frase di un paragrafo lungo.</summary>
public sealed record UnitaDaisy(string Id, string Testo, int LivelloTitolo);

/// <summary>Una sezione del libro: comincia con un titolo (o con l'inizio del documento) e ha un file audio suo.</summary>
public sealed record SezioneDaisy(int Numero, string Titolo, int Livello, List<UnitaDaisy> Unita)
{
    public string FileSmil => $"s{Numero:D3}.smil";

    public string FileAudio => $"a{Numero:D3}.mp3";
}

/// <summary>Dove sta una unità nell'audio della sua sezione.</summary>
public readonly record struct TempoUnita(TimeSpan Inizio, TimeSpan Fine);

/// <summary>
/// Libro DAISY 2.02 "audio e testo completo": il formato degli audiolibri per chi non vede. I lettori DAISY
/// (programmi e apparecchi) vanno di titolo in titolo dalla navigazione (ncc.html), leggono il testo
/// (content.html) e fanno sentire l'audio sincronizzato, paragrafo per paragrafo, grazie ai file SMIL.
/// Una sezione per titolo, di qualsiasi livello; un MP3 per sezione.
/// </summary>
public sealed class EsportatoreDaisy(CatalogoVoci catalogo, Action<string, string>? codifica = null)
{
    private static readonly TimeSpan PausaDopoTitolo = TimeSpan.FromMilliseconds(700);
    private static readonly TimeSpan PausaTraUnita = TimeSpan.FromMilliseconds(400);

    private readonly Action<string, string> _codifica = codifica ?? ((wav, mp3) =>
    {
        MediaFoundationApi.Startup();
        EsportatoreAudiolibro.CodificaMp3(wav, mp3);
    });

    public static List<SezioneDaisy> Sezioni(ContenutoDocumento contenuto, string titoloDocumento)
    {
        var sezioni = new List<SezioneDaisy>();
        SezioneDaisy? corrente = null;
        int unita = 0;
        foreach (var paragrafo in contenuto.Paragrafi)
        {
            var testo = paragrafo.Testo.Replace('\f', ' ').Trim();
            if (testo.Length == 0)
                continue;
            if (paragrafo.LivelloTitolo > 0 || corrente is null)
            {
                bool titolo = paragrafo.LivelloTitolo > 0;
                corrente = new SezioneDaisy(sezioni.Count + 1, titolo ? testo : titoloDocumento, titolo ? paragrafo.LivelloTitolo : 1, []);
                sezioni.Add(corrente);
                if (titolo)
                {
                    corrente.Unita.Add(new UnitaDaisy($"u{++unita}", testo, paragrafo.LivelloTitolo));
                    continue;
                }
            }
            // I paragrafi lunghi si dividono in frasi: chi ascolta torna indietro di poco, non di una pagina.
            foreach (var brano in Brani.DaTesto(testo, 0, testo.Length))
                corrente.Unita.Add(new UnitaDaisy($"u{++unita}", brano.Testo, 0));
        }
        return sezioni;
    }

    public async Task<int> EsportaAsync(ContenutoDocumento contenuto, string cartella, VoceDisponibile voce, ParametriVoce parametri,
        IProgress<AvanzamentoEsportazione>? avanzamento, CancellationToken annulla)
    {
        var metadati = contenuto.Metadati;
        var titolo = metadati.Titolo.Length > 0 ? metadati.Titolo : Path.GetFileName(cartella);
        var sezioni = Sezioni(contenuto, titolo);
        if (sezioni.Count == 0)
            return 0;
        Directory.CreateDirectory(cartella);

        var tempi = new Dictionary<string, TempoUnita>();
        var durate = new List<TimeSpan>();
        for (int s = 0; s < sezioni.Count; s++)
        {
            annulla.ThrowIfCancellationRequested();
            avanzamento?.Report(new AvanzamentoEsportazione(s + 1, sezioni.Count, sezioni[s].Titolo));
            var temporaneo = Path.Combine(Path.GetTempPath(), $"eleu-daisy-{Guid.NewGuid():N}.wav");
            try
            {
                durate.Add(await ScriviWavAsync(sezioni[s], temporaneo, voce, parametri, tempi, annulla));
                await Task.Run(() => _codifica(temporaneo, Path.Combine(cartella, sezioni[s].FileAudio)), annulla);
            }
            finally
            {
                File.Delete(temporaneo);
            }
        }

        var trascorso = TimeSpan.Zero;
        for (int s = 0; s < sezioni.Count; s++)
        {
            await File.WriteAllTextAsync(Path.Combine(cartella, sezioni[s].FileSmil), Smil(sezioni[s], tempi, durate[s], trascorso, titolo), new UTF8Encoding(false), annulla);
            trascorso += durate[s];
        }
        await File.WriteAllTextAsync(Path.Combine(cartella, "content.html"), Contenuto(sezioni, titolo), new UTF8Encoding(false), annulla);
        int file = sezioni.Count * 2 + 2;
        long byte_ = Directory.EnumerateFiles(cartella).Sum(f => new FileInfo(f).Length);
        await File.WriteAllTextAsync(Path.Combine(cartella, "ncc.html"),
            Ncc(sezioni, metadati with { Titolo = titolo }, trascorso, file, byte_, voce.Nome), new UTF8Encoding(false), annulla);
        return file;
    }

    /// <summary>L'audio della sezione: ogni unità di seguito, con una pausa dopo; restituisce la durata totale.</summary>
    private async Task<TimeSpan> ScriviWavAsync(SezioneDaisy sezione, string percorso, VoceDisponibile voce, ParametriVoce parametri,
        Dictionary<string, TempoUnita> tempi, CancellationToken annulla)
    {
        WaveFileWriter? scrittore = null;
        var adesso = TimeSpan.Zero;
        try
        {
            foreach (var unita in sezione.Unita)
            {
                var audio = await catalogo.SintetizzaAsync(unita.Testo, voce, parametri, annulla);
                scrittore ??= new WaveFileWriter(percorso, audio.Formato);
                scrittore.Write(audio.Pcm, 0, audio.Pcm.Length);
                tempi[unita.Id] = new TempoUnita(adesso, adesso + audio.Durata);
                var pausa = unita.LivelloTitolo > 0 ? PausaDopoTitolo : PausaTraUnita;
                int byteSilenzio = (int)(audio.Formato.AverageBytesPerSecond * pausa.TotalSeconds) / audio.Formato.BlockAlign * audio.Formato.BlockAlign;
                scrittore.Write(new byte[byteSilenzio], 0, byteSilenzio);
                adesso += audio.Durata + TimeSpan.FromSeconds((double)byteSilenzio / audio.Formato.AverageBytesPerSecond);
            }
        }
        finally
        {
            scrittore?.Dispose();
        }
        return adesso;
    }

    private static string Npt(TimeSpan t) => string.Create(CultureInfo.InvariantCulture, $"npt={t.TotalSeconds:0.000}s");

    private static string Orario(TimeSpan t, bool millisecondi = false) =>
        millisecondi ? $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}.{t.Milliseconds:000}" : $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}";

    private static string X(string testo) => WebUtility.HtmlEncode(testo);

    public static string Smil(SezioneDaisy sezione, IReadOnlyDictionary<string, TempoUnita> tempi, TimeSpan durata, TimeSpan trascorso, string titolo)
    {
        var s = new StringBuilder();
        s.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        s.AppendLine("<!DOCTYPE smil PUBLIC \"-//W3C//DTD SMIL 1.0//EN\" \"http://www.w3.org/TR/REC-smil/SMIL10.dtd\">");
        s.AppendLine("<smil>");
        s.AppendLine("<head>");
        s.AppendLine("<meta name=\"dc:format\" content=\"Daisy 2.02\" />");
        s.AppendLine($"<meta name=\"dc:title\" content=\"{X(titolo)}\" />");
        s.AppendLine("<meta name=\"ncc:generator\" content=\"Eleu\" />");
        s.AppendLine($"<meta name=\"ncc:totalElapsedTime\" content=\"{Orario(trascorso, true)}\" />");
        s.AppendLine($"<meta name=\"ncc:timeInThisSmil\" content=\"{Orario(durata, true)}\" />");
        s.AppendLine("<layout><region id=\"txtView\" /></layout>");
        s.AppendLine("</head>");
        s.AppendLine("<body>");
        s.AppendLine(string.Create(CultureInfo.InvariantCulture, $"<seq dur=\"{durata.TotalSeconds:0.000}s\">"));
        foreach (var unita in sezione.Unita)
        {
            var tempo = tempi[unita.Id];
            s.AppendLine($"<par endsync=\"last\" id=\"p_{unita.Id}\">");
            s.AppendLine($"<text src=\"content.html#{unita.Id}\" id=\"t_{unita.Id}\" />");
            s.AppendLine($"<audio src=\"{sezione.FileAudio}\" clip-begin=\"{Npt(tempo.Inizio)}\" clip-end=\"{Npt(tempo.Fine)}\" id=\"a_{unita.Id}\" />");
            s.AppendLine("</par>");
        }
        s.AppendLine("</seq>");
        s.AppendLine("</body>");
        s.AppendLine("</smil>");
        return s.ToString();
    }

    private const string Intestazione =
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
        "<!DOCTYPE html PUBLIC \"-//W3C//DTD XHTML 1.0 Transitional//EN\" \"http://www.w3.org/TR/xhtml1/DTD/xhtml1-transitional.dtd\">\n" +
        "<html xmlns=\"http://www.w3.org/1999/xhtml\" lang=\"it\" xml:lang=\"it\">\n";

    public static string Contenuto(IReadOnlyList<SezioneDaisy> sezioni, string titolo)
    {
        var h = new StringBuilder(Intestazione);
        h.AppendLine("<head>");
        h.AppendLine("<meta http-equiv=\"Content-type\" content=\"text/html; charset=utf-8\" />");
        h.AppendLine($"<title>{X(titolo)}</title>");
        h.AppendLine("</head>");
        h.AppendLine("<body>");
        foreach (var sezione in sezioni)
            foreach (var unita in sezione.Unita)
            {
                var tag = unita.LivelloTitolo > 0 ? $"h{Math.Min(6, unita.LivelloTitolo)}" : "p";
                h.AppendLine($"<{tag} id=\"{unita.Id}\"><a href=\"{sezione.FileSmil}#t_{unita.Id}\">{X(unita.Testo)}</a></{tag}>");
            }
        h.AppendLine("</body>");
        h.AppendLine("</html>");
        return h.ToString();
    }

    public static string Ncc(IReadOnlyList<SezioneDaisy> sezioni, Metadati metadati, TimeSpan totale, int file, long dimensione, string narratore)
    {
        var h = new StringBuilder(Intestazione);
        h.AppendLine("<head>");
        h.AppendLine("<meta http-equiv=\"Content-type\" content=\"text/html; charset=utf-8\" />");
        h.AppendLine($"<title>{X(metadati.Titolo)}</title>");
        void Meta(string nome, string valore) => h.AppendLine($"<meta name=\"{nome}\" content=\"{X(valore)}\" />");
        Meta("dc:title", metadati.Titolo);
        Meta("dc:creator", metadati.Autore.Length > 0 ? metadati.Autore : "Sconosciuto");
        Meta("dc:date", (metadati.Creato ?? DateTime.Today).ToString("yyyy-MM-dd"));
        Meta("dc:format", "Daisy 2.02");
        Meta("dc:identifier", $"eleu-{Guid.NewGuid():N}");
        Meta("dc:language", "it");
        Meta("dc:publisher", metadati.Autore.Length > 0 ? metadati.Autore : "Eleu");
        Meta("ncc:charset", "utf-8");
        Meta("ncc:generator", "Eleu");
        Meta("ncc:depth", Math.Min(6, sezioni.Max(s => s.Livello)).ToString(CultureInfo.InvariantCulture));
        Meta("ncc:files", file.ToString(CultureInfo.InvariantCulture));
        Meta("ncc:footnotes", "0");
        Meta("ncc:kByteSize", (dimensione / 1024).ToString(CultureInfo.InvariantCulture));
        Meta("ncc:maxPageNormal", "0");
        Meta("ncc:multimediaType", "audioFullText");
        Meta("ncc:narrator", narratore);
        Meta("ncc:pageFront", "0");
        Meta("ncc:pageNormal", "0");
        Meta("ncc:pageSpecial", "0");
        Meta("ncc:prodNotes", "0");
        Meta("ncc:setInfo", "1 of 1");
        Meta("ncc:sidebars", "0");
        Meta("ncc:tocItems", sezioni.Count.ToString(CultureInfo.InvariantCulture));
        Meta("ncc:totalTime", Orario(totale));
        h.AppendLine("</head>");
        h.AppendLine("<body>");
        for (int i = 0; i < sezioni.Count; i++)
        {
            var sezione = sezioni[i];
            // Il primo titolo è il titolo del libro (class="title"), come vuole lo standard.
            int livello = i == 0 ? 1 : Math.Clamp(sezione.Livello, 1, 6);
            var classe = i == 0 ? " class=\"title\"" : "";
            h.AppendLine($"<h{livello}{classe} id=\"ncc{sezione.Numero}\"><a href=\"{sezione.FileSmil}#p_{sezione.Unita[0].Id}\">{X(sezione.Titolo)}</a></h{livello}>");
        }
        h.AppendLine("</body>");
        h.AppendLine("</html>");
        return h.ToString();
    }
}
