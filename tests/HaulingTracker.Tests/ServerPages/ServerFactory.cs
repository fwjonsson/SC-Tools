using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using HaulingTracker.Server;
using HaulingTracker.Server.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HaulingTracker.Tests.ServerPages;

/// <summary>The server in memory (pages, logins and uploads over HTTP), with its database and logs in a folder of its own that is deleted afterwards.</summary>
public sealed class ServerFactory : WebApplicationFactory<Program>
{
    public const string AdminName = "admin";
    public const string AdminPassword = "a test password 123";

    public string Dir { get; } = Path.Combine(Path.GetTempPath(), "ht-web-" + Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(Dir);
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["HaulingTracker:Database"] = Path.Combine(Dir, "tracker.db"),
            ["HaulingTracker:BackupDir"] = Path.Combine(Dir, "backups"),
            ["HaulingTracker:DataFile"] = Path.Combine(Dir, "data.json"),
            ["HaulingTracker:LogDir"] = Path.Combine(Dir, "logs"),
            ["HaulingTracker:Culture"] = "",   // the culture of the machine, as the other tests have it
            ["HaulingTracker:Admin:UserName"] = AdminName,
            ["HaulingTracker:Admin:Password"] = AdminPassword,
        }));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(Dir, true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public LiveTracker Live => Services.GetRequiredService<LiveTracker>();
    public DataStore Store => Services.GetRequiredService<DataStore>();

    /// <summary>A browser: it keeps its cookies and does not follow redirects (so a test sees where it is sent).</summary>
    public HttpClient Browser() => CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    /// <summary>A tracker with this key (the X-Tracker-Key header).</summary>
    public HttpClient Tracker(string key)
    {
        var c = Browser();
        c.DefaultRequestHeaders.Add("X-Tracker-Key", key);
        return c;
    }

    /// <summary>Makes a key for this player, as the admin page does.</summary>
    public string AddKey(string player)
    {
        var key = TrackerKeys.NewKey();
        Store.AddTrackerKey(player, key, DateTime.UtcNow);
        Services.GetRequiredService<TrackerKeys>().Reload(Store.TrackerKeys());
        return key;
    }

    /// <summary>The token of the form on this page (every form that changes something sends it back).</summary>
    public static string Token(string html)
    {
        var m = Regex.Match(html, "__RequestVerificationToken\"[^>]*?value=\"([^\"]+)\"");
        Assert.True(m.Success, "no form token on the page");
        return m.Groups[1].Value;
    }

    /// <summary>Opens the page with the form, and sends the form with these fields.</summary>
    public static async Task<HttpResponseMessage> Send(HttpClient c, string page, string action, params (string Name, string Value)[] fields)
    {
        var html = await c.GetStringAsync(page);
        var form = fields.Select(f => KeyValuePair.Create(f.Name, f.Value)).Append(KeyValuePair.Create("__RequestVerificationToken", Token(html)));
        return await c.PostAsync(action, new FormUrlEncodedContent(form));
    }

    /// <summary>A browser with the admin logged in.</summary>
    public async Task<HttpClient> Admin()
    {
        var c = Browser();
        var r = await Send(c, "/login", "/login", ("UserName", AdminName), ("Password", AdminPassword));
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        return c;
    }

    /// <summary>A browser with this player logged in by his tracker (Open my page).</summary>
    public async Task<HttpClient> Player(string key)
    {
        var url = await LoginLink(key);
        var c = Browser();
        var r = await c.GetAsync(url);
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.Equal("/me", r.Headers.Location?.OriginalString);
        return c;
    }

    /// <summary>The login link a tracker gets (relative).</summary>
    public async Task<string> LoginLink(string key)
    {
        var r = await Tracker(key).PostAsync("/api/login-link", null);
        r.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.GetProperty("url").GetString()!;
    }
}
