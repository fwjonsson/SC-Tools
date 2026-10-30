using Microsoft.EntityFrameworkCore;
using SCTools.Missions;
using SCTools.Play;
using SCTools.Server.Data;

namespace SCTools.Server;

/// <summary>
/// The contracts tracker (#383, Frank 30 Oct 2026): the one place that knows every contract the players had, and from which every tool gets its contract
/// money (ContractSplit says how it is split). It is worked out again on every question from what is kept for good: the Mission Library's missions (every
/// player's own row of a mission: did he take it or was it shared with him, how and when it ended for him), the Gauntlet Tracker's runs (a scenario shared in
/// the party is in every member's log; the one who accepted it has its start) and the Hauling Planner's payouts (AppData.Payouts, kept with the sessions),
/// with what each contract pays from the Mission Library (typed in, else the Star Citizen Wiki's) and the admin's payout overrides of the Debts tab. Asked by
/// the Session tool and the Companion (Earned), the Gauntlet Tracker (its runs' parts, GauntletService) and, through the same rule, the Hauling Planner's
/// payouts (Payouts.ComputePayouts, which writes the planner's payouts this reads).
/// </summary>
public sealed class ContractTracker
{
    readonly DataStore _store;
    readonly MissionLibrary _library;
    readonly LiveTracker _live;

    public ContractTracker(DataStore store, MissionLibrary library, LiveTracker live)
    {
        _store = store;
        _library = library;
        _live = live;
    }

    /// <summary>Every contract, the oldest first.</summary>
    public List<TrackedContract> All() => Build(null, null);

