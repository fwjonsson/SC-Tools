using System.Text.RegularExpressions;

namespace HaulingTracker;

public sealed class TripPickup
{
    public string Zone = "";
    public int Missions;        // missions loaded at this location during the run
    public DateTime Time;       // last pickup event here (UTC)
    public List<string> MissionIds = new();   // the missions loaded here (one batch each)
}

/// <summary>One run: from the previous hand-in (or the first accepted contract) to a hand-in at Ruin.</summary>
public sealed class Trip
{
    public int Number;
    public DateTime Start;      // UTC
    public DateTime? End;       // UTC, null = still on the way
    public List<TripPickup> Pickups = new();
    public int HandedIn;        // missions that had a hand-in in this run
}

public static class TripBuilder
{
    static readonly TimeSpan HandInGap = TimeSpan.FromMinutes(3);
    static readonly Regex StopId = new(@"_\d+$", RegexOptions.Compiled);

    /// <summary>Every hand-in at Ruin ends a run, also a partial one.</summary>
    public static List<Trip> Build(SessionGroup g)
    {
        // all pickup events (zone, mission, time) and all hand-in events (mission, time)
        var pickups = new List<(string Zone, string Mission, DateTime Time)>();
        var drops = new List<(string Mission, DateTime Time)>();
        foreach (var m in g.Missions)
        {
            foreach (var kv in m.Events)
            {
                if (kv.Key.StartsWith("dropoff_"))
                {
                    foreach (var e in kv.Value) drops.Add((m.Id, e.Time));
                }
                else if (kv.Key.StartsWith("pickup_") && StopId.IsMatch(kv.Key) && m.ObjZone.TryGetValue(kv.Key, out var zone))
                {
                    foreach (var e in kv.Value) pickups.Add((zone, m.Id, e.Time));
                }
            }
        }
        drops.Sort((a, b) => a.Time.CompareTo(b.Time));

        // hand-ins within a few minutes of each other (several missions in a row) are one hand-in
        var ends = new List<(DateTime Time, HashSet<string> Missions)>();
        foreach (var d in drops)
        {
            if (ends.Count > 0 && d.Time - ends[^1].Time <= HandInGap)
            {
                ends[^1].Missions.Add(d.Mission);
                ends[^1] = (d.Time, ends[^1].Missions);
            }
            else
            {
                ends.Add((d.Time, new HashSet<string> { d.Mission }));
            }
        }

        var trips = new List<Trip>();
        var start = g.Missions.Select(m => m.Accepted ?? m.FirstSeen ?? g.Start).DefaultIfEmpty(g.Start).Min();
        for (var i = 0; i <= ends.Count; i++)
        {
            DateTime? end = i < ends.Count ? ends[i].Time : (DateTime?)null;
            var trip = new Trip { Number = i + 1, Start = start, End = end, HandedIn = i < ends.Count ? ends[i].Missions.Count : 0 };
            var inRun = pickups.Where(p => p.Time > start && (end == null || p.Time <= end.Value));
            foreach (var grp in inRun.GroupBy(p => p.Zone))
            {
                trip.Pickups.Add(new TripPickup
                {
                    Zone = grp.Key,
                    Missions = grp.Select(p => p.Mission).Distinct().Count(),
                    Time = grp.Max(p => p.Time),
                });
            }
            trip.Pickups.Sort((a, b) => a.Time.CompareTo(b.Time));

            if (end == null && trip.Pickups.Count == 0) break;   // nothing going on after the last hand-in
            trips.Add(trip);
            if (end.HasValue) start = end.Value;
        }
        return trips;
    }
}

/// <summary>One run of one worker.</summary>
public sealed class WorkerTrip
{
    public string Worker = "";
    public int Number;
    public DateTime Start;      // UTC: the previous hand-in, but never before the missions of this run were accepted
    public DateTime KeyTime;    // UTC: the start before that correction, so what you ticked on the run keeps its key
    public DateTime? End;       // UTC, null = still on the way
    public List<TripPickup> Pickups = new();
    public int HandedIn;        // missions handed in at the end of this run
    public bool Manual;         // the run was ended by hand

