using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace HaulingTracker;

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
                    + "Cache-Control: no-store\r\n"
                    + "X-Content-Type-Options: nosniff\r\n"
                    + "Content-Security-Policy: default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; connect-src 'self'\r\n"
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

    const string Page = """
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Hauling Tracker (live)</title>
<style>
  :root { --bg:#1e1e1e; --fg:#e6e6e6; --header:#1f3a68; --line:#3a3a3a; --cell:#2b2b2b; }
  * { box-sizing: border-box; }
  body { margin:0; padding:14px 18px 40px; background:var(--bg); color:var(--fg); font:14px "Segoe UI", system-ui, sans-serif; }
  h1 { font-size:20px; margin:0 0 2px; }
  h2 { font-size:16px; margin:22px 0 6px; }
  .bar { display:flex; gap:14px; align-items:baseline; flex-wrap:wrap; margin-bottom:6px; opacity:.85; }
  .live { font-weight:600; }
  .ok { color:#5fd35f; } .bad { color:#ff6b6b; }
  .top { display:flex; gap:18px; flex-wrap:wrap; align-items:flex-start; }
  .top > div { min-width:300px; flex:1 1 360px; }
  .scroll { overflow-x:auto; }
  table { border-collapse:collapse; width:100%; }
  th, td { border:1px solid var(--line); padding:4px 8px; white-space:nowrap; }
  th { background:var(--header); color:#fff; font-weight:600; text-align:center; white-space:normal; }
  .kv td:first-child { font-weight:600; width:48%; }
  .empty { opacity:.6; padding:6px 0; }
</style>
</head>
<body>
<h1>Hauling Tracker</h1>
<div class="bar"><span id="session"></span><span id="live" class="live"></span><span id="updated"></span></div>
<div class="top">
  <div><h2>Pickup locations</h2><div class="scroll" id="pickups"></div></div>
  <div>
    <h2>Contracts</h2><div class="scroll" id="rewards"></div>
    <h2>Session</h2><div class="scroll" id="summary"></div>
  </div>
</div>
<h2>Session leaderboard</h2><div class="scroll" id="leaders"></div>
<h2>Cargo runs</h2><div class="scroll" id="runs"></div>
<script>
function table(el, g) {
  el.textContent = '';
  if (!g || !g.rows || g.rows.length === 0) {
    const d = document.createElement('div'); d.className = 'empty'; d.textContent = 'Nothing yet.'; el.appendChild(d); return;
  }
  const t = document.createElement('table');
  const hr = t.createTHead().insertRow();
  g.headers.forEach(function (h) { const th = document.createElement('th'); th.textContent = h.text; hr.appendChild(th); });
  const tb = t.createTBody();
  g.rows.forEach(function (r) {
    const tr = tb.insertRow();
    if (r.bold) tr.style.fontWeight = '700';
    r.cells.forEach(function (v, i) {
      const td = tr.insertCell();
      td.textContent = v;
      td.style.textAlign = g.headers[i].align;
      if (r.back[i]) td.style.background = r.back[i];
      if (r.fore[i]) td.style.color = r.fore[i];
    });
  });
  el.appendChild(t);
}
function summary(el, items) {
  el.textContent = '';
  const t = document.createElement('table'); t.className = 'kv';
  const tb = t.createTBody();
  items.forEach(function (it) {
    const tr = tb.insertRow();
    tr.insertCell().textContent = it.k;
    const td = tr.insertCell(); td.textContent = it.v; td.style.textAlign = 'right';
  });
  el.appendChild(t);
}
async function refresh() {
  const live = document.getElementById('live');
  try {
    const res = await fetch('/data.json', { cache: 'no-store' });
    if (!res.ok) throw new Error('HTTP ' + res.status);
    const d = await res.json();
    const root = document.documentElement.style;
    if (d.theme) { root.setProperty('--bg', d.theme.bg); root.setProperty('--fg', d.theme.fg); root.setProperty('--header', d.theme.header); root.setProperty('--line', d.theme.line); root.setProperty('--cell', d.theme.cell); }
    document.getElementById('session').textContent = d.session || '';
    document.getElementById('updated').textContent = 'updated ' + d.updated;
    live.textContent = '● live'; live.className = 'live ok';
    table(document.getElementById('pickups'), d.pickups);
    table(document.getElementById('rewards'), d.rewards);
    summary(document.getElementById('summary'), d.summary || []);
    table(document.getElementById('leaders'), d.leaders);
    table(document.getElementById('runs'), d.runs);
  } catch (e) {
    live.textContent = '● connection lost, trying again'; live.className = 'live bad';
  }
}
refresh();
setInterval(refresh, 3000);
</script>
</body>
</html>
""";
}

public sealed partial class MainForm
{
    LiveWebServer? _web;
    readonly System.Windows.Forms.Timer _webTimer = new();
    readonly ToolStripMenuItem _webBtn = new();

    string WebButtonText() => _web != null ? "Live Web View: On (port " + _web.Port + ")" : "Live Web View: Off";

    void InitWebView()
    {
        _webTimer.Interval = 2000;
        _webTimer.Tick += (_, _) => PublishWeb();
        FormClosing += (_, _) => StopWeb();
        if (_data.WebView) StartWeb(false);
    }

