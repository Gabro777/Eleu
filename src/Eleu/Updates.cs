using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Eleu;

/// <summary>A version published on GitHub: number, release notes, page and the installer to download (if any).</summary>
public sealed record Release(Version Version, string Tag, string ReleaseNotes, string Page, string? MsiUrl, string? MsiName, long Size, string? Sha256);

/// <summary>
/// Updates from GitHub releases: ask for the latest published version; if it is newer, download the
/// installer (.msi), check its SHA-256 digest given by GitHub and start it; when the installation is done,
/// Eleu reopens by itself. The installer is per-user: no administrator rights are needed.
/// </summary>
public static partial class Updates
{
    public const string Repository = "Gabro777/Eleu";
    // ELEU_UPDATE_URL: another address to ask for the latest version, for tests.
    private static readonly string LatestUrl =
        Environment.GetEnvironmentVariable("ELEU_UPDATE_URL") ?? $"https://api.github.com/repos/{Repository}/releases/latest";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Eleu", CurrentVersion.ToString()));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    /// <summary>The Eleu version in use (for example 1.1.0).</summary>
    public static Version CurrentVersion =>
        VersionFromTag(Application.ProductVersion) ?? new Version(0, 0);

    /// <summary>"v1.2.0", "1.2", "1.2.0+abc" → 1.2.0; null if it is not a version number.</summary>
    public static Version? VersionFromTag(string? tag)
    {
        var m = VersionNumber().Match(tag ?? "");
        if (!m.Success || !Version.TryParse(m.Value, out var v))
            return null;
        return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
    }

    /// <summary>
    /// The latest version from the GitHub response (api/releases/latest); null if the response cannot be understood or if
    /// it is a draft or a prerelease.
    /// </summary>
    public static Release? FromJson(string json)
    {
        try
        {
            using var documents = JsonDocument.Parse(json);
            var r = documents.RootElement;
            if (Real(r, "draft") || Real(r, "prerelease"))
                return null;
            var tag = Text(r, "tag_name");
            if (VersionFromTag(tag) is not Version version)
                return null;
            string? url = null, name = null, sha = null;
            long size = 0;
            if (r.TryGetProperty("assets", out var attachments) && attachments.ValueKind == JsonValueKind.Array)
                foreach (var a in attachments.EnumerateArray())
                {
                    var attachmentName = Text(a, "name");
                    if (!attachmentName.EndsWith(".msi", StringComparison.OrdinalIgnoreCase))
                        continue;
                    name = attachmentName;
                    url = Text(a, "browser_download_url");
                    size = a.TryGetProperty("size", out var s) && s.TryGetInt64(out long n) ? n : 0;
                    var digest = Text(a, "digest");
                    sha = digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? digest[7..].ToLowerInvariant() : null;
                    break;
                }
            return new Release(version, tag, Text(r, "body"), Text(r, "html_url"), url, name, size, sha);
        }
        catch (JsonException)
        {
            return null;
        }

        static string Text(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
        static bool Real(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
    }

    /// <summary>The latest published version. Throws if GitHub does not answer or the repository is not public.</summary>
    public static async Task<Release?> LatestAsync(CancellationToken cancel = default)
    {
        using var response = await Http.GetAsync(LatestUrl, cancel);
        response.EnsureSuccessStatusCode();
        return FromJson(await response.Content.ReadAsStringAsync(cancel));
    }

    /// <summary>The release notes written in Markdown, turned into plain text to read with the screen reader.</summary>
    public static string ReadableReleaseNotes(string markdown)
    {
        var rows = markdown.ReplaceLineEndings("\n").Split('\n').Select(r =>
        {
            var row = TitleRegex().Replace(r, "");
            row = ListRegex().Replace(row, "• ");
            row = Link().Replace(row, "$1");
            return row.Replace("**", "").Replace("__", "").Replace("`", "");
        });
        return string.Join(Environment.NewLine, rows).Trim();
    }

    /// <summary>
    /// Downloads the installer into a temporary folder and checks its digest. Progress: bytes downloaded and total.
    /// </summary>
    public static async Task<string> DownloadAsync(Release release, IProgress<(long Done, long Total)>? progress, CancellationToken cancel)
    {
        if (release.MsiUrl is null || release.MsiName is null)
            throw new InvalidOperationException("Questa versione non ha l'installer.");
        var folder = Path.Combine(Path.GetTempPath(), "Eleu-aggiornamento");
        Directory.CreateDirectory(folder);
        var target = Path.Combine(folder, Path.GetFileName(release.MsiName));

        using (var response = await Http.GetAsync(release.MsiUrl, HttpCompletionOption.ResponseHeadersRead, cancel))
        {
            response.EnsureSuccessStatusCode();
            long total = response.Content.Headers.ContentLength ?? release.Size;
            await using var source = await response.Content.ReadAsStreamAsync(cancel);
            await using var file = File.Create(target);
            var buffer = new byte[81920];
            long done = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, cancel)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, read), cancel);
                done += read;
                progress?.Report((done, total));
            }
        }

        if (release.Sha256 is string expected)
        {
            string digest;
            await using (var read = File.OpenRead(target))
                digest = Convert.ToHexStringLower(await SHA256.HashDataAsync(read, cancel));
            if (digest != expected)
            {
                File.Delete(target);
                throw new InvalidDataException("Il file scaricato non corrisponde a quello pubblicato: per sicurezza non viene installato.");
            }
        }
        return target;
    }

    /// <summary>Is this Eleu the one installed with the installer (and so it can be updated with the new installer)?</summary>
    public static bool IsInstalled =>
        Path.GetDirectoryName(Application.ExecutablePath) is string folder
        && string.Equals(Path.GetFullPath(folder).TrimEnd('\\'),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Eleu"),
            StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Starts the installation and, when done, reopens Eleu. Must be called when Eleu's windows are already closed
    /// (the installer replaces the program files).
    /// </summary>
    public static void StartInstallation(string msi)
    {
        // First a few seconds of waiting, so Eleu has finished closing; then the installer with only the progress
        // bar (/passive) and, when done, Eleu again.
        var commands = $"ping -n 4 127.0.0.1 >nul & start \"\" /wait msiexec.exe /i \"{msi}\" /passive /norestart & start \"\" \"{Application.ExecutablePath}\"";
        Process.Start(new ProcessStartInfo("cmd.exe", "/c " + commands) { CreateNoWindow = true, UseShellExecute = false });
    }

    [GeneratedRegex(@"\d+\.\d+(\.\d+)?")]
    private static partial Regex VersionNumber();

    [GeneratedRegex(@"^\s{0,3}#{1,6}\s*")]
    private static partial Regex TitleRegex();

    [GeneratedRegex(@"^\s*[-*+]\s+")]
    private static partial Regex ListRegex();

    [GeneratedRegex(@"\[([^\]]+)\]\([^)]+\)")]
    private static partial Regex Link();
}
