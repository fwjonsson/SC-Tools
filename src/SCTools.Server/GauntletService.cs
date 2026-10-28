using SCTools.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace SCTools.Server;

/// <summary>
/// The Gauntlet Tracker's data. The gauntlet missions that ended in the uploaded logs (LogUploads.GauntletRuns) are written to the database, so they stay
/// after the logs are no longer kept; a mission is kept once per player. The grind sessions come from them (they start and stop by themselves). The rewards of the scenarios are in the
/// Mission Library (the same for everybody, so the leaderboard compares like with like); what a MG scrip is worth is on the admin's Scrip page.
/// </summary>
public sealed class GauntletService
{
    readonly DataStore _store;
    readonly LogUploads _uploads;
    readonly MissionLibrary _library;
    readonly object _lock = new();
    HashSet<(string, string)>? _known;

    public GauntletService(DataStore store, LogUploads uploads, MissionLibrary library)
    {
        _store = store;
        _uploads = uploads;
        _library = library;
    }

    /// <summary>The key of a scenario's kind in the Mission Library.</summary>
    public static string LibraryKey(int scenario) => SCTools.Missions.MissionKinds.TitleKey(GauntletRewards.Title(scenario))!;

    /// <summary>
    /// The rewards of the scenarios: the Mission Library's (a reward nobody knows counts 0). The first time, the library gets the ones the admin's Gauntlet
    /// page had (the built-in ones with his changes), for the scenarios nobody typed in there yet.
    /// </summary>
    public Dictionary<int, GauntletReward> Rewards()
    {
        var before = Rewards(Settings());
        var kinds = _library.Kinds(Enumerable.Range(1, GauntletRewards.Scenarios)
            .Select(n => new MissionKindRow { Key = LibraryKey(n), Title = GauntletRewards.Title(n), Auec = before[n].Auec, MgScrip = before[n].Scrip }).ToList());
        return Enumerable.Range(1, GauntletRewards.Scenarios).ToDictionary(n => n, n =>
        {
            var k = kinds[LibraryKey(n)];
            return new GauntletReward(k.Auec ?? 0, k.MgScrip ?? 0);
        });
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

    /// <summary>The rewards the admin's Gauntlet page had before they were in the Mission Library: the defaults with his changes.</summary>
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

    /// <summary>
    /// The grind sessions of a player, newest first: they start and stop by themselves (AutoSessions). One starts when he accepts a scenario and goes on while
    /// he accepts, completes or abandons them; after an hour with nothing it ended 5 minutes after the last one. Worked out from his runs (kept for good) and
    /// the scenario he is in now, so the earlier ones are there too.
    /// </summary>
    public List<SCTools.Sessions.AutoSessions.Window> GrindSessions(string player, DateTime nowUtc)
    {
        var runs = Runs(player);
        var going = _uploads.GauntletAccepted().GetValueOrDefault(player) ?? Array.Empty<DateTime>();
        var starts = runs.Select(r => r.AcceptedUtc ?? r.EndedUtc).Concat(going).ToList();
        var activity = runs.Select(r => r.EndedUtc).Concat(starts);
        var found = SCTools.Sessions.AutoSessions.Find(starts, activity, nowUtc);
        found.Reverse();
        return found;
    }
}
