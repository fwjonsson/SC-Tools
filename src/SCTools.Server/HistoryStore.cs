using System.Globalization;
using System.IO.Compression;
using System.Text;
using SCTools.Server.Data;

namespace SCTools.Server;

/// <summary>
/// The old logs the trackers send (HistorySender): every backup log on a player's PC once, so the tools that keep everything know what he played before
/// his tracker ran. They are kept apart from the live logs (the Hauling Planner never reads them): their lines in the history folder (gzip, one folder per
/// player), and a row per log in LogFiles (when the game ran, its build). The Gauntlet runs in a log go to the database straight away. A log of another
/// player (somebody else played on that PC) is noted and not read.
/// </summary>
public sealed class HistoryStore
{
    readonly DataStore _store;
    readonly GauntletService _gauntlet;
    readonly string _dir;
    readonly object _lock = new();

    public HistoryStore(DataStore store, GauntletService gauntlet, ServerPaths paths)
    {
        _store = store;
        _gauntlet = gauntlet;
        _dir = paths.HistoryDir;
    }

    /// <summary>Which of these logs of the player the server does not have at the current version.</summary>
    public List<string> Missing(string player, IEnumerable<string> files)
    {
        lock (_lock)
        {
            using var db = _store.Open();
            var have = db.LogFiles.Where(f => f.Player == player && f.Version >= HistorySender.Version).Select(f => f.File).ToHashSet(StringComparer.Ordinal);
            return files.Where(f => !string.IsNullOrWhiteSpace(f) && !have.Contains(f)).Distinct(StringComparer.Ordinal).ToList();
        }
    }

    /// <summary>A piece of an old log: its lines from line number from on. The last piece reads the whole log and keeps it.</summary>
    public UploadResult Add(string player, string file, long from, IReadOnlyList<string> lines, bool done, DateTime? last, string? build, DateTime nowUtc)
    {
        if (!DateTime.TryParse(file, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var start))
            throw new ArgumentException("An old log is named by the time of its first line.", nameof(file));
        lock (_lock)
        {
            var dir = Path.Combine(_dir, Safe(player));
            Directory.CreateDirectory(dir);
            var part = Path.Combine(dir, Safe(file) + ".part");
            var have = File.Exists(part) ? File.ReadLines(part).LongCount() : 0;
            if (from == 0 && have > 0) { File.Delete(part); have = 0; }   // sent again from the start
            if (from != have) return new UploadResult(false, have);
            File.AppendAllLines(part, lines, Encoding.UTF8);
            if (!done) return new UploadResult(true, from + lines.Count);

            var all = File.ReadAllLines(part, Encoding.UTF8);
            var log = OldLog.Read(all);
            var other = log.Player != null && !string.Equals(log.Player, player, StringComparison.OrdinalIgnoreCase);
            if (!other && log.GauntletRuns.Count > 0)
                _gauntlet.Add(new Dictionary<string, List<GauntletRun>> { [player] = log.GauntletRuns.ToList() });

            using (var gz = new GZipStream(File.Create(Path.Combine(dir, Safe(file) + ".log.gz")), CompressionLevel.Optimal))
            using (var w = new StreamWriter(gz, Encoding.UTF8))
                foreach (var line in all) w.WriteLine(line);
            File.Delete(part);

            using var db = _store.Open();
            var row = db.LogFiles.Find(player, file);
            if (row == null) db.LogFiles.Add(row = new LogFileRow { Player = player, File = file });
            row.Version = HistorySender.Version;
            row.StartUtc = DateTime.SpecifyKind(start, DateTimeKind.Utc);
            row.EndUtc = (last ?? log.LastLine)?.ToUniversalTime();
            row.Build = build;
            row.Lines = all.Length;
            row.ImportedUtc = nowUtc;
            row.Note = other ? log.Player + "'s game" : null;
            db.SaveChanges();
            return new UploadResult(true, from + lines.Count);
        }
    }

    /// <summary>The old logs of a player, oldest first.</summary>
    public List<LogFileRow> Files(string player)
    {
        lock (_lock)
        {
            using var db = _store.Open();
            return db.LogFiles.Where(f => f.Player == player).OrderBy(f => f.File).ToList();
        }
    }

    static string Safe(string name)
    {
        var bad = Path.GetInvalidFileNameChars();
        return new string(name.Select(c => bad.Contains(c) || c == ':' ? '_' : c).ToArray());
    }
}
