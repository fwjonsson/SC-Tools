using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SCTools.Updates;

/// <summary>
/// Looks for a newer build on GitHub (the "latest" release the build makes from main) and replaces the running exe with it: the program, or the tracker that
/// uploads the logs (each its own zip in the release). Only builds made by GitHub know their version (the commit), so a program you built yourself in Visual
/// Studio never updates itself.
/// </summary>
public static class Updater
{
    const string Repo = "fwjonsson/SC-Tools";

    /// <summary>
    /// The start of the name of what an update puts in Temp: the zip while it downloads (SCToolsUpdate-&lt;guid&gt;.zip, removed again). Before 29 Oct 2026 it was a
    /// folder of that name with the zip and the exe in it, which stayed: AppName.RemoveLeftoversAsync sweeps them.
    /// </summary>
    public const string TempName = "SCToolsUpdate-";

    static readonly HttpClient Http = CreateClient();

    static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("SCTools-updater");
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

    /// <summary>
    /// Downloads the newest exe (exeName, in zipName of the latest release) and puts it next to the running program as Name.new.exe, ready for ReplaceAndStart;
    /// returns its path. The zip only passes through Temp and is removed again, so an update leaves nothing there.
    /// </summary>
    public static async Task<string> DownloadAsync(string zipName, string exeName)
    {
        var me = Environment.ProcessPath ?? throw new InvalidOperationException("The path of the program is not known.");
        var zip = Path.Combine(Path.GetTempPath(), TempName + Guid.NewGuid().ToString("N") + ".zip");
        try
        {
            using (var resp = await Http.GetAsync("https://github.com/" + Repo + "/releases/download/latest/" + zipName, HttpCompletionOption.ResponseHeadersRead))
            {
                resp.EnsureSuccessStatusCode();
                await using var file = File.Create(zip);
                await resp.Content.CopyToAsync(file);
            }
            return Unpack(zip, exeName, Path.ChangeExtension(me, ".new.exe"));
        }
        finally
        {
            try { File.Delete(zip); }
            catch (IOException) { }                   // held for a moment (a virus scanner): RemoveLeftoversAsync takes it next time
            catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>Takes the exe (exeName) out of the zip to the target path (over what is there) and returns that path; a zip without the exe is refused.</summary>
    public static string Unpack(string zip, string exeName, string target)
    {
        using var archive = ZipFile.OpenRead(zip);
        var entry = archive.Entries.FirstOrDefault(e => string.Equals(e.FullName, exeName, StringComparison.OrdinalIgnoreCase))
            ?? throw new FileNotFoundException("The download does not contain " + exeName + ".");
        entry.ExtractToFile(target, overwrite: true);
        return target;
    }

    /// <summary>
    /// Windows does not let a program overwrite itself while it runs, but it does let the running exe be renamed. So the running exe becomes
    /// HaulingPlanner.old.exe (removed when the new one starts, AppName.RemoveLeftoversAsync), the new exe (HaulingPlanner.new.exe next to it, DownloadAsync)
    /// takes its place and is started (the same for the tracker's exe).
    /// </summary>
    public static void ReplaceAndStart(string newExe, string? arguments = null)
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
        Process.Start(new ProcessStartInfo(me) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(me) ?? "", Arguments = arguments ?? "" });
    }
}
