using SCTools.Play;
using SCTools.Server.Data;

namespace SCTools.Server;

/// <summary>
/// The games every player played, kept for good in LogFiles: the old logs a tracker sent (HistoryStore), and the games the live logs show while they run
/// (Collect: their start, the last time they were seen, quit or not; written with version 0, so the backup log, which has all of it, still comes later and
/// takes over). A game of another player on the same PC is not his.
/// </summary>
public sealed class Playtime
{
    readonly DataStore _store;
    readonly LogUploads _uploads;
    readonly object _lock = new();
    readonly Dictionary<(string, string), (DateTime End, bool Quit)> _written = new();

    public Playtime(DataStore store, LogUploads uploads)
    {
        _store = store;
        _uploads = uploads;
    }

    long _collected = -1;   // Uploads.Pieces when the live logs were last looked at

    /// <summary>Writes the games of the live logs that are new or went on (never over a game its backup log told). Returns how many were written.</summary>
    public int Collect()
    {
        // a game's length and whether it was quit only change when a piece of its log comes in: nothing new, nothing to look at (this runs every two seconds)
        var pieces = _uploads.Pieces;
        if (pieces == _collected) return 0;
        var games = _uploads.Games();
        lock (_lock)
        {
            var changed = new List<(string Player, string File, GameRun Run, bool Quit)>();
            foreach (var (player, list) in games)
                foreach (var (file, run, quit) in list)
                {
                    var key = (player.ToLowerInvariant(), file);
                    if (_written.TryGetValue(key, out var w) && w.Quit == quit && run.Last - w.End < TimeSpan.FromMinutes(1)) continue;   // a minute at a time
                    changed.Add((player, file, run, quit));
                }
            if (changed.Count == 0)
            {
                _collected = pieces;
                return 0;
            }
            using var db = _store.Open();
            var written = 0;
            foreach (var (player, file, run, quit) in changed)
            {
                var row = db.LogFiles.Find(player, file);
                if (row != null && row.Version > 0) continue;   // its backup log told all of it already
                if (row == null) db.LogFiles.Add(row = new LogFileRow { Player = player, File = file, ImportedUtc = DateTime.UtcNow });
                row.Version = 0;
                row.StartUtc = run.Start;
                row.EndUtc = run.Last;
                row.Quit = quit;
                written++;
            }
            db.SaveChanges();
            // noted once the database took it: a write that fails is tried again next time
            foreach (var (player, file, run, quit) in changed) _written[(player.ToLowerInvariant(), file)] = (run.Last, quit);
            _collected = pieces;
            return written;
        }
    }

    /// <summary>The games of this player, newest first; the newest is Running while its log still grows (it was seen in the last fifteen minutes, not quit).</summary>
    public List<PlayedGame> Games(string player, DateTime nowUtc)
    {
        Collect();
        List<LogFileRow> rows;
        lock (_lock)
        {
            using var db = _store.Open();
            rows = db.LogFiles.Where(f => f.Player == player && f.Note == null).ToList();
        }
        return rows.Select(r =>
            {
                var end = r.EndUtc ?? r.StartUtc;
                var running = r.Version == 0 && r.Quit != true && nowUtc - end < AutoParty.Quiet;
                return new PlayedGame(r.File, r.StartUtc, running ? nowUtc : end, r.Quit, running);
            })
            .OrderByDescending(g => g.StartUtc).ToList();
    }
}
