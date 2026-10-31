using SCTools.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace SCTools.Server;

/// <summary>
/// The Gauntlet Tracker's data. The gauntlet missions that ended in the uploaded logs (LogUploads.GauntletRuns) are written to the database, so they stay
/// after the logs are no longer kept; a mission is kept once per player. The grind sessions come from them (they start and stop by themselves). The rewards of the scenarios are in the
/// Mission Library (the same for everybody, so the leaderboard compares like with like); what a MG scrip is worth is on the admin's Scrip Value page. What each
/// player's run earned him is the contracts tracker's (ContractSplit): a scenario shared in the party pays every member who completed it his share of the
/// aUEC, and the MG scrip only to the one who accepted it (Frank, 30 Oct 2026).
/// </summary>
public sealed class GauntletService
{
    readonly DataStore _store;
    readonly LogUploads _uploads;
    readonly MissionLibrary _library;
    readonly ContractTracker _contracts;
    readonly object _lock = new();
    HashSet<(string, string)>? _known;

    public GauntletService(DataStore store, LogUploads uploads, MissionLibrary library, ContractTracker contracts)
    {
        _store = store;
        _uploads = uploads;
        _library = library;
        _contracts = contracts;
    }

    /// <summary>The key of a scenario's kind in the Mission Library.</summary>
    public static string LibraryKey(int scenario) => SCTools.Missions.MissionKinds.TitleKey(GauntletRewards.Title(scenario))!;

    /// <summary>
    /// The rewards of the scenarios: the Mission Library's, as every contract's (the aUEC typed in, else the Star Citizen Wiki's; a reward nobody knows counts
    /// 0). The first time, the library gets the built-in ones, for the scenarios nobody typed in there yet (the admin's old Gauntlet page and its
    /// GauntletSettings went on 30 Oct 2026: the table was empty).
    /// </summary>
    public Dictionary<int, GauntletReward> Rewards()
    {
        var before = GauntletRewards.Defaults;
        _library.Kinds(Enumerable.Range(1, GauntletRewards.Scenarios)
            .Select(n => new MissionKindRow { Key = LibraryKey(n), Title = GauntletRewards.Title(n), Auec = before[n].Auec, MgScrip = before[n].Scrip }).ToList());
        var rewards = _library.Rewards();
        return Enumerable.Range(1, GauntletRewards.Scenarios).ToDictionary(n => n, n =>
        {
            var k = rewards.GetValueOrDefault(LibraryKey(n));
            return new GauntletReward(k?.Auec ?? 0, k?.Mg ?? 0);
        });
    }

    /// <summary>
    /// The runs of these rows, each with what it paid its player (the contracts tracker: his share of the aUEC of a scenario shared in the party, the scrip
    /// when he accepted it).
    /// </summary>
    List<(string Player, GauntletRun Run)> Paid(List<GauntletRunRow> rows)
    {
        var done = rows.Where(r => r.Outcome == nameof(GauntletOutcome.Complete)).ToList();
        if (done.Count == 0) return rows.Select(r => (r.Player, Run(r))).ToList();
        var rewards = Rewards();
        var contracts = _contracts.Of(done.Select(r => r.MissionId).Distinct(StringComparer.OrdinalIgnoreCase).ToList());
        return rows.Select(r =>
        {
            var run = Run(r);
            if (run.Outcome != GauntletOutcome.Complete || !contracts.TryGetValue(r.MissionId, out var c)) return (r.Player, run);
            var whole = GauntletRewards.Of(r.Scenario, rewards);
            var part = ContractSplit.PartOf(c, r.Player, new ContractReward(whole.Auec, whole.Scrip, null));
            return (r.Player, run with { Paid = new GauntletReward(part?.Auec ?? 0, part?.Mg ?? 0) });
        }).ToList();
    }

    long _collected = -1;   // Uploads.Pieces when the live logs were last looked at

    /// <summary>
    /// Writes the missions the logs show that the database does not have yet, when anything came in since the last time (this runs every two seconds, and for
    /// the Gauntlet pages: with nothing new, the database is left alone). Returns how many were new.
    /// </summary>
    public int Collect()
    {
        var pieces = _uploads.Pieces;
        if (pieces == _collected) return 0;
        var added = Add(_uploads.GauntletRuns());
        _collected = pieces;
        return added;
    }