    /// <summary>Stable key for saving things you tick on the run.</summary>
    public string Key => Worker + "|" + (KeyTime == default ? Start : KeyTime).ToString("yyyyMMddHHmmss");
}

public static class WorkerTripBuilder
{
    static readonly TimeSpan HandInGap = TimeSpan.FromMinutes(3);
    static readonly Regex StopId = new(@"_\d+$", RegexOptions.Compiled);

    /// <summary>
    /// Who did it, taken from the logs: only the player who takes cargo out of a freight elevator (pickup)
    /// or uses the loading platform (hand-in) has those lines in his own log, a few seconds before the mission update.
    /// Returns null when no log shows it (for example a party member whose log we do not have).
    /// </summary>
    public static string? ActorAt(HaulingLog log, bool pickup, DateTime time)
    {
        var source = pickup ? log.UnstowByPlayer : log.PlatformByPlayer;
        var from = time - TimeSpan.FromSeconds(pickup ? 20 : 30);
        var to = time + TimeSpan.FromSeconds(2);

        string? best = null;
        var bestGap = TimeSpan.MaxValue;
        foreach (var kv in source)
        {
            foreach (var x in kv.Value)
            {
                if (x < from || x > to) continue;
                var gap = (time - x).Duration();
                if (gap < bestGap)
                {
                    bestGap = gap;
                    best = kv.Key;
                }
            }
        }
        return best;
    }

    /// <summary>
    /// The Assigned Worker of a location at the time of a pickup. Pickups from before the last reset use the assignments
    /// that were frozen then, so clearing the table for new missions does not take the workers off the old runs.
    /// </summary>
    public static string WorkerAt(SessionInputs inp, string zone, DateTime time)
    {
        foreach (var era in inp.PastWorkers.OrderBy(e => e.Until))
        {
            if (time < era.Until)
                return era.Workers.TryGetValue(zone, out var old) && !string.IsNullOrWhiteSpace(old) ? old.Trim() : "Unassigned";
        }
        return inp.Rows.TryGetValue(zone, out var r) && !string.IsNullOrWhiteSpace(r.Worker) ? r.Worker.Trim() : "Unassigned";
    }

    /// <summary>
    /// A clean slate for a new set of missions: when every mission of the earlier set is completed and new missions have been accepted,
    /// the assigned workers are cleared (and frozen for the old runs). Returns true when something changed and has to be saved.
    /// </summary>
    public static bool ResetWorkersForNewBatch(SessionGroup g, SessionInputs inp)
    {
        var open = g.Missions.Where(m => !m.Completed).ToList();
        var done = g.Missions.Where(m => m.Completed).ToList();
        if (open.Count == 0 || done.Count == 0) return false;

        var batchStart = open.Min(m => m.Accepted ?? m.FirstSeen ?? g.Start);
        if (done.Any(m => (m.EndedAt ?? DateTime.MinValue) > batchStart)) return false;   // the old missions were still going when the new ones came
        if (inp.WorkersResetAt.HasValue && inp.WorkersResetAt.Value >= batchStart) return false;   // already reset for this set

        var era = new WorkerEra { Until = batchStart };
        foreach (var kv in inp.Rows)
        {
            if (string.IsNullOrWhiteSpace(kv.Value.Worker)) continue;
            era.Workers[kv.Key] = kv.Value.Worker.Trim();
            kv.Value.Worker = "";
        }
        if (era.Workers.Count > 0) inp.PastWorkers.Add(era);
        inp.WorkersResetAt = batchStart;
        return true;
    }

