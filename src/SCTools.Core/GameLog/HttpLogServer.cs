using System.Net;
using System.Net.Http.Json;

namespace SCTools.GameLog;

/// <summary>The server over the internet: every request carries the tracker's key (X-Tracker-Key), which says whose log it is.</summary>
public sealed class HttpLogServer : ILogServer, IDisposable
{
    readonly HttpClient _http;

    /// <param name="baseUrl">the address of the site, like https://sc-tools.fwjonsson.dev</param>
    public HttpLogServer(string baseUrl, string key, HttpMessageHandler? handler = null)
    {
        _http = handler == null ? new HttpClient() : new HttpClient(handler);
        _http.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
        _http.Timeout = TimeSpan.FromSeconds(30);
        _http.DefaultRequestHeaders.Add("X-Tracker-Key", key);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("SCTools-tracker");
    }

    sealed record At(long at);
    sealed record Me(string player);
    sealed record Link(string url);
    sealed record Piece(string file, long from, long to, IReadOnlyList<string> lines, DateTime? last);

    /// <summary>The player of this key, or null when the server does not know it.</summary>
    public async Task<string?> PlayerAsync(CancellationToken ct)
    {
        using var resp = await _http.GetAsync("api/me", ct);
        if (resp.StatusCode == HttpStatusCode.Unauthorized) return null;
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<Me>(ct))?.player;
    }

    /// <summary>
    /// The address that logs the player of this key in on the website ("Open my page"): it works once, within two minutes, so the key itself is never in an
    /// address. Null when the server does not know the key.
    /// </summary>
    public async Task<string?> LoginLinkAsync(CancellationToken ct)
    {
        using var resp = await _http.PostAsync("api/login-link", null, ct);
        if (resp.StatusCode == HttpStatusCode.Unauthorized) return null;
        resp.EnsureSuccessStatusCode();
        var url = (await resp.Content.ReadFromJsonAsync<Link>(ct))?.url;
        return url == null ? null : new Uri(_http.BaseAddress!, url.TrimStart('/')).ToString();
    }

    public async Task<long> CursorAsync(string file, CancellationToken ct)
    {
        using var resp = await _http.GetAsync("api/log/cursor?file=" + Uri.EscapeDataString(file), ct);
        Check(resp);
        return (await resp.Content.ReadFromJsonAsync<At>(ct))?.at ?? 0;
    }

    public async Task<UploadResult> UploadAsync(string file, long from, long to, IReadOnlyList<string> lines, DateTime? last, CancellationToken ct)
    {
        using var resp = await _http.PostAsJsonAsync("api/log", new Piece(file, from, to, lines, last), ct);
        if (resp.StatusCode == HttpStatusCode.Conflict) return new UploadResult(false, (await resp.Content.ReadFromJsonAsync<At>(ct))?.at ?? from);
        Check(resp);
        return new UploadResult(true, (await resp.Content.ReadFromJsonAsync<At>(ct))?.at ?? to);
    }

    static void Check(HttpResponseMessage resp)
    {
        if (resp.StatusCode == HttpStatusCode.Unauthorized) throw new UnauthorizedAccessException("The server does not know this key.");
        resp.EnsureSuccessStatusCode();
    }

    public void Dispose() => _http.Dispose();
}
