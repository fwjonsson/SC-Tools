using System.Diagnostics;

namespace SCTools.Updates;

/// <summary>
/// A program of SC Tools in the release: what it is called, its exe and its zip, and the exe it was before the rename. The release still has a zip under
/// the old name (the same program), so a program from before updates as it always did, and then renames itself when it starts (MoveAndRestart).
/// </summary>
public sealed record AppName(string Title, string Name, string OldName)
{
    /// <summary>The SC Tools Tracker on the gaming PCs: it sends what every tool of the site needs. It was HaulingTrackerSender.</summary>
    public static readonly AppName Tracker = new("SC Tools Tracker", "SCToolsTracker", "HaulingTrackerSender");

    /// <summary>The Hauling Planner, the Windows program. It was HaulingTracker.</summary>
    public static readonly AppName Planner = new("Hauling Planner", "HaulingPlanner", "HaulingTracker");

    public string Exe => Name + ".exe";
    public string Zip => Name + ".zip";
    public string OldExe => OldName + ".exe";

    /// <summary>Where a program that is still under its old name moves to (the new name next to it), or null when it has its name already.</summary>
    public string? MovedPath(string exePath) =>
        string.Equals(Path.GetFileName(exePath), OldExe, StringComparison.OrdinalIgnoreCase) ? Path.Combine(Path.GetDirectoryName(exePath) ?? "", Exe) : null;

    /// <summary>
    /// The copies an update leaves next to the exe: the program before the update (Name.old.exe), the same from under the old name, and the new exe of an
    /// update that did not get as far as putting it in place (Name.new.exe, Updater.DownloadAsync).
    /// </summary>
    public IReadOnlyList<string> Leftovers(string exePath)
    {
        var dir = Path.GetDirectoryName(exePath) ?? "";
        return new[]
            {
                Path.ChangeExtension(exePath, ".old.exe"), Path.Combine(dir, Name + ".old.exe"), Path.Combine(dir, OldName + ".old.exe"),
                Path.ChangeExtension(exePath, ".new.exe"), Path.Combine(dir, Name + ".new.exe"),
            }
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// What updates left in Temp (SCToolsUpdate-*, Updater.TempName) that is older than a day, so an update running right now keeps its zip: the folders the
    /// updates before 29 Oct 2026 made (the zip and the exe stayed in them, 46 MB each), and the zip of an update that was cut short.
    /// </summary>
    public static IReadOnlyList<string> TempLeftovers(string? tempPath = null, DateTime? now = null)
    {
        try
        {
            var before = (now ?? DateTime.UtcNow) - TimeSpan.FromDays(1);
            return Directory.GetFileSystemEntries(tempPath ?? Path.GetTempPath(), Updater.TempName + "*")
                .Where(path => File.GetLastWriteTimeUtc(path) < before)
                .ToList();
        }
        catch (IOException) { return Array.Empty<string>(); }
        catch (UnauthorizedAccessException) { return Array.Empty<string>(); }
    }

    /// <summary>
    /// Removes what updates left behind, when the program starts: the copies next to the exe (Leftovers) and the old files in Temp (TempLeftovers). The
    /// program it replaced may still be closing, and OneDrive may hold a file for a moment while it syncs it, so it tries again a few times, in the
    /// background. Anything still in the way goes the next time.
    /// </summary>
    public async Task RemoveLeftoversAsync(string? exePath = null, int tries = 10, TimeSpan? wait = null, string? tempPath = null)
    {
        exePath ??= Environment.ProcessPath;
        if (exePath == null) return;
        var left = Leftovers(exePath).Concat(TempLeftovers(tempPath)).ToList();
        for (var i = 0; i < tries && left.Count > 0; i++)
        {
            if (i > 0) await Task.Delay(wait ?? TimeSpan.FromSeconds(3));
            left.RemoveAll(Removed);
        }
    }

    static bool Removed(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, true);
            else if (File.Exists(path)) File.Delete(path);
            return true;
        }
        catch (Exception)
        {
            return false;   // still in use
        }
    }

    /// <summary>
    /// A program still under its old name renames itself (Windows lets a running exe be renamed), says so (moved: Start with Windows goes along), and
    /// starts again under the new name (which removes the backup of the old one, RemoveLeftoversAsync). True when it did: the caller then ends. Anything in the way: it keeps its old name.
    /// </summary>
    public bool MoveAndRestart(Action<string>? moved = null)
    {
        try
        {
            var me = Environment.ProcessPath;
            if (me == null || MovedPath(me) is not { } target || File.Exists(target)) return false;
            File.Move(me, target);
            moved?.Invoke(target);
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(target) ?? "" });
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