    /// <summary>The contracts of these missions, by mission (any capitals).</summary>
    public Dictionary<string, TrackedContract> Of(IReadOnlyCollection<string> missionIds) =>
        Build(missionIds, null).ToDictionary(c => c.MissionId, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The contracts this player was paid a part of, each with his part (worked out now: a reward typed in later counts for the past): the ones he completed
    /// (his own log says so), and the Hauling Planner's payouts he had a share of without the contract in his own log. Oldest first.
    /// </summary>
    public List<EarnedMission> Earned(string player)
    {
        var earned = new List<EarnedMission>();
        foreach (var c in Build(null, player))
        {
            if (c.PartOf(player) is not { } part) continue;
            var holder = c.HolderOf(player);
            if (holder?.Completed == true)
            {
                if (holder.EndedUtc == null) continue;   // completed, but the log never said when: in no period
                earned.Add(new EarnedMission(holder.EndedUtc.Value, c.Kind ?? c.MissionId, !c.TookIt(player), part.Auec, part.Mg, part.Council));
            }
            else if (c.Planner != null) earned.Add(new EarnedMission(c.Planner.Time, c.Planner.Contract, true, part.Auec, part.Mg, part.Council));
        }
        return earned.OrderBy(m => m.EndedUtc).ToList();
    }

    /// <summary>The contracts of these accounts (one player's combined accounts, AppData.CombinedAccounts), each with that account's part, together.</summary>
    public List<EarnedMission> Earned(IEnumerable<string> accounts) =>
        accounts.Distinct(StringComparer.OrdinalIgnoreCase).SelectMany(Earned).OrderBy(m => m.EndedUtc).ToList();

    /// <summary>
    /// The contracts of these missions (null: every one), or the ones this player had or was paid a share of (player): everybody's rows of them, so the split
    /// counts everybody who had them.
    /// </summary>
    List<TrackedContract> Build(IReadOnlyCollection<string>? missionIds, string? player)
    {
        var (payouts, overrides) = _live.Read(t => (
            t.State.Data.Payouts.GroupBy(p => p.MissionId, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => Copy(g.Last()), StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, decimal>(t.State.Data.PayoutOverrides, StringComparer.OrdinalIgnoreCase)));
        var rewards = _library.Rewards();
        List<MissionRow> missions;
        List<GauntletRunRow> runs;
        HashSet<string>? ids = missionIds == null ? null : new HashSet<string>(missionIds, StringComparer.OrdinalIgnoreCase);
        using (var db = _store.Open())
        {
            if (player != null)
            {
                var lower = player.ToLowerInvariant();
                ids = db.Missions.AsNoTracking().Where(m => m.Player.ToLower() == lower).Select(m => m.MissionId)
                    .Concat(db.GauntletRuns.AsNoTracking().Where(r => r.Player.ToLower() == lower).Select(r => r.MissionId)).ToList()
                    .Concat(payouts.Values.Where(p => p.Members.Any(n => PlayerNames.SameName(n, player))).Select(p => p.MissionId))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
            }
            if (ids == null)
            {
                missions = db.Missions.AsNoTracking().ToList();
                runs = db.GauntletRuns.AsNoTracking().ToList();
            }
            else
            {
                var list = ids.Select(i => i.ToLowerInvariant()).Distinct().ToList();
                missions = db.Missions.AsNoTracking().Where(m => list.Contains(m.MissionId.ToLower())).ToList();
                runs = db.GauntletRuns.AsNoTracking().Where(r => list.Contains(r.MissionId.ToLower())).ToList();
            }
        }
        var all = missions.Select(m => m.MissionId).Concat(runs.Select(r => r.MissionId)).Concat(payouts.Keys.Where(k => ids == null || ids.Contains(k)))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        var byMission = missions.ToLookup(m => m.MissionId, StringComparer.OrdinalIgnoreCase);
        var byRun = runs.ToLookup(r => r.MissionId, StringComparer.OrdinalIgnoreCase);
        return all.Select(id => Contract(id, byMission[id].ToList(), byRun[id].ToList(), payouts.GetValueOrDefault(id), overrides, rewards))
            .OrderBy(c => c.EndedUtc ?? c.Planner?.Time ?? DateTime.MaxValue).ThenBy(c => c.MissionId, StringComparer.Ordinal).ToList();
    }

    static PayoutRecord Copy(PayoutRecord p) =>
        new() { MissionId = p.MissionId, Time = p.Time, Contract = p.Contract, Payout = p.Payout, Members = p.Members.ToList(), Each = p.Each };

    /// <summary>
    /// One contract from everybody's rows of its mission: a player with a row in the Mission Library and a run in the Gauntlet Tracker is one holder; the
    /// one who took it is the one who accepted it first.
    /// </summary>
    static TrackedContract Contract(string id, List<MissionRow> missions, List<GauntletRunRow> runs, PayoutRecord? planner, Dictionary<string, decimal> overrides,
        Dictionary<string, ContractReward> rewards)
    {
        var holders = new List<ContractHolder>();
        void Add(ContractHolder h)
        {
            var i = holders.FindIndex(x => PlayerNames.SameName(x.Player, h.Player));
            if (i < 0)
            {
                holders.Add(h);
                return;
            }
            var was = holders[i];
            var took = was.Took == true || h.Took == true ? true : was.Took == false || h.Took == false ? false : (bool?)null;
            var ended = was.Completed || !h.Completed && was.Outcome != null ? was : h;
            holders[i] = new ContractHolder(was.Player, took, ended.Outcome, ended.EndedUtc);
        }
        foreach (var m in missions)
            Add(new ContractHolder(m.Player, !m.Shared, Enum.TryParse<MissionOutcome>(m.Outcome, out var o) ? o : null, m.EndedUtc));
        foreach (var r in runs)
            Add(new ContractHolder(r.Player, r.AcceptedUtc != null ? true : null, r.Outcome switch
            {
                nameof(GauntletOutcome.Complete) => MissionOutcome.Complete,
                nameof(GauntletOutcome.Fail) => MissionOutcome.Fail,
                _ => MissionOutcome.Abandon,
            }, r.EndedUtc));
        // who took it: the one who accepted it first. A contract shared in the party is "Contract Accepted" in the log of everybody who accepts the share
        // too, a few seconds after the game's "Contract Shared" line, which has no mission id (all zeros; Frank's logs of 29-30 Oct 2026), so the logs
        // never mark it as shared: the first accept is the taker's, a later one a share. (The game says every contract he has accepted again when he
        // logs in, so the Mission Library keeps the earliest.) A holder whose accept was never read is not known, and one marked Shared did not take it.
        var startOf = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        void Start(string p, DateTime? t)
        {
            if (t != null && (!startOf.TryGetValue(p, out var was) || t < was)) startOf[p] = t.Value;
        }
        foreach (var m in missions.Where(m => !m.Shared)) Start(m.Player, m.StartedUtc);
        foreach (var r in runs) Start(r.Player, r.AcceptedUtc);
        var first = startOf.Count > 0 ? startOf.Values.Min() : (DateTime?)null;
        // when each of them accepted it or the share (the scrip goes to the share accepted first when the taker abandons it)
        var acceptedOf = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        foreach (var (p, t) in missions.Select(m => (m.Player, m.StartedUtc)).Concat(runs.Select(r => (r.Player, r.AcceptedUtc))))
            if (t != null && (!acceptedOf.TryGetValue(p, out var was) || t < was)) acceptedOf[p] = t.Value;
        holders = holders.Select(h => h with
        {
            Took = startOf.TryGetValue(h.Player, out var s) ? s == first : h.Took == false ? false : null,
            AcceptedUtc = acceptedOf.TryGetValue(h.Player, out var a) ? a : null,
        }).ToList();

        var kind = missions.Select(m => m.Kind).FirstOrDefault(k => k != null)
            ?? (runs.Count > 0 ? GauntletService.LibraryKey(runs[0].Scenario) : null)
            ?? missions.Select(m => m.ContractId).FirstOrDefault(c => !string.IsNullOrEmpty(c));
        var title = missions.Select(m => m.Title).FirstOrDefault(t => t != null) ?? (runs.Count > 0 ? GauntletRewards.Title(runs[0].Scenario) : planner?.Contract);
        var reward = kind != null && rewards.TryGetValue(kind, out var r0) ? r0 : ContractReward.Unknown;
        if (overrides.TryGetValue(id, out var typed) && typed > 0) reward = reward with { Auec = (long)Math.Round(typed) };   // what it really paid (the Debts tab)
        return new TrackedContract(missions.FirstOrDefault()?.MissionId ?? runs.FirstOrDefault()?.MissionId ?? id, kind, title, holders, reward, planner);
    }
}
