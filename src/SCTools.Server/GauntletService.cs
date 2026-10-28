using SCTools.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace SCTools.Server;

/// <summary>
/// The Gauntlet Tracker's data. The gauntlet missions that ended in the uploaded logs (LogUploads.GauntletRuns) are written to the database, so they stay
/// after the logs are no longer kept; a mission is kept once per player. Also the grind sessions of every player, and the settings (the rewards and what a
/// MG scrip is worth), which are the same for everybody and set by the admin, so the leaderboard compares like with like.
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
    public int Collect() => Add(_uploads.GauntletRuns());

    /// <summary>Writes these missions where the database does not have them yet (the live logs, or an old log a tracker sent). Returns how many were new.</summary>
    public int Add(IReadOnlyDictionary<string, List<GauntletRun>> found)
    {
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
            return rows.OrderBy(r => r.EndedUtc).Select(Run).ToList();
        }
    }

    /// <summary>The settings are kept under this name: they are everybody's.</summary>
    public const string Everyone = "";

    /// <summary>
    /// The settings of everybody (the admin's Gauntlet page). Before they were everybody's, every player had his own: until the admin saves, the first that
    /// somebody saved are taken, so nothing typed is lost.
    /// </summary>
    public GauntletSettingsRow Settings()
    {
        lock (_lock)
        {
            using var db = _store.Open();
            var rows = db.GauntletSettings.AsNoTracking().ToList();
            var row = rows.FirstOrDefault(s => s.Player == Everyone) ?? rows.FirstOrDefault() ?? new GauntletSettingsRow();
            row.Player = Everyone;
            return row;
        }
    }

    public void SaveSettings(GauntletSettingsRow settings)
    {
        settings.Player = Everyone;
        lock (_lock)
        {
            using var db = _store.Open();
            var old = db.GauntletSettings.SingleOrDefault(s => s.Player == Everyone);
            if (old == null) db.GauntletSettings.Add(settings);
            else db.Entry(old).CurrentValues.SetValues(settings);
            db.SaveChanges();
        }
    }

    /// <summary>The runs of every player, by player.</summary>
    public Dictionary<string, List<GauntletRun>> AllRuns()
    {
        lock (_lock)
        {
            using var db = _store.Open();
            return db.GauntletRuns.AsNoTracking().ToList()
                .GroupBy(r => r.Player, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.OrderBy(r => r.EndedUtc).Select(Run).ToList(), StringComparer.OrdinalIgnoreCase);
        }
    }

    static GauntletRun Run(GauntletRunRow r) =>
        new(r.MissionId, r.Scenario, r.AcceptedUtc, r.EndedUtc, Enum.TryParse<GauntletOutcome>(r.Outcome, out var o) ? o : GauntletOutcome.Abandon);

    /// <summary>The rewards: the defaults with the admin's changes.</summary>
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