    void ToggleWeb()
    {
        if (_web != null)
        {
            StopWeb();
            _data.WebView = false;
            Storage.Save(_data);
            SetStatus("The live web view is off.");
            return;
        }
        if (StartWeb(true))
        {
            _data.WebView = true;
            Storage.Save(_data);
        }
    }

    bool StartWeb(bool announce)
    {
        var port = _data.WebViewPort is >= 1024 and <= 65535 ? _data.WebViewPort : 8765;
        try
        {
            var web = new LiveWebServer(port);
            web.Start();
            _web = web;
        }
        catch (Exception ex)
        {
            _web = null;
            _webBtn.Text = WebButtonText();
            SetStatus("Could not start the live web view on port " + port + ": " + ex.Message);
            return false;
        }
        _webBtn.Text = WebButtonText();
        _webTimer.Start();
        PublishWeb();
        SetStatus("Live web view on http://localhost:" + port + " (read only).");
        if (announce)
        {
            MessageBox.Show(this,
                "The live view now runs on this computer at:\n\nhttp://localhost:" + port + "\n\n"
                + "It is read only: it only shows the tracker, nobody can change anything from it.\n\n"
                + "To let somebody else watch, run this in a terminal and send them the https://...trycloudflare.com address it prints:\n\n"
                + "cloudflared tunnel --url http://localhost:" + port,
                "Live Web View", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        return true;
    }

    void StopWeb()
    {
        _webTimer.Stop();
        _web?.Dispose();
        _web = null;
        _webBtn.Text = WebButtonText();
    }

    void PublishWeb()
    {
        if (_web == null || IsDisposed) return;
        try
        {
            _web.Json = BuildWebSnapshot();
        }
        catch (Exception ex)
        {
            SetStatus("Live web view: " + ex.Message);
        }
    }

    /// <summary>The Session tab as data: the same tables, with the colours they have on the screen.</summary>
    string BuildWebSnapshot()
    {
        var snapshot = new
        {
            updated = DateTime.Now.ToString("HH:mm:ss"),
            session = _sessionBox.Text,
            theme = new
            {
                bg = WebColor(Theme.Window),
                fg = WebColor(Theme.Text),
                header = WebColor(Theme.Header),
                line = WebColor(Theme.Lines),
                cell = WebColor(Theme.Cell),
            },
            summary = new[]
            {
                new { k = "Start Time", v = _startBox.Text },
                new { k = "Session Time", v = _durationVal.Text },
                new { k = "Batches Delivered", v = _missionsVal.Text },
                new { k = "Missions Completed", v = _missionsDoneVal.Text },
                new { k = "Total aUEC Earned", v = _revenueVal.Text },
                new { k = "aUEC/Hour", v = _hourlyVal.Text },
            },
            rewards = WebGrid(_rewards),
            pickups = WebGrid(_logGrid),
            leaders = WebGrid(_statsGrid),
            runs = WebGrid(_tripGrid),
        };
        return JsonSerializer.Serialize(snapshot);
    }

    static string WebColor(Color c) => "#" + c.R.ToString("x2") + c.G.ToString("x2") + c.B.ToString("x2");

    static string WebAlign(DataGridViewContentAlignment a)
    {
        var name = a.ToString();
        return name.Contains("Right") ? "right" : name.Contains("Center") ? "center" : "left";
    }

    static Color WebBack(DataGridViewCell c, DataGridView g)
    {
        foreach (var style in new[] { c.Style, c.OwningRow.DefaultCellStyle, c.OwningColumn.DefaultCellStyle, g.DefaultCellStyle })
        {
            if (!style.BackColor.IsEmpty) return style.BackColor;
        }
        return Theme.Cell;
    }

    static Color WebFore(DataGridViewCell c, DataGridView g)
    {
        foreach (var style in new[] { c.Style, c.OwningRow.DefaultCellStyle, c.OwningColumn.DefaultCellStyle, g.DefaultCellStyle })
        {
            if (!style.ForeColor.IsEmpty) return style.ForeColor;
        }
        return Theme.Text;
    }

    static object WebGrid(DataGridView g)
    {
        var columns = g.Columns.Cast<DataGridViewColumn>().Where(c => c.Visible).ToList();
        var headers = columns.Select(c => new { text = c.HeaderText, align = WebAlign(c.DefaultCellStyle.Alignment) }).ToList();
        var rows = new List<object>();
        foreach (DataGridViewRow r in g.Rows)
        {
            if (r.IsNewRow) continue;
            var cells = columns.Select(c => r.Cells[c.Index]).ToList();
            rows.Add(new
            {
                cells = cells.Select(c => Convert.ToString(c.Value, System.Globalization.CultureInfo.InvariantCulture) ?? "").ToArray(),
                back = cells.Select(c => WebColor(WebBack(c, g))).ToArray(),
                fore = cells.Select(c => WebColor(WebFore(c, g))).ToArray(),
                bold = (r.DefaultCellStyle.Font ?? g.DefaultCellStyle.Font)?.Bold == true,
            });
        }
        return new { headers, rows };
    }
}
