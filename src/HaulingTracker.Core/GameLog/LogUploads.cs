using System.Globalization;

namespace HaulingTracker.GameLog;

/// <summary>What became of an upload: taken (the cursor is now At), or not (the tracker has to send from At).</summary>
public readonly record struct UploadResult(bool Ok, long At);

/// <summary>
/// The logs the trackers upload to the server: one log per player, fed with the lines in the order they arrive. A tracker uploads a log file in pieces of whole
/// lines, each from the cursor the server has for that file (where the last piece ended, in bytes of the file), so nothing is read twice or skipped, also when a
/// tracker restarts. A file is named by the time of its first line, so the Game.log of a game and its copy in logbackups are the same file. What was uploaded
/// is kept on disk (one file per player and log file, with its cursor next to it), so a restart of the server reads it again.
/// </summary>
public sealed class LogUploads
{
    sealed class Feed
    {
        public HaulingLog Log = new();
        public Dictionary<string, long> At = new(StringComparer.Ordinal);   // log file -> how far it is uploaded (bytes of the file)
    }

    readonly string? _dir;
    readonly Dictionary<string, Feed> _feeds = new(StringComparer.OrdinalIgnoreCase);
    readonly List<HaulingLog> _logs = new();

    /// <param name="dir">where the uploads are kept (null: only in memory)</param>
    public LogUploads(string? dir = null) => _dir = dir;

    /// <summary>Taken while the logs are read or fed (uploads come in on other threads than the one that works them out).</summary>
    public object Sync { get; } = new();

    /// <summary>One log per player.</summary>
    public IReadOnlyList<HaulingLog> Logs => _logs;

    /// <summary>Changes whenever anything new is read.</summary>
    public int Version
    {
        get
        {
            lock (Sync) return _logs.Sum(l => l.Version) + _logs.Count * 1000003;
        }
    }

    /// <summary>How far this log file of this player is uploaded (0: not yet).</summary>
    public long Cursor(string player, string file)
    {
        lock (Sync) return _feeds.TryGetValue(player, out var f) ? f.At.GetValueOrDefault(file) : 0;
    }

    /// <summary>
    /// A piece of a log file: the lines between the byte positions from and to of the file. Taken only when it starts where the server's cursor is; otherwise
    /// nothing changes and the answer says where to start.
    /// </summary>
    public UploadResult Add(string player, string file, long from, long to, IReadOnlyList<string> lines)
    {
        if (string.IsNullOrWhiteSpace(player) || string.IsNullOrWhiteSpace(file)) throw new ArgumentException("A player and a file are needed.");
        lock (Sync)
        {
            var feed = FeedOf(player);
            var at = feed.At.GetValueOrDefault(file);
            if (from != at || to < from) return new UploadResult(false, at);
            foreach (var line in lines) feed.Log.ProcessLine(line.TrimEnd('\r'));
            feed.At[file] = to;
            Keep(player, file, to, lines);
            return new UploadResult(true, to);
        }
    }

    /// <summary>Reads again what was uploaded and kept on disk in the last while (the log files written to in that time), each player's in the order of the files.</summary>
    public void LoadKept(TimeSpan maxAge)
    {
        if (_dir == null || !Directory.Exists(_dir)) return;
        lock (Sync)
        {
            var since = DateTime.UtcNow - maxAge;
            foreach (var playerDir in Directory.GetDirectories(_dir))
            {
                var feed = FeedOf(Path.GetFileName(playerDir));
                var files = Directory.GetFiles(playerDir, "*.log")
                    .Where(f => File.GetLastWriteTimeUtc(f) >= since)
                    .OrderBy(f => HaulingLog.FirstTimestamp(f) ?? DateTime.MaxValue)
                    .ToList();
                foreach (var path in files)
                {
                    var pos = path[..^4] + ".pos";
                    if (!File.Exists(pos)) continue;
                    var kept = File.ReadAllLines(pos);
                    if (kept.Length < 2 || !long.TryParse(kept[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var at)) continue;
                    feed.Log.ProcessFile(path);
                    feed.At[kept[0]] = at;
                }
            }
        }
    }

    Feed FeedOf(string player)
    {
        if (_feeds.TryGetValue(player, out var feed)) return feed;
        feed = new Feed();
        _feeds[player] = feed;
        _logs.Add(feed.Log);
        return feed;
    }

    /// <summary>Appends the lines to the player's copy of that log file and keeps the cursor next to it (the file's name and how far it is uploaded).</summary>
    void Keep(string player, string file, long at, IReadOnlyList<string> lines)
    {
        if (_dir == null) return;
        var dir = Path.Combine(_dir, SafeName(player));
        Directory.CreateDirectory(dir);
        var name = Path.Combine(dir, SafeName(file));
        if (lines.Count > 0) File.AppendAllLines(name + ".log", lines.Select(l => l.TrimEnd('\r')));
        else if (!File.Exists(name + ".log")) File.WriteAllText(name + ".log", "");
        File.WriteAllLines(name + ".pos", new[] { file, at.ToString(CultureInfo.InvariantCulture) });
    }

    /// <summary>A name that is safe as a file name: letters, digits, '-', '_' and '.'; everything else becomes '_'.</summary>
    public static string SafeName(string s) => new(s.Select(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '_').ToArray());
}
