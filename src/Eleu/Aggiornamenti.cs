using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Eleu;

/// <summary>Una versione pubblicata su GitHub: numero, novità, pagina e l'installer da scaricare (se c'è).</summary>
public sealed record Rilascio(Version Versione, string Tag, string Novita, string Pagina, string? IndirizzoMsi, string? NomeMsi, long Dimensione, string? Sha256);

/// <summary>
/// Aggiornamenti dalle release di GitHub: si chiede l'ultima versione pubblicata, se è più nuova si scarica
/// l'installer (.msi), se ne controlla l'impronta SHA-256 data da GitHub e lo si avvia; finita l'installazione,
/// Eleu si riapre da solo. L'installer è per l'utente: non servono permessi di amministratore.
/// </summary>
public static partial class Aggiornamenti
{
    public const string Repository = "Gabro777/Eleu";
    // ELEU_AGGIORNAMENTI: un altro indirizzo da cui chiedere l'ultima versione, per le prove.
    private static readonly string IndirizzoUltima =
        Environment.GetEnvironmentVariable("ELEU_AGGIORNAMENTI") ?? $"https://api.github.com/repos/{Repository}/releases/latest";

    private static readonly HttpClient Http = CreaClient();

    private static HttpClient CreaClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Eleu", VersioneAttuale.ToString()));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    /// <summary>La versione di Eleu in uso (per esempio 1.1.0).</summary>
    public static Version VersioneAttuale =>
        VersioneDaTag(Application.ProductVersion) ?? new Version(0, 0);

    /// <summary>"v1.2.0", "1.2", "1.2.0+abc" → 1.2.0; null se non è un numero di versione.</summary>
    public static Version? VersioneDaTag(string? tag)
    {
        var m = NumeroVersione().Match(tag ?? "");
        if (!m.Success || !Version.TryParse(m.Value, out var v))
            return null;
        return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
    }

