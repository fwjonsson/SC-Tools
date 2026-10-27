using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HaulingTracker.Updates;

/// <summary>
/// Looks for a newer build on GitHub (the "latest" release the build makes from main) and replaces the running exe with it: the program, or the tracker that
/// uploads the logs (each its own zip in the release). Only builds made by GitHub know their version (the commit), so a program you built yourself in Visual
/// Studio never updates itself.
/// </summary>
public static class Updater
{
    const string Repo = "fwjonsson/SCHaulingTracker";

    static readonly HttpClient Http = CreateClient();

    static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("HaulingTracker-updater");
        c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return c;
    }

    /// <summary>The commit this program was built from, or null for a build made on your own computer.</summary>
    public static string? OwnCommit
    {
        get
        {
            var v = (Assembly.GetEntryAssembly() ?? typeof(Updater).Assembly).GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            var m = Regex.Match(v ?? "", "release-([0-9a-f]{40})");
            return m.Success ? m.Groups[1].Value : null;
        }
    }

    /// <summary>The commit of the newest build on GitHub (written in the notes of the "latest" release), or null.</summary>
    public static async Task<string?> LatestCommitAsync(CancellationToken ct)
    {
        using var resp = await Http.GetAsync("https://api.github.com/repos/" + Repo + "/releases/tags/latest", ct);
        if (!resp.IsSuccessStatusCode) return null;
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        var body = doc.RootElement.TryGetProperty("body", out var b) ? b.GetString() : null;
        var m = Regex.Match(body ?? "", "build: ([0-9a-f]{40})");
        return m.Success ? m.Groups[1].Value : null;
    }

    /// <summary>True when the latest build is ahead of this one (so a build from a branch that is not merged yet is never replaced by an older one).</summary>
    public static async Task<bool> IsNewerAsync(string own, string latest, CancellationToken ct)
    {
        if (own == latest) return false;
        using var resp = await Http.GetAsync("https://api.github.com/repos/" + Repo + "/compare/" + own + "..." + latest, ct);
        if (!resp.IsSuccessStatusCode) return false;
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        return doc.RootElement.TryGetProperty("status", out var s) && s.GetString() == "ahead";
    }

    /// <summary>Downloads the newest exe (zipName of the latest release, with exeName in it) into a temporary folder and returns its path.</summary>
    public static async Task<string> DownloadAsync(string zipName, string exeName)
    {
        var dir = Path.Combine(Path.GetTempPath(), "HaulingTrackerUpdate-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var zip = Path.Combine(dir, zipName);
        using (var resp = await Http.GetAsync("https://github.com/" + Repo + "/releases/download/latest/" + zipName, HttpCompletionOption.ResponseHeadersRead))
        {
            resp.EnsureSuccessStatusCode();
            await using var file = File.Create(zip);
            await resp.Content.CopyToAsync(file);
        }
        ZipFile.ExtractToDirectory(zip, dir);
        var exe = Path.Combine(dir, exeName);
        if (!File.Exists(exe)) throw new FileNotFoundException("The download does not contain " + exeName + ".");
        return exe;
    }

    /// <summary>
    /// Windows does not let a program overwrite itself while it runs, but it does let the running exe be renamed. So the running exe becomes
    /// HaulingTracker.old.exe (kept as a backup until the next update), the new exe takes its place and is started (the same for the tracker's exe).
    /// </summary>
    public static void ReplaceAndStart(string newExe)
    {
        var me = Environment.ProcessPath ?? throw new InvalidOperationException("The path of the program is not known.");
        var old = Path.ChangeExtension(me, ".old.exe");
        if (File.Exists(old)) File.Delete(old);
        File.Move(me, old);
        try
        {
            File.Move(newExe, me);
        }
        catch
        {
            File.Move(old, me);   // put the program back, nothing has changed
            throw;
        }
        Process.Start(new ProcessStartInfo(me) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(me) ?? "" });
    }
}
