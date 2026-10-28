using SCTools.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace SCTools.Server;

/// <summary>
/// The Gauntlet Tracker's data. The gauntlet missions that ended in the uploaded logs (LogUploads.GauntletRuns) are written to the database, so they stay
/// after the logs are no longer kept; a mission is kept once per player. Also the settings and the grind sessions of every player.
/// </summary>
public sealed class GauntletService
{
    readonly DataStore _store;
    readonly LogUploads _uploads;
    readonly object _lock = new();
    HashSet<(string, string)>? _known;

    public GauntletService(DataStore store, LogUploads uploads)
    {
        _store = store;
        _uploads = uploads;
    }

    /// <summary>Writes the missions the logs show that the database does not have yet. Returns how many were new.</summary>
    public int Collect()
    {
        var found = _uploads.GauntletRuns();
        lock (_lock)
        {
            using var db = _store.Open();
            _known ??= db.GauntletRuns.AsNoTracking().Select(r => new { r.Player, r.MissionId }).AsEnumerable()
                .Select(r => (r.Player.ToLowerInvariant(), r.MissionId.ToLowerInvariant())).ToHashSet();
            var added = 0;
            foreach (var (player, runs) in found)
            {
                foreach (var r in runs)
                {
                    if (!_known.Add((player.ToLowerInvariant(), r.MissionId.ToLowerInvariant()))) continue;
                    db.GauntletRuns.Add(new GauntletRunRow
                    {
                        Player = player, MissionId = r.MissionId, Scenario = r.Scenario,
                        AcceptedUtc = r.AcceptedUtc, EndedUtc = r.EndedUtc, Outcome = r.Outcome.ToString(),
                    });
                    added++;
                }
            }
            if (added > 0) db.SaveChanges();
            return added;
        }
    }

    /// <summary>All the runs of a player, oldest first.</summary>
    public List<GauntletRun> Runs(string player)
    {
        lock (_lock)
        {
            using var db = _store.Open();
            var rows = db.GauntletRuns.AsNoTracking().Where(r => r.Player == player).ToList();
            return rows.OrderBy(r => r.EndedUtc)
                .Select(r => new GauntletRun(r.MissionId, r.Scenario, r.AcceptedUtc, r.EndedUtc,
                    Enum.TryParse<GauntletOutcome>(r.Outcome, out var o) ? o : GauntletOutcome.Abandon))
                .ToList();
        }
    }

    /// <summary>The players with a tracker or runs, by name.</summary>
    public List<string> Players()
    {
        lock (_lock)
        {
            using var db = _store.Open();
            var names = db.GauntletRuns.AsNoTracking().Select(r => r.Player).Distinct().ToList();
            names.AddRange(_uploads.GauntletRuns().Keys);
            return names.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        }
    }

    public GauntletSettingsRow Settings(string player)
    {
        lock (_lock)
        {
            using var db = _store.Open();
            return db.GauntletSettings.AsNoTracking().SingleOrDefault(s => s.Player == player) ?? new GauntletSettingsRow { Player = player };
        }
    }

    public void SaveSettings(GauntletSettingsRow settings)
    {
        lock (_lock)
        {
            using var db = _store.Open();
            var old = db.GauntletSettings.SingleOrDefault(s => s.Player == settings.Player);
            if (old == null) db.GauntletSettings.Add(settings);
            else db.Entry(old).CurrentValues.SetValues(settings);
            db.SaveChanges();
        }
    }

    /// <summary>The rewards of this player: the defaults with his changes.</summary>
    public static Dictionary<int, GauntletReward> Rewards(GauntletSettingsRow s)
    {
        var map = new Dictionary<int, GauntletReward>();
        for (var n = 1; n <= GauntletRewards.Scenarios; n++)
        {
            var d = GauntletRewards.Defaults[n];
            map[n] = new GauntletReward(s.RewardAuec.GetValueOrDefault(n, d.Auec), s.RewardScrip.GetValueOrDefault(n, d.Scrip));
        }
        return map;
    }

    /// <summary>The grind sessions of a player, newest first.</summary>
    public List<GauntletSessionRow> Sessions(string player)
    {
        lock (_lock)
        {
            using var db = _store.Open();
            return db.GauntletSessions.AsNoTracking().Where(s => s.Player == player).ToList().OrderByDescending(s => s.StartedUtc).ToList();
        }
    }

    /// <summary>Starts a grind session (none when one is running already).</summary>
    public void Start(string player, DateTime now)
    {
        lock (_lock)
        {
            using var db = _store.Open();
            if (db.GauntletSessions.Any(s => s.Player == player && s.EndedUtc == null)) return;
            db.GauntletSessions.Add(new GauntletSessionRow { Player = player, StartedUtc = now });
            db.SaveChanges();
        }
    }

    /// <summary>Ends the running grind session.</summary>
    public void Stop(string player, DateTime now)
    {
        lock (_lock)
        {
            using var db = _store.Open();
            var running = db.GauntletSessions.SingleOrDefault(s => s.Player == player && s.EndedUtc == null);
            if (running == null) return;
            running.EndedUtc = now;
            db.SaveChanges();
        }
    }
}
