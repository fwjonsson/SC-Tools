using System.Text;

namespace HaulingTracker.GameLog;

/// <summary>
/// The Game.log files the program reads: log files opened by hand (one log per player, so the logs of two accounts stay apart), or the live sources that are
/// watched (a Game.log, a folder with one, or the StarCitizen folder with LIVE, HOTFIX... inside; it can be on another computer, like \\PCNAME\StarCitizen).
/// A live source follows its Game.log while the game writes it, reads the recent backup logs when it starts, and reads a new Game.log from the top.
/// </summary>
public sealed class LogSources
{
    /// <summary>The backup logs that are read when a source starts: the ones written in the last day.</summary>
    public static readonly TimeSpan BackupAge = TimeSpan.FromHours(24);

    /// <summary>A live source and how far it has been read.</summary>
    sealed class Source
    {
        public string Path = "";
        public long Pos;
        public Decoder Dec = Encoding.UTF8.GetDecoder();
        public string Carry = "";
        public HaulingLog Log = new();
        public string? Current;     // the Game.log file that is being read right now
    }

    readonly List<HaulingLog> _logs = new();
    readonly List<Source> _sources = new();

    /// <summary>One log per player (files opened by hand) or per live source.</summary>
    public IReadOnlyList<HaulingLog> Logs => _logs;

    /// <summary>Changes whenever anything new is read, so the program knows when to work everything out again.</summary>
    public int Version => _logs.Sum(l => l.Version) + _logs.Count * 1000003;

    /// <summary>The time of the last line read (UTC), or the minimum time when nothing is read.</summary>
    public DateTime LastLine => _logs.Count == 0 ? DateTime.MinValue : _logs.Max(l => l.LastTime);

    /// <summary>Nothing is read any more.</summary>
    public void Clear()
    {
        _logs.Clear();
        _sources.Clear();
    }

    /// <summary>Reads log files (from any of your computers) in the order they were written: one log per player, so logs from two accounts stay apart.</summary>
    public void LoadFiles(IEnumerable<string> paths)
    {
        Clear();
        var files = paths.OrderBy(f => HaulingLog.FirstTimestamp(f) ?? DateTime.MaxValue).ToList();
        var byPlayer = new Dictionary<string, HaulingLog>();
        foreach (var f in files)
        {
            var player = HaulingLog.FirstPlayer(f) ?? "Unknown player";
            if (!byPlayer.TryGetValue(player, out var log))
            {
                log = new HaulingLog();
                byPlayer[player] = log;
                _logs.Add(log);
            }
            log.ProcessFile(f);
        }
    }

    /// <summary>From now on these live sources are followed, every one in a log of its own (nothing is read until Poll).</summary>
    public void Watch(IEnumerable<string> paths)
    {
        Clear();
        foreach (var path in paths)
        {
            var src = new Source { Path = path };
            _sources.Add(src);
            _logs.Add(src.Log);
        }
    }

    /// <summary>Reads what is new in every live source. The channel (Auto, LIVE, HOTFIX, ...) picks the Game.log in a StarCitizen folder.</summary>
    public void Poll(string? channel)
    {
        foreach (var src in _sources)
        {
            try
            {
                var file = ResolveLog(src.Path, channel);
                if (file == null) continue;
                if (file != src.Current)
                {
                    // after a restart of this program or of the game: read the recent backup logs first,
                    // so a hauling session that was already going on is not lost
                    Backfill(src, file);

                    // switched channel (LIVE / HOTFIX) or a new location: read the new file from the top
                    src.Current = file;
                    src.Pos = 0;
                    src.Carry = "";
                    src.Dec = Encoding.UTF8.GetDecoder();
                }
                var info = new FileInfo(file);
                if (!info.Exists) continue;

                // A new Game.log starts empty. Keep the missions we already know and read it from the top.
                if (info.Length < src.Pos)
                {
                    src.Pos = 0;
                    src.Carry = "";
                    src.Dec = Encoding.UTF8.GetDecoder();
                }
                if (info.Length <= src.Pos) continue;

                using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                fs.Seek(src.Pos, SeekOrigin.Begin);
                var buffer = new byte[1 << 16];
                var chars = new char[Encoding.UTF8.GetMaxCharCount(buffer.Length)];
                int read;
                while ((read = fs.Read(buffer, 0, buffer.Length)) > 0)
                {
                    src.Pos += read;
                    var n = src.Dec.GetChars(buffer, 0, read, chars, 0);
                    Feed(src, new string(chars, 0, n));
                }
            }
            catch (IOException)
            {
                // The game may be writing at the same moment, or the other computer is offline. Try again next tick.
            }
        }
    }

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

    /// <summary>Reads the logs in the logbackups folder next to Game.log that are less than a day old.</summary>
    static void Backfill(Source src, string liveFile)
    {
        try
        {
            var dir = Path.Combine(Path.GetDirectoryName(liveFile) ?? "", "logbackups");
            if (!Directory.Exists(dir)) return;

            var cutoff = DateTime.UtcNow - BackupAge;
            var files = Directory.GetFiles(dir, "*.log")
                .Where(f => File.GetLastWriteTimeUtc(f) >= cutoff)
                .OrderBy(f => HaulingLog.FirstTimestamp(f) ?? DateTime.MaxValue)
                .ToList();

            foreach (var f in files)
            {
                try { src.Log.ProcessFile(f); }
                catch (IOException) { }
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>Passes the whole lines of new text to the log; a line that is not finished yet waits for the rest.</summary>
    static void Feed(Source src, string text)
    {
        var data = src.Carry + text;
        var start = 0;
        int nl;
        while ((nl = data.IndexOf('\n', start)) >= 0)
        {
            src.Log.ProcessLine(data.Substring(start, nl - start).TrimEnd('\r'));
            start = nl + 1;
        }
        src.Carry = data.Substring(start);
    }
}
