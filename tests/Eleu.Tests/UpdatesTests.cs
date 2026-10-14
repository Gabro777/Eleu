using System.Net;
using System.Security.Cryptography;
using static Eleu.Tests.Support;

namespace Eleu.Tests;

public class UpdatesTests
{
    private const string Response = """
        {"tag_name":"v1.2.0","draft":false,"prerelease":false,"html_url":"https://github.com/Gabro777/Eleu/releases/tag/v1.2.0",
         "body":"# Eleu 1.2.0\n\n## Novità\n\n- **ODT** e [commenti](https://example.org)\n* `Opzioni`",
         "assets":[{"name":"note.txt","browser_download_url":"https://x/note.txt","size":3},
                   {"name":"Eleu-1.2.0.msi","browser_download_url":"https://x/Eleu-1.2.0.msi","size":1234,"digest":"sha256:ABCDEF"}]}
        """;

    [Theory]
    [InlineData("v1.2.0", "1.2.0")]
    [InlineData("1.2", "1.2.0")]
    [InlineData("1.1.0+abc123", "1.1.0")]
    [InlineData("Eleu v2.0.1", "2.0.1")]
    public void VersionFromTag(string tag, string wait) => Assert.Equal(Version.Parse(wait), Updates.VersionFromTag(tag));

    [Fact]
    public void VersionFromTag_Invalid() => Assert.Null(Updates.VersionFromTag("ultima"));

    [Fact]
    public void FromJson_TakesVersionNotesAndInstaller()
    {
        var r = Updates.FromJson(Response)!;
        Assert.Equal(new Version(1, 2, 0), r.Version);
        Assert.Equal("v1.2.0", r.Tag);
        Assert.Equal("Eleu-1.2.0.msi", r.MsiName);
        Assert.Equal("https://x/Eleu-1.2.0.msi", r.MsiUrl);
        Assert.Equal(1234, r.Size);
        Assert.Equal("abcdef", r.Sha256);
        Assert.EndsWith("/v1.2.0", r.Page);
    }

    [Fact]
    public void FromJson_NoDraftsOrPrereleases()
    {
        Assert.Null(Updates.FromJson(Response.Replace("\"draft\":false", "\"draft\":true")));
        Assert.Null(Updates.FromJson(Response.Replace("\"prerelease\":false", "\"prerelease\":true")));
        Assert.Null(Updates.FromJson("non è json"));
    }

    [Fact]
    public void ReleaseNotes_WithoutMarkdownMarks()
    {
        var text = Updates.ReadableReleaseNotes(Updates.FromJson(Response)!.ReleaseNotes);
        Assert.DoesNotContain("#", text);
        Assert.DoesNotContain("**", text);
        Assert.DoesNotContain("](", text);
        Assert.Contains("• ODT e commenti", text);
        Assert.Contains("• Opzioni", text);
    }

    private static (HttpListener Server, string Url) Server(byte[] data)
    {
        for (int attempt = 0; ; attempt++)
        {
            int port = Random.Shared.Next(20000, 60000);
            var server = new HttpListener();
            server.Prefixes.Add($"http://localhost:{port}/");
            try
            {
                server.Start();
            }
            catch (HttpListenerException) when (attempt < 5)
            {
                continue;
            }
            _ = Task.Run(async () =>
            {
                while (server.IsListening)
                {
                    try
                    {
                        var c = await server.GetContextAsync();
                        c.Response.ContentLength64 = data.Length;
                        await c.Response.OutputStream.WriteAsync(data);
                        c.Response.Close();
                    }
                    catch (Exception e) when (e is HttpListenerException or ObjectDisposedException or InvalidOperationException)
                    {
                        return;
                    }
                }
            });
            return (server, $"http://localhost:{port}/Eleu-9.9.9.msi");
        }
    }

    [Fact]
    public async Task Download_RightDigest_SameFile()
    {
        var data = new byte[300_000];
        Random.Shared.NextBytes(data);
        var (server, url) = Server(data);
        using (server)
        {
            var r = new Release(new Version(9, 9, 9), "v9.9.9", "", "", url, "Eleu-9.9.9.msi", data.Length, Convert.ToHexStringLower(SHA256.HashData(data)));
            long last = 0;
            var file = await Updates.DownloadAsync(r, new Progress<(long Done, long Total)>(a => last = a.Done), default);
            Assert.Equal(data, await File.ReadAllBytesAsync(file));
            File.Delete(file);
        }
    }

    [Fact]
    public async Task Download_DifferentDigest_NoInstallation()
    {
        var (server, url) = Server([1, 2, 3]);
        using (server)
        {
            var r = new Release(new Version(9, 9, 9), "v9.9.9", "", "", url, "Eleu-9.9.8.msi", 3, new string('0', 64));
            await Assert.ThrowsAsync<InvalidDataException>(() => Updates.DownloadAsync(r, null, default));
            Assert.False(File.Exists(Path.Combine(Path.GetTempPath(), "Eleu-aggiornamento", "Eleu-9.9.8.msi")));
        }
    }
}
