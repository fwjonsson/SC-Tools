using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace SCTools.Web;

/// <summary>
/// A tiny read-only web server for the live view. It only answers GET for the page and the data, listens on this computer only
/// (localhost) and has no way to change anything. To let somebody else watch, a tunnel (Cloudflare) points a public link at it.
/// </summary>
sealed class LiveWebServer : IDisposable
{
    readonly TcpListener _listener;
    volatile string _json = "{}";
    volatile bool _stop;

    public int Port { get; }

    /// <summary>The newest picture of the tracker, as JSON.</summary>
    public string Json
    {
        set => _json = value;
    }

    public LiveWebServer(int port)
    {
        Port = port;
        _listener = new TcpListener(IPAddress.Loopback, port);
    }

    public void Start()
    {
        _listener.Start();
        _ = Task.Run(AcceptLoop);
    }

    public void Dispose()
    {
        _stop = true;
        try { _listener.Stop(); }
        catch (Exception) { }
    }

    async Task AcceptLoop()
    {
        while (!_stop)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync();
            }
            catch (Exception)
            {
                if (_stop) return;
                await Task.Delay(200);
                continue;
            }
            _ = Task.Run(() => Serve(client));
        }
    }

    async Task Serve(TcpClient client)
    {
        using (client)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                var stream = client.GetStream();

                // read the request head (only the first line matters)
                var buffer = new byte[8192];
                var length = 0;
                while (length < buffer.Length)
                {
                    var n = await stream.ReadAsync(buffer.AsMemory(length), cts.Token);
                    if (n == 0) break;
                    length += n;
                    if (Encoding.ASCII.GetString(buffer, 0, length).Contains("\r\n\r\n")) break;
                }
                var head = Encoding.ASCII.GetString(buffer, 0, length);
                var parts = head.Split("\r\n")[0].Split(' ');
                var method = parts.Length > 0 ? parts[0] : "";
                var target = parts.Length > 1 ? parts[1] : "/";
                var path = target.Split('?')[0];

                var status = 200;
                var reason = "OK";
                var type = "text/html; charset=utf-8";
                var cache = "no-store";
                byte[] body;
                if (method != "GET" && method != "HEAD")
                {
                    status = 405;
                    reason = "Method Not Allowed";
                    type = "text/plain; charset=utf-8";
                    body = Encoding.UTF8.GetBytes("Read only.");
                }
                else if (path == "/" || path == "/index.html")
                {
                    body = Encoding.UTF8.GetBytes(Page);
                }
                else if (path == "/data.json")
                {
                    type = "application/json; charset=utf-8";
                    body = Encoding.UTF8.GetBytes(_json);
                }
                else if (path == "/favicon.ico" && WebAssets.Icon is { } icon)
                {
                    type = "image/x-icon";
                    body = icon;
                    cache = "public, max-age=86400";
                }
                else
                {
                    status = 404;
                    reason = "Not Found";
                    type = "text/plain; charset=utf-8";
                    body = Encoding.UTF8.GetBytes("Not found.");
                }

                var header = "HTTP/1.1 " + status + " " + reason + "\r\n"
                    + "Content-Type: " + type + "\r\n"
                    + "Content-Length: " + body.Length + "\r\n"
                    + "Cache-Control: " + cache + "\r\n"
                    + "X-Content-Type-Options: nosniff\r\n"
                    + "Content-Security-Policy: default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; connect-src 'self'; img-src 'self'\r\n"
                    + "Connection: close\r\n\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(header), cts.Token);
                if (method != "HEAD") await stream.WriteAsync(body, cts.Token);
                await stream.FlushAsync(cts.Token);
                client.Client.Shutdown(SocketShutdown.Send);
            }
            catch (Exception)
            {
                // a viewer that went away, or was too slow: nothing to do
            }
        }
    }


    /// <summary>The page: wwwroot (index.html with its app.css and app.js), embedded in this library and put together once.</summary>
    static string Page => WebAssets.Page;
}
