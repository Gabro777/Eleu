using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace Eleu.Import;

/// <summary>
/// Photos from the phone, without apps or cables: a tiny web server on the home network, alive only while the
/// "Foto dal telefono" window is open. The phone opens the page (from the QR code or by typing the address), takes the
/// photo of the page and sends it here. Safety: it listens only on the local network address, answers only on a
/// random secret path that changes every time, accepts only images up to 25 MB, and stops when the window closes.
/// </summary>
public sealed class PhoneUpload : IDisposable
{
    public const int MaxPhotoBytes = 25 * 1024 * 1024;
    private const int MaxHeaderBytes = 16 * 1024;

    private readonly TcpListener _listener;
    private readonly string _token;
    private readonly string _folder;
    private readonly CancellationTokenSource _stop = new();
    private int _photos;

    /// <summary>The address for the phone: http://address:port/secret.</summary>
    public string Url { get; }

    /// <summary>A photo arrived (the path of the saved file). Raised on a background thread.</summary>
    public event Action<string>? PhotoReceived;

    /// <param name="address">Where to listen: the local network address, or loopback in tests.</param>
    public PhoneUpload(string folder, IPAddress? address = null)
    {
        _folder = folder;
        Directory.CreateDirectory(folder);
        address ??= LocalNetworkAddress() ?? throw new InvalidOperationException(
            "Il computer non è collegato a una rete: collegalo alla stessa rete Wi-Fi del telefono.");
        _token = Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant();
        _listener = new TcpListener(address, 0);
        _listener.Start();
        Url = $"http://{address}:{((IPEndPoint)_listener.LocalEndpoint).Port}/{_token}";
        _ = AcceptAsync();
    }

