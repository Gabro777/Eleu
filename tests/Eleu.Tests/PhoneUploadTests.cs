using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using Eleu.Import;

namespace Eleu.Tests;

/// <summary>Photos from the phone: the page answers only on the secret path, and only images are accepted.</summary>
public class PhoneUploadTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"eleu-test-{Guid.NewGuid():N}");
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };

    public void Dispose()
    {
        _http.Dispose();
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    private PhoneUpload Server() => new(_folder, IPAddress.Loopback);

    [Fact]
    public async Task Page_OnlyOnTheSecretPath()
    {
        using var server = Server();
        Assert.Matches(@"^http://127\.0\.0\.1:\d+/[0-9a-f]{24}$", server.Url);

        var page = await _http.GetAsync(server.Url);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var html = await page.Content.ReadAsStringAsync();
        Assert.Contains("Fotografa una pagina", html);
        Assert.Contains("Scegli dalla galleria", html);
        Assert.Contains("accept=\"image/*\" multiple", html);

        var root = new Uri(server.Url).GetLeftPart(UriPartial.Authority);
        Assert.Equal(HttpStatusCode.NotFound, (await _http.GetAsync(root + "/")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _http.GetAsync(root + "/0123456789abcdef01234567")).StatusCode);
    }

    [Fact]
    public void EverySessionHasItsOwnSecret()
    {
        using var first = Server();
        using var second = Server();
        Assert.NotEqual(new Uri(first.Url).AbsolutePath, new Uri(second.Url).AbsolutePath);
    }

    [Fact]
    public async Task Photo_IsSavedAndAnnounced()
    {
        using var server = Server();
        var received = new TaskCompletionSource<string>();
        server.PhotoReceived += file => received.TrySetResult(file);
        var photo = Enumerable.Range(0, 100_000).Select(i => (byte)i).ToArray();

        var content = new ByteArrayContent(photo);
        content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        var answer = await _http.PostAsync(server.Url + "/foto", content);

        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        Assert.Equal("1", await answer.Content.ReadAsStringAsync());
        var file = await received.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.EndsWith(".jpg", file);
        Assert.Equal(photo, File.ReadAllBytes(file));
    }

    [Fact]
    public async Task SeveralPhotos_NumberedInOrder()
    {
        using var server = Server();
        var answers = new List<string>();
        foreach (var type in new[] { "image/png", "image/jpeg", "image/webp" })
        {
            var content = new ByteArrayContent([1, 2, 3]);
            content.Headers.ContentType = new MediaTypeHeaderValue(type);
            answers.Add(await (await _http.PostAsync(server.Url + "/foto", content)).Content.ReadAsStringAsync());
        }
        Assert.Equal(["1", "2", "3"], answers);
        Assert.Equal(["foto001.png", "foto002.jpg", "foto003.webp"], Directory.GetFiles(_folder).Select(Path.GetFileName).Order());
    }

    [Fact]
    public async Task NotAnImage_Refused_NothingSaved()
    {
        using var server = Server();
        var content = new StringContent("ciao", Encoding.UTF8, "text/plain");
        var answer = await _http.PostAsync(server.Url + "/foto", content);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, answer.StatusCode);
        Assert.Empty(Directory.GetFiles(_folder));
    }

    [Fact]
    public async Task TooBig_RefusedBeforeReadingIt()
    {
        using var server = Server();
        var uri = new Uri(server.Url);
        using var client = new TcpClient();
        await client.ConnectAsync(uri.Host, uri.Port);
        var stream = client.GetStream();
        var request = $"POST {uri.AbsolutePath}/foto HTTP/1.1\r\nHost: x\r\nContent-Type: image/png\r\nContent-Length: {PhoneUpload.MaxPhotoBytes + 1}\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(request));
        var reply = new byte[200];
        int n = await stream.ReadAsync(reply);
        Assert.StartsWith("HTTP/1.1 413", Encoding.ASCII.GetString(reply, 0, n));
        Assert.Empty(Directory.GetFiles(_folder));
    }

    [Fact]
    public async Task AfterClosing_NobodyAnswers()
    {
        string url;
        using (var server = Server())
            url = server.Url;
        await Assert.ThrowsAnyAsync<HttpRequestException>(() => _http.GetAsync(url));
    }
}
