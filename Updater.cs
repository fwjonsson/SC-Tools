using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HaulingTracker;

/// <summary>
/// Looks for a newer build on GitHub (the "latest" release the build makes from main) and replaces the running exe with it.
/// Only builds made by GitHub know their version (the commit), so a program you built yourself in Visual Studio never updates itself.
/// </summary>
static class Updater
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
            var v = typeof(Updater).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
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

    /// <summary>Downloads the newest exe into a temporary folder and returns its path.</summary>
    public static async Task<string> DownloadAsync()
    {
        var dir = Path.Combine(Path.GetTempPath(), "HaulingTrackerUpdate-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var zip = Path.Combine(dir, "HaulingTracker.zip");
        using (var resp = await Http.GetAsync("https://github.com/" + Repo + "/releases/download/latest/HaulingTracker.zip", HttpCompletionOption.ResponseHeadersRead))
        {
            resp.EnsureSuccessStatusCode();
            await using var file = File.Create(zip);
            await resp.Content.CopyToAsync(file);
        }
        ZipFile.ExtractToDirectory(zip, dir);
        var exe = Path.Combine(dir, "HaulingTracker.exe");
        if (!File.Exists(exe)) throw new FileNotFoundException("The download does not contain HaulingTracker.exe.");
        return exe;
    }

    /// <summary>
    /// Windows does not let a program overwrite itself while it runs, but it does let the running exe be renamed. So the running exe becomes
    /// HaulingTracker.old.exe (kept as a backup until the next update), the new exe takes its place and is started.
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

public sealed partial class MainForm
{
    readonly ToolStripMenuItem _versionItem = new() { Enabled = false };
    readonly ToolStripMenuItem _updateBtn = new();
    bool _updateBusy;

    void InitUpdater()
    {
        var own = Updater.OwnCommit;
        _versionItem.Text = own == null ? "Version: built on this computer" : "Version: " + own[..7];
        _updateBtn.Text = "Check for Updates…";
        _updateBtn.Click += async (_, _) => await CheckForUpdateAsync(true);
        Shown += async (_, _) => await CheckForUpdateAsync(false);
    }

    /// <summary>
    /// Asks GitHub for the newest build. At startup it says nothing unless there is something newer (and then asks first);
    /// from the menu it always answers.
    /// </summary>
    async Task CheckForUpdateAsync(bool manual)
    {
        if (_updateBusy) return;
        _updateBusy = true;
        var asked = false;
        try
        {
            var own = Updater.OwnCommit;
            if (own == null)
            {
                if (manual) MessageBox.Show(this, "This program was built on your own computer, so it does not update itself.", "Update", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var latest = await Updater.LatestCommitAsync(cts.Token);
            if (latest == null)
            {
                if (manual) MessageBox.Show(this, "Could not read the newest version from GitHub.", "Update", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (!await Updater.IsNewerAsync(own, latest, cts.Token))
            {
                if (manual) MessageBox.Show(this, "You have the newest version.", "Update", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            asked = true;
            var answer = MessageBox.Show(this,
                "A newer version of the Hauling Session Tracker is available.\n\nUpdate now? The program restarts. Your data is not touched, and the old program is kept as HaulingTracker.old.exe.",
                "Update", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.Yes) return;

            SetStatus("Downloading the update…");
            var exe = await Updater.DownloadAsync();
            SetStatus("Installing the update…");
            Updater.ReplaceAndStart(exe);
            Application.Exit();
        }
        catch (Exception ex)
        {
            SetStatus("The update did not work: " + ex.Message);
            if (manual || asked) MessageBox.Show(this, "The update did not work: " + ex.Message, "Update", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            _updateBusy = false;
        }
    }
}