    /// <summary>Writes these missions where the database does not have them yet (the live logs, or an old log a tracker sent). Returns how many were new.</summary>
    public int Add(IReadOnlyDictionary<string, List<GauntletRun>> found)
    {
        lock (_lock)
        {
            using var db = _store.Open();
            _known ??= db.GauntletRuns.AsNoTracking().Select(r => new { r.Player, r.MissionId }).AsEnumerable()
                .Select(r => (r.Player.ToLowerInvariant(), r.MissionId.ToLowerInvariant())).ToHashSet();
            var added = 0;
            var pending = new HashSet<(string, string)>();   // noted as known once the database took them: a write that fails is tried again next time
            foreach (var (player, runs) in found)
            {
                foreach (var r in runs)
                {
                    var key = (player.ToLowerInvariant(), r.MissionId.ToLowerInvariant());
                    if (_known.Contains(key) || !pending.Add(key)) continue;
                    db.GauntletRuns.Add(new GauntletRunRow
                    {
                        Player = player, MissionId = r.MissionId, Scenario = r.Scenario,
                        AcceptedUtc = r.AcceptedUtc, EndedUtc = r.EndedUtc, Outcome = r.Outcome.ToString(),
                    });
                    added++;
                }
            }
            if (added > 0)
            {
                db.SaveChanges();
                _known.UnionWith(pending);
            }
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
            return Paid(rows.OrderBy(r => r.EndedUtc).ToList()).Select(r => r.Run).ToList();
        }
    }

    /// <summary>
    /// All the runs of these players (an in-game party: the pages show the party's together), oldest first, each with whose it is. A mission shared in the
    /// party is in every member's log, so it is here once per member, each with his part of what it paid (so the party's earnings count it once); only the
    /// one who accepted it has its start, so it is timed once.
    /// </summary>
    public List<(string Player, GauntletRun Run)> RunsOf(IReadOnlyCollection<string> players)
    {
        var wanted = new HashSet<string>(players, StringComparer.OrdinalIgnoreCase);
        lock (_lock)
        {
            using var db = _store.Open();
            return Paid(db.GauntletRuns.AsNoTracking().ToList().Where(r => wanted.Contains(r.Player)).OrderBy(r => r.EndedUtc).ToList());
        }
    }

    /// <summary>The runs of every player, by player.</summary>
    public Dictionary<string, List<GauntletRun>> AllRuns()
    {
        lock (_lock)
        {
            using var db = _store.Open();
            return Paid(db.GauntletRuns.AsNoTracking().ToList().OrderBy(r => r.EndedUtc).ToList())
                .GroupBy(r => r.Player, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Select(r => r.Run).ToList(), StringComparer.OrdinalIgnoreCase);
        }
    }

    static GauntletRun Run(GauntletRunRow r) =>
        new(r.MissionId, r.Scenario, r.AcceptedUtc, r.EndedUtc, Enum.TryParse<GauntletOutcome>(r.Outcome, out var o) ? o : GauntletOutcome.Abandon);

    /// <summary>
    /// The grind sessions of a player, newest first: they start and stop by themselves (AutoSessions). One starts when he accepts a scenario and goes on while
    /// he accepts, completes or abandons them; after an hour with nothing it ended 5 minutes after the last one. Worked out from his runs (kept for good) and
    /// the scenario he is in now, so the earlier ones are there too.
    /// </summary>
    public List<SCTools.Sessions.AutoSessions.Window> GrindSessions(string player, DateTime nowUtc) => GrindSessions(new[] { player }, nowUtc);

    /// <summary>The grind sessions of these players together (an in-game party: what any of them did with a scenario keeps the session going), newest first.</summary>
    public List<SCTools.Sessions.AutoSessions.Window> GrindSessions(IReadOnlyCollection<string> players, DateTime nowUtc) =>
        GrindSessions(players, RunsOf(players).Select(r => r.Run).ToList(), nowUtc);

    /// <summary>The same, with the runs of these players the caller has already (the Gauntlet page reads them once, not twice).</summary>
    public List<SCTools.Sessions.AutoSessions.Window> GrindSessions(IReadOnlyCollection<string> players, List<GauntletRun> runs, DateTime nowUtc)
    {
        var accepted = _uploads.GauntletAccepted();
        var going = players.SelectMany(p => accepted.GetValueOrDefault(p) ?? Array.Empty<DateTime>()).ToList();
        var starts = runs.Select(r => r.AcceptedUtc ?? r.EndedUtc).Concat(going).ToList();
        var activity = runs.Select(r => r.EndedUtc).Concat(starts);
        var found = SCTools.Sessions.AutoSessions.Find(starts, activity, nowUtc);
        found.Reverse();
        return found;
    }
}