    /// <summary>Who the logs say did the first pickup at a location (null if no log shows it).</summary>
    public static string? PickupActor(SessionGroup g, string zone, HaulingLog log, DateTime? since = null)
    {
        DateTime? bestTime = null;
        string? who = null;
        foreach (var m in g.Missions)
        {
            foreach (var kv in m.ObjZone)
            {
                if (kv.Value != zone || !kv.Key.StartsWith("pickup_") || !StopId.IsMatch(kv.Key)) continue;
                if (!m.Events.TryGetValue(kv.Key, out var ev) || ev.Count == 0) continue;
                var first = ev.Min(e => e.Time);
                if (since.HasValue && first < since.Value) continue;   // pickups of an earlier set of missions do not count
                var actor = ActorAt(log, true, first);
                if (actor != null && (bestTime == null || first < bestTime))
                {
                    bestTime = first;
                    who = actor;
                }
            }
        }
        return who;
    }

    /// <summary>
    /// Runs per worker. The worker of a pickup is the Assigned Worker of that location,
    /// and only where nobody is assigned who the logs say used the freight elevator. A run starts when the missions start, or when the previous run was handed in.
    /// A run is leaving the base, collecting cargo from any number of missions and handing it in at Ruin.
    /// A hand-in ends only the run of the player who did it, never someone else's run, whatever missions it was for.
    /// Who did it comes from the logs. If no log names anybody it is credited to a worker without a log.
    /// </summary>
    const string ManualMission = "~manual";

    public static List<WorkerTrip> Build(SessionGroup g, Func<string, DateTime, string> workerOfZone, HaulingLog log,
        IEnumerable<ManualEnd>? manualEnds = null)
    {
        var manual = (manualEnds ?? Enumerable.Empty<ManualEnd>()).ToList();
        var failedIds = g.Failed.Select(m => m.Id).ToHashSet();
        var sessionStart = g.Missions.Select(m => m.Accepted ?? m.FirstSeen ?? g.Start).DefaultIfEmpty(g.Start).Min();
        var acceptedAt = g.Missions.Concat(g.Failed).GroupBy(m => m.Id).ToDictionary(x => x.Key, x => x.First().Accepted ?? x.First().FirstSeen ?? g.Start);

        var pickups = new List<(string Worker, string Zone, string Mission, DateTime Time)>();
        var drops = new List<(string Mission, DateTime Time, string? Actor)>();
        foreach (var m in g.Missions.Concat(g.Failed))
        {
            foreach (var kv in m.Events)
            {
                if (!kv.Key.StartsWith("dropoff_")) continue;
                foreach (var e in kv.Value) drops.Add((m.Id, e.Time, ActorAt(log, false, e.Time)));
            }
            foreach (var kv in m.ObjZone)
            {
                if (!kv.Key.StartsWith("pickup_") || !StopId.IsMatch(kv.Key)) continue;
                if (!m.Events.TryGetValue(kv.Key, out var ev) || ev.Count == 0) continue;
                var first = ev.Min(e => e.Time);
                // the Assigned Worker of the location comes first, the logs only fill in where nobody is assigned
                var assigned = workerOfZone(kv.Value, first);
                var who = assigned != "Unassigned" ? assigned : (ActorAt(log, true, first) ?? "Unassigned");
                pickups.Add((who, kv.Value, m.Id, first));
            }
        }

        // who carried cargo for each mission, by the rule above
        var carriers = pickups
            .GroupBy(p => p.Mission)
            .ToDictionary(x => x.Key, x => x.Select(p => p.Worker).ToHashSet(StringComparer.OrdinalIgnoreCase));

        // players whose own log we have
        var logPlayers = new HashSet<string>(log.UnstowByPlayer.Keys.Concat(log.PlatformByPlayer.Keys), StringComparer.OrdinalIgnoreCase);

        var result = new List<WorkerTrip>();
        foreach (var wg in pickups.GroupBy(p => p.Worker, StringComparer.OrdinalIgnoreCase).OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            var worker = wg.Key;
            var mine = wg.OrderBy(p => p.Time).ToList();
            var myMissions = new HashSet<string>(mine.Select(p => p.Mission));

            // events in time order: his pickups, and the hand-ins that are his
            var events = new List<(DateTime Time, bool IsPickup, string Mission, string Zone, bool ActorKnown)>();
            foreach (var p in mine) events.Add((p.Time, true, p.Mission, p.Zone, true));
            foreach (var d in drops)
            {
                if (d.Actor != null)
                {
                    // the logs name who did the hand-in: it ends only his run, whatever missions it was for
                    if (string.Equals(d.Actor, worker, StringComparison.OrdinalIgnoreCase))
                        events.Add((d.Time, false, d.Mission, "", true));
                    continue;
                }

                // nobody is named. This is most likely a party member without a log,
                // otherwise the signal was missing and it is one of the workers who carried this mission.
                if (!carriers.TryGetValue(d.Mission, out var cs) || !cs.Contains(worker)) continue;
                var someoneWithoutLog = cs.Any(c => !logPlayers.Contains(c));
                if (someoneWithoutLog && logPlayers.Contains(worker)) continue;
                events.Add((d.Time, false, d.Mission, "", false));
            }
            foreach (var me in manual)
            {
                if (string.Equals(me.Worker, worker, StringComparison.OrdinalIgnoreCase))
                    events.Add((me.Time, false, ManualMission, "", true));
            }
            events = events.OrderBy(e => e.Time).ThenBy(e => e.IsPickup ? 1 : 0).ToList();

            var number = 1;
            var runStart = sessionStart;
            var carried = new HashSet<string>();
            var runPickups = new List<(string Zone, string Mission, DateTime Time)>();
            WorkerTrip? last = null;

            foreach (var e in events)
            {
                if (e.IsPickup)
                {
                    carried.Add(e.Mission);
                    runPickups.Add((e.Zone, e.Mission, e.Time));
                    continue;
                }

                var isManual = e.Mission == ManualMission;
                var ends = isManual ? carried.Count > 0 : (e.ActorKnown ? carried.Count > 0 : carried.Contains(e.Mission));
                if (ends)
                {
                    var trip = NewTrip(worker, number++, runStart, e.Time, runPickups, isManual ? 0 : carried.Count, failedIds, acceptedAt);
                    trip.Manual = isManual;
                    result.Add(trip);
                    last = trip;
                    runStart = e.Time;
                    carried.Clear();
                    runPickups = new List<(string, string, DateTime)>();
                }
                else if (!isManual && last != null && !last.Manual && last.End.HasValue && e.Time - last.End.Value <= HandInGap)
                {
                    // the next mission of the same hand-in, not a new run
                    last.End = e.Time;
                    runStart = e.Time;
                }
            }

            if (runPickups.Count > 0)
                result.Add(NewTrip(worker, number, runStart, null, runPickups, 0, failedIds, acceptedAt));
        }
        return result;
    }