    /// <summary>
    /// L'ultima versione dalla risposta di GitHub (api/releases/latest); null se la risposta non si capisce o se è
    /// una bozza o una versione di prova.
    /// </summary>
    public static Rilascio? DaJson(string json)
    {
        try
        {
            using var documento = JsonDocument.Parse(json);
            var r = documento.RootElement;
            if (Vero(r, "draft") || Vero(r, "prerelease"))
                return null;
            var tag = Testo(r, "tag_name");
            if (VersioneDaTag(tag) is not Version versione)
                return null;
            string? indirizzo = null, nome = null, sha = null;
            long dimensione = 0;
            if (r.TryGetProperty("assets", out var allegati) && allegati.ValueKind == JsonValueKind.Array)
                foreach (var a in allegati.EnumerateArray())
                {
                    var nomeAllegato = Testo(a, "name");
                    if (!nomeAllegato.EndsWith(".msi", StringComparison.OrdinalIgnoreCase))
                        continue;
                    nome = nomeAllegato;
                    indirizzo = Testo(a, "browser_download_url");
                    dimensione = a.TryGetProperty("size", out var s) && s.TryGetInt64(out long n) ? n : 0;
                    var impronta = Testo(a, "digest");
                    sha = impronta.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? impronta[7..].ToLowerInvariant() : null;
                    break;
                }
            return new Rilascio(versione, tag, Testo(r, "body"), Testo(r, "html_url"), indirizzo, nome, dimensione, sha);
        }
        catch (JsonException)
        {
            return null;
        }

        static string Testo(JsonElement e, string nome) =>
            e.TryGetProperty(nome, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
        static bool Vero(JsonElement e, string nome) => e.TryGetProperty(nome, out var v) && v.ValueKind == JsonValueKind.True;
    }

    /// <summary>L'ultima versione pubblicata. Eccezione se GitHub non risponde o il repository non è pubblico.</summary>
    public static async Task<Rilascio?> UltimaAsync(CancellationToken annulla = default)
    {
        using var risposta = await Http.GetAsync(IndirizzoUltima, annulla);
        risposta.EnsureSuccessStatusCode();
        return DaJson(await risposta.Content.ReadAsStringAsync(annulla));
    }

    /// <summary>Le novità scritte in Markdown, rese testo semplice da leggere con lo screen reader.</summary>
    public static string NovitaLeggibili(string markdown)
    {
        var righe = markdown.ReplaceLineEndings("\n").Split('\n').Select(r =>
        {
            var riga = Titolo().Replace(r, "");
            riga = Elenco().Replace(riga, "• ");
            riga = Collegamento().Replace(riga, "$1");
            return riga.Replace("**", "").Replace("__", "").Replace("`", "");
        });
        return string.Join(Environment.NewLine, righe).Trim();
    }

    /// <summary>
    /// Scarica l'installer in una cartella temporanea e ne controlla l'impronta. Avanzamento: byte scaricati e totali.
    /// </summary>
    public static async Task<string> ScaricaAsync(Rilascio rilascio, IProgress<(long Fatti, long Totale)>? avanzamento, CancellationToken annulla)
    {
        if (rilascio.IndirizzoMsi is null || rilascio.NomeMsi is null)
            throw new InvalidOperationException("Questa versione non ha l'installer.");
        var cartella = Path.Combine(Path.GetTempPath(), "Eleu-aggiornamento");
        Directory.CreateDirectory(cartella);
        var destinazione = Path.Combine(cartella, Path.GetFileName(rilascio.NomeMsi));

        using (var risposta = await Http.GetAsync(rilascio.IndirizzoMsi, HttpCompletionOption.ResponseHeadersRead, annulla))
        {
            risposta.EnsureSuccessStatusCode();
            long totale = risposta.Content.Headers.ContentLength ?? rilascio.Dimensione;
            await using var sorgente = await risposta.Content.ReadAsStreamAsync(annulla);
            await using var file = File.Create(destinazione);
            var buffer = new byte[81920];
            long fatti = 0;
            int letti;
            while ((letti = await sorgente.ReadAsync(buffer, annulla)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, letti), annulla);
                fatti += letti;
                avanzamento?.Report((fatti, totale));
            }
        }

        if (rilascio.Sha256 is string atteso)
        {
            string impronta;
            await using (var letto = File.OpenRead(destinazione))
                impronta = Convert.ToHexStringLower(await SHA256.HashDataAsync(letto, annulla));
            if (impronta != atteso)
            {
                File.Delete(destinazione);
                throw new InvalidDataException("Il file scaricato non corrisponde a quello pubblicato: per sicurezza non viene installato.");
            }
        }
        return destinazione;
    }

    /// <summary>Eleu è quello installato con l'installer (e quindi si può aggiornare con il nuovo installer)?</summary>
    public static bool Installato =>
        Path.GetDirectoryName(Application.ExecutablePath) is string cartella
        && string.Equals(Path.GetFullPath(cartella).TrimEnd('\\'),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Eleu"),
            StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Avvia l'installazione e, finita, riapre Eleu. Va chiamato quando le finestre di Eleu sono già chiuse
    /// (l'installer sostituisce i file del programma).
    /// </summary>
    public static void AvviaInstallazione(string msi)
    {
        // Prima qualche secondo di attesa, perché Eleu abbia finito di chiudersi; poi l'installer con la sola barra di
        // avanzamento (/passive) e, finito, di nuovo Eleu.
        var comandi = $"ping -n 4 127.0.0.1 >nul & start \"\" /wait msiexec.exe /i \"{msi}\" /passive /norestart & start \"\" \"{Application.ExecutablePath}\"";
        Process.Start(new ProcessStartInfo("cmd.exe", "/c " + comandi) { CreateNoWindow = true, UseShellExecute = false });
    }

    [GeneratedRegex(@"\d+\.\d+(\.\d+)?")]
    private static partial Regex NumeroVersione();

    [GeneratedRegex(@"^\s{0,3}#{1,6}\s*")]
    private static partial Regex Titolo();

    [GeneratedRegex(@"^\s*[-*+]\s+")]
    private static partial Regex Elenco();

    [GeneratedRegex(@"\[([^\]]+)\]\([^)]+\)")]
    private static partial Regex Collegamento();
}
