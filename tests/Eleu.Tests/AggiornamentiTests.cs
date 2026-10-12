using System.Net;
using System.Security.Cryptography;
using static Eleu.Tests.Supporto;

namespace Eleu.Tests;

public class AggiornamentiTests
{
    private const string Risposta = """
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
    public void VersioneDaTag(string tag, string attesa) => Assert.Equal(Version.Parse(attesa), Aggiornamenti.VersioneDaTag(tag));

    [Fact]
    public void VersioneDaTag_NonValida() => Assert.Null(Aggiornamenti.VersioneDaTag("ultima"));

    [Fact]
    public void DaJson_PrendeVersioneNovitaEInstaller()
    {
        var r = Aggiornamenti.DaJson(Risposta)!;
        Assert.Equal(new Version(1, 2, 0), r.Versione);
        Assert.Equal("v1.2.0", r.Tag);
        Assert.Equal("Eleu-1.2.0.msi", r.NomeMsi);
        Assert.Equal("https://x/Eleu-1.2.0.msi", r.IndirizzoMsi);
        Assert.Equal(1234, r.Dimensione);
        Assert.Equal("abcdef", r.Sha256);
        Assert.EndsWith("/v1.2.0", r.Pagina);
    }

    [Fact]
    public void DaJson_BozzeEProveNo()
    {
        Assert.Null(Aggiornamenti.DaJson(Risposta.Replace("\"draft\":false", "\"draft\":true")));
        Assert.Null(Aggiornamenti.DaJson(Risposta.Replace("\"prerelease\":false", "\"prerelease\":true")));
        Assert.Null(Aggiornamenti.DaJson("non è json"));
    }

    [Fact]
    public void Novita_SenzaSegniDelMarkdown()
    {
        var testo = Aggiornamenti.NovitaLeggibili(Aggiornamenti.DaJson(Risposta)!.Novita);
        Assert.DoesNotContain("#", testo);
        Assert.DoesNotContain("**", testo);
        Assert.DoesNotContain("](", testo);
        Assert.Contains("• ODT e commenti", testo);
        Assert.Contains("• Opzioni", testo);
    }

    private static (HttpListener Server, string Indirizzo) Server(byte[] dati)
    {
        for (int tentativo = 0; ; tentativo++)
        {
            int porta = Random.Shared.Next(20000, 60000);
            var server = new HttpListener();
            server.Prefixes.Add($"http://localhost:{porta}/");
            try
            {
                server.Start();
            }
            catch (HttpListenerException) when (tentativo < 5)
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
                        c.Response.ContentLength64 = dati.Length;
                        await c.Response.OutputStream.WriteAsync(dati);
                        c.Response.Close();
                    }
                    catch (Exception e) when (e is HttpListenerException or ObjectDisposedException or InvalidOperationException)
                    {
                        return;
                    }
                }
            });
            return (server, $"http://localhost:{porta}/Eleu-9.9.9.msi");
        }
    }

    [Fact]
    public async Task Scarica_ImprontaGiusta_FileUguale()
    {
        var dati = new byte[300_000];
        Random.Shared.NextBytes(dati);
        var (server, indirizzo) = Server(dati);
        using (server)
        {
            var r = new Rilascio(new Version(9, 9, 9), "v9.9.9", "", "", indirizzo, "Eleu-9.9.9.msi", dati.Length, Convert.ToHexStringLower(SHA256.HashData(dati)));
            long ultimo = 0;
            var file = await Aggiornamenti.ScaricaAsync(r, new Progress<(long Fatti, long Totale)>(a => ultimo = a.Fatti), default);
            Assert.Equal(dati, await File.ReadAllBytesAsync(file));
            File.Delete(file);
        }
    }

    [Fact]
    public async Task Scarica_ImprontaDiversa_NienteInstallazione()
    {
        var (server, indirizzo) = Server([1, 2, 3]);
        using (server)
        {
            var r = new Rilascio(new Version(9, 9, 9), "v9.9.9", "", "", indirizzo, "Eleu-9.9.8.msi", 3, new string('0', 64));
            await Assert.ThrowsAsync<InvalidDataException>(() => Aggiornamenti.ScaricaAsync(r, null, default));
            Assert.False(File.Exists(Path.Combine(Path.GetTempPath(), "Eleu-aggiornamento", "Eleu-9.9.8.msi")));
        }
    }
}