    /// <summary>The IPv4 address of the computer on the home network (private ranges), on an interface with a gateway.</summary>
    public static IPAddress? LocalNetworkAddress()
    {
        var candidates = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .Select(n => n.GetIPProperties())
            .Where(p => p.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork))
            .SelectMany(p => p.UnicastAddresses.Select(u => u.Address))
            .Where(a => a.AddressFamily == AddressFamily.InterNetwork && IsPrivate(a));
        return candidates.FirstOrDefault();
    }

    /// <summary>
    /// Whether Windows treats a connected network as public: then the firewall usually blocks the phone, unless Eleu was
    /// allowed on public networks too. Read through the Network List Manager (no administrator rights needed).
    /// </summary>
    public static bool OnPublicNetwork()
    {
        try
        {
            var type = Type.GetTypeFromCLSID(new Guid("DCB00C01-570F-4A9B-8D69-199FDBA5723B"));
            if (type is null)
                return false;
            dynamic manager = Activator.CreateInstance(type)!;
            const int Connected = 1, Public = 0;
            foreach (dynamic network in manager.GetNetworks(Connected))
                if ((int)network.GetCategory() == Public)
                    return true;
            return false;
        }
        catch (Exception e) when (e is System.Runtime.InteropServices.COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException
            or InvalidCastException or TypeLoadException)
        {
            return false; // not knowing is not a reason to warn
        }
    }

    private static bool IsPrivate(IPAddress address)
    {
        var b = address.GetAddressBytes();
        return b[0] == 10 || b[0] == 172 && b[1] >= 16 && b[1] <= 31 || b[0] == 192 && b[1] == 168;
    }

    private async Task AcceptAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_stop.Token);
            }
            catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException or SocketException)
            {
                return;
            }
            _ = ServeAsync(client);
        }
    }

    private async Task ServeAsync(TcpClient client)
    {
        using (client)
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token))
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(60));
            try
            {
                var stream = client.GetStream();
                var (method, path, headers, rest) = await ReadHeadAsync(stream, timeout.Token);
                var (status, type, body) = await AnswerAsync(stream, method, path, headers, rest, timeout.Token);
                var head = $"HTTP/1.1 {status}\r\nContent-Type: {type}\r\nContent-Length: {body.Length}\r\n"
                    + "Cache-Control: no-store\r\nConnection: close\r\n\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(head), timeout.Token);
                await stream.WriteAsync(body, timeout.Token);
            }
            catch (Exception e) when (e is IOException or OperationCanceledException or SocketException or InvalidDataException or ObjectDisposedException)
            {
                // A phone that goes away or sends garbage: the connection is simply dropped.
            }
        }
    }

    private async Task<(string Status, string Type, byte[] Body)> AnswerAsync(NetworkStream stream, string method, string path,
        Dictionary<string, string> headers, byte[] rest, CancellationToken cancel)
    {
        var page = "/" + _token;
        if (method == "GET" && path == page)
            return ("200 OK", "text/html; charset=utf-8", Encoding.UTF8.GetBytes(Page));
        if (method != "POST" || path != page + "/foto")
            return ("404 Not Found", "text/plain; charset=utf-8", "Pagina non trovata"u8.ToArray());

        var type = headers.GetValueOrDefault("content-type", "").Split(';')[0].Trim().ToLowerInvariant();
        var extension = type switch
        {
            "image/jpeg" or "image/jpg" => ".jpg",
            "image/png" => ".png",
            "image/heic" or "image/heif" => ".heic",
            "image/webp" => ".webp",
            _ => null,
        };
        if (extension is null)
            return ("415 Unsupported Media Type", "text/plain; charset=utf-8", "Solo immagini"u8.ToArray());
        if (!int.TryParse(headers.GetValueOrDefault("content-length"), out int length) || length <= 0)
            return ("411 Length Required", "text/plain; charset=utf-8", "Manca la lunghezza"u8.ToArray());
        if (length > MaxPhotoBytes)
            return ("413 Content Too Large", "text/plain; charset=utf-8", "Foto troppo grande"u8.ToArray());

        var photo = new byte[length];
        int read = Math.Min(rest.Length, length);
        Array.Copy(rest, photo, read);
        while (read < length)
        {
            int n = await stream.ReadAsync(photo.AsMemory(read, length - read), cancel);
            if (n == 0)
                throw new IOException("Foto incompleta");
            read += n;
        }

        int number = Interlocked.Increment(ref _photos);
        var file = Path.Combine(_folder, $"foto{number:000}{extension}");
        await File.WriteAllBytesAsync(file, photo, cancel);
        PhotoReceived?.Invoke(file);
        return ("200 OK", "text/plain; charset=utf-8", Encoding.UTF8.GetBytes($"{number}"));
    }

    /// <summary>Request line and headers, and what was already read of the body.</summary>
    private static async Task<(string Method, string Path, Dictionary<string, string> Headers, byte[] BodyStart)> ReadHeadAsync(
        NetworkStream stream, CancellationToken cancel)
    {
        var buffer = new byte[MaxHeaderBytes];
        int filled = 0, end;
        while ((end = IndexOfBlankLine(buffer, filled)) < 0)
        {
            if (filled == buffer.Length)
                throw new InvalidDataException("Intestazione troppo lunga");
            int n = await stream.ReadAsync(buffer.AsMemory(filled), cancel);
            if (n == 0)
                throw new IOException("Connessione chiusa");
            filled += n;
        }
        var lines = Encoding.ASCII.GetString(buffer, 0, end).Split("\r\n");
        var first = lines[0].Split(' ');
        if (first.Length < 2)
            throw new InvalidDataException("Richiesta non valida");
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines.Skip(1))
        {
            int colon = line.IndexOf(':');
            if (colon > 0)
                headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
        }
        return (first[0], first[1].Split('?')[0], headers, buffer[(end + 4)..filled]);
    }

    private static int IndexOfBlankLine(byte[] buffer, int length)
    {
        for (int i = 0; i + 3 < length; i++)
            if (buffer[i] == '\r' && buffer[i + 1] == '\n' && buffer[i + 2] == '\r' && buffer[i + 3] == '\n')
                return i;
        return -1;
    }

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Stop();
        _stop.Dispose();
    }

    /// <summary>
    /// The page on the phone: one big button to take the photo, one to choose photos already taken from the gallery
    /// (several at once, sent in order), and what happened, read by the phone's screen reader.
    /// </summary>
    private const string Page = """
        <!DOCTYPE html>
        <html lang="it">
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <title>Eleu: foto della pagina</title>
        <style>
        body { font-family: system-ui, sans-serif; font-size: 1.3rem; margin: 1.5rem; line-height: 1.5; }
        label { display: block; background: #1e3a9e; color: #fff; text-align: center; padding: 1.4rem; border-radius: 0.8rem; font-weight: 700; margin-top: 1rem; }
        input { position: absolute; opacity: 0; width: 1px; height: 1px; }
        input:focus + label { outline: 0.3rem solid #f5b400; }
        #state { margin-top: 1.5rem; }
        </style>
        </head>
        <body>
        <h1>Foto della pagina per Eleu</h1>
        <p>Fotografa la pagina da leggere, oppure scegli foto già fatte dalla galleria: Eleu ne riconosce il testo. Puoi mandare più pagine, una dopo l'altra.</p>
        <input id="photo" type="file" accept="image/*" capture="environment">
        <label for="photo">Fotografa una pagina</label>
        <input id="gallery" type="file" accept="image/*" multiple>
        <label for="gallery">Scegli dalla galleria</label>
        <p id="state" role="status" aria-live="polite"></p>
        <script>
        const state = document.getElementById("state");
        // The photos one at a time, in the order they were chosen, so the pages stay in order.
        async function send(e) {
          const files = [...e.target.files];
          e.target.value = "";
          for (const [i, file] of files.entries()) {
            state.textContent = files.length > 1 ? "Invio della foto " + (i + 1) + " di " + files.length + "…" : "Invio della foto in corso…";
            try {
              const r = await fetch(location.pathname + "/foto", { method: "POST", headers: { "Content-Type": file.type || "image/jpeg" }, body: file });
              if (!r.ok) {
                state.textContent = "Eleu non ha accettato la foto: " + (await r.text());
                return;
              }
              state.textContent = "Pagina " + (await r.text()) + " ricevuta da Eleu.";
            } catch {
              state.textContent = "La foto non è arrivata: controlla che telefono e computer siano sulla stessa rete Wi-Fi e che la finestra di Eleu sia ancora aperta.";
              return;
            }
          }
          if (files.length > 0)
            state.textContent += " Puoi mandare altre pagine, oppure tornare al computer.";
        }
        document.getElementById("photo").addEventListener("change", send);
        document.getElementById("gallery").addEventListener("change", send);
        </script>
        </body>
        </html>
        """;
}