    static WorkerTrip NewTrip(string worker, int number, DateTime start, DateTime? end,
        List<(string Zone, string Mission, DateTime Time)> pickups, int handedIn, HashSet<string> failedIds,
        Dictionary<string, DateTime> acceptedAt)
    {
        // A run starts at the previous hand-in, unless its missions were accepted later (a worker's first run, or new
        // missions after a break): then it starts when the first of them was accepted, so the break is not run time.
        var realStart = start;
        var accepted = pickups.Where(p => acceptedAt.ContainsKey(p.Mission)).Select(p => acceptedAt[p.Mission]).ToList();
        if (accepted.Count > 0 && accepted.Min() > realStart) realStart = accepted.Min();
        if (pickups.Count > 0 && pickups.Min(p => p.Time) < realStart) realStart = pickups.Min(p => p.Time);   // never after the first pickup
        if (end.HasValue && realStart > end.Value) realStart = start;

        var t = new WorkerTrip { Worker = worker, Number = number, Start = realStart, KeyTime = start, End = end, HandedIn = handedIn };
        foreach (var grp in pickups.GroupBy(p => p.Zone))
        {
            t.Pickups.Add(new TripPickup
            {
                Zone = grp.Key,
                Missions = grp.Select(p => p.Mission).Distinct().Count(m => !failedIds.Contains(m)),   // failed missions are no batches
                Time = grp.Max(p => p.Time),
                MissionIds = grp.Select(p => p.Mission).Distinct().ToList(),
            });
        }
        t.Pickups.Sort((a, b) => a.Time.CompareTo(b.Time));
        return t;
    }
}
