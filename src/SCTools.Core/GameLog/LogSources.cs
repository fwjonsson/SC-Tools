using System.Text;

namespace SCTools.GameLog;

/// <summary>
/// Where the Game.log of a Star Citizen folder is (the tracker and its old logs ask): a Game.log, a folder with one, or the StarCitizen folder with LIVE,
/// HOTFIX... inside (it can be on another computer, like \\PCNAME\StarCitizen). The desktop program's log watcher that was here went with the program.
/// </summary>
public static class LogSources
{
    /// <summary>The backup logs that are read when a source starts: the ones written in the last day.</summary>
    public static readonly TimeSpan BackupAge = TimeSpan.FromHours(24);

    /// <summary>
    /// The Game.log of a watch path: the file itself, the Game.log in the folder, or in a StarCitizen folder the one of the channel (Auto: the one written last).
    /// Null when there is none, or the path cannot be reached right now.
    /// </summary>
    public static string? ResolveLog(string path, string? channel)
    {
        try
        {
            if (File.Exists(path)) return path;
            if (!Directory.Exists(path)) return null;

            var direct = Path.Combine(path, "Game.log");
            if (File.Exists(direct)) return direct;

            var found = Directory.GetDirectories(path)
                .Select(d => (Name: Path.GetFileName(d), File: Path.Combine(d, "Game.log")))
                .Where(x => File.Exists(x.File))
                .ToList();

            if (!string.IsNullOrEmpty(channel) && channel != "Auto")
            {
                var pick = found.FirstOrDefault(x => string.Equals(x.Name, channel, StringComparison.OrdinalIgnoreCase));
                return pick.File;   // null when that channel has no log
            }
            return found.OrderByDescending(x => File.GetLastWriteTimeUtc(x.File)).Select(x => x.File).FirstOrDefault();
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

}
