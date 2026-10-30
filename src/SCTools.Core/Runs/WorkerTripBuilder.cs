using System.Text.RegularExpressions;

namespace SCTools.Runs;

public static class WorkerTripBuilder
{
    static readonly TimeSpan HandInGap = TimeSpan.FromMinutes(3);
    static readonly Regex StopId = new(@"_\d+$", RegexOptions.Compiled);

    /// <summary>
    /// Who did a step of a from-Ruin contract. A shared mission is in the log of everyone who has it, and every one of them gets the update when somebody takes cargo out or
    /// delivers it, but only the player who did it has the elevator / loading platform lines of it. Several times (the same update in the logs of two players, a few seconds apart) are all tried.
    /// </summary>
    public static string? OutActor(HaulingLog log, bool pickup, IEnumerable<DateTime> times, IReadOnlyCollection<string>? holders = null, string? station = null)
    {
        foreach (var t in times.OrderBy(x => x))
        {
            var who = ActorAt(log, pickup, t, holders, station);
            if (who != null) return who;
        }
        return null;
    }

    /// <summary>Every finished step (cargo taken out at Ruin, cargo delivered at a station) of the from-Ruin contracts in the log, with who did it. Missions marked as gone are left out.</summary>
    public static List<OutStep> OutSteps(HaulingLog log)
    {
        // the same merged log gives the same steps: kept with the log until it reads another line or a mission is marked gone (the plan asked for them about
        // a dozen times per flyer, each a walk through the whole log)
        var dropped = log.Missions.Values.Count(m => m.Dropped);
        if (OutStepsKept.TryGetValue(log, out var kept) && kept.Version == log.Version && kept.Dropped == dropped) return kept.Steps;
        var steps = MakeOutSteps(log);
        OutStepsKept.AddOrUpdate(log, new KeptSteps(log.Version, dropped, steps));
        return steps;
    }

    sealed record KeptSteps(int Version, int Dropped, List<OutStep> Steps);

    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<HaulingLog, KeptSteps> OutStepsKept = new();

    static List<OutStep> MakeOutSteps(HaulingLog log)
    {
        var steps = new List<OutStep>();
        foreach (var m in log.Missions.Values.Where(x => x.IsOut && !x.Dropped))
        {
            foreach (var kv in m.Events)
            {
                var pickup = kv.Key.StartsWith("pickup_", StringComparison.Ordinal);
                if (!pickup && !kv.Key.StartsWith("dropoff_", StringComparison.Ordinal)) continue;
                var times = kv.Value.Where(e => e.State == "COMPLETED").Select(e => e.Time).OrderBy(t => t).ToList();
                if (times.Count == 0) continue;
                var station = m.ObjZone.TryGetValue(kv.Key, out var zone) ? StationOfZone(zone) : null;
                steps.Add(new OutStep { Mission = m, Objective = kv.Key, Pickup = pickup, Worker = OutActor(log, pickup, times, m.Holders, station), Time = times[0] });
            }
        }
        return steps;
    }

    /// <summary>
    /// Who did it, taken from the logs: only the player who takes cargo out of a freight elevator (pickup)
    /// or uses the loading platform (hand-in) has those lines in his own log, a few seconds before the mission update.
    /// Returns null when no log shows it (for example a party member whose log we do not have). With the mission's holders given, only one of them can
    /// be it: two players at loading platforms at the same moment are told apart by who has the mission (29 Oct 2026: zzatvo handed his own direct haul
    /// in at Gaslight while zzoega loaded at Orbituary, and the delivery went to zzoega, who the map then put at Gaslight), and by who was there (Frank,
    /// 30 Oct 2026: with the station of the step given, a player whose own jumps had him at that station comes first, one his jumps say nothing about
    /// next, one they put elsewhere last; among equals the nearest in time).
    /// </summary>
    public static string? ActorAt(HaulingLog log, bool pickup, DateTime time, IReadOnlyCollection<string>? holders = null, string? station = null)
    {
        var source = pickup ? log.UnstowByPlayer : log.PlatformByPlayer;
        var from = time - TimeSpan.FromSeconds(pickup ? 20 : 30);
        var to = time + TimeSpan.FromSeconds(2);

        var found = new List<(string Name, TimeSpan Gap, string? At)>();   // everybody with such a line in the window: how far from the moment, and where his jumps had him
        foreach (var kv in source)
        {
            if (holders is { Count: > 0 } && !holders.Contains(kv.Key, StringComparer.OrdinalIgnoreCase)) continue;   // not his mission: not his delivery
            foreach (var x in kv.Value)
            {
                if (x < from || x > to) continue;
                found.Add((kv.Key, (time - x).Duration(), station == null ? null : StationAt(log, kv.Key, time)));
            }
        }
        if (found.Count == 0) return null;
        return found.OrderBy(f => station == null || f.At == null ? 1 : f.At == station ? 0 : 2).ThenBy(f => f.Gap).First().Name;
    }

    /// <summary>The station of the distance table a location id is (by the position the log gave with it), or null.</summary>
    public static string? StationOfZone(string? zone) => zone == null ? null : PyroDistances.IdOfTail(SCTools.Tracking.Places.StationKey(zone));

    /// <summary>
    /// The station a player's own jumps had him at this moment: the destination of his last jump before it, once he had arrived (null when his log has no
    /// jumps, the destination is not a station of the table, or he was still flying).
    /// </summary>
    static string? StationAt(HaulingLog log, string player, DateTime time)
    {
        if (!log.QtByPlayer.TryGetValue(player, out var qt)) return null;
        string? last = null;
        var arrived = false;
        foreach (var e in qt.OrderBy(q => q.Time))
        {
            if (e.Time > time) break;
            if (e.Kind == QtKind.Selected) { last = e.Destination; arrived = false; }
            else if (e.Kind == QtKind.Arrived) arrived = true;
        }
        return last != null && arrived ? PyroDistances.IdOfQuantumTarget(last) : null;
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
    public static bool ResetWorkersForNewBatch(SessionGroup g, SessionInputs inp, ISet<string>? ignore = null)
    {
        var open = g.Missions.Where(m => !m.Completed && ignore?.Contains(m.Id) != true).ToList();   // a mission with missing cargo never completes: it does not hold up the clean slate
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
        inp.LockedTrips.Clear();   // the locked Next trips belonged to the old set of missions
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
                if (kv.Value != zone || !kv.Key.StartsWith("pickup_", StringComparison.Ordinal) || !StopId.IsMatch(kv.Key)) continue;
                if (!m.Events.TryGetValue(kv.Key, out var ev) || ev.Count == 0) continue;
                var first = ev.Min(e => e.Time);
                if (since.HasValue && first < since.Value) continue;   // pickups of an earlier set of missions do not count
                var actor = ActorAt(log, true, first, m.Holders, StationOfZone(kv.Value));
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
    const string SubmittedMission = "~submitted";   // the Submit button: the run ends here and counts as it is (Frank, 30 Oct 2026)
    const string LeftMission = "~left";

    /// <summary>True for the objective of a pickup stop of a mission.</summary>
    public static bool IsPickupStop(string objective) => objective.StartsWith("pickup_", StringComparison.Ordinal) && StopId.IsMatch(objective);

    /// <param name="plan">
    /// From plan.Since on there is no Assigned Worker. The worker of a pickup is the one the log shows unloading the cargo, else the one who handed
    /// the mission in, else the one the Trip Planner credited it to (plan.Claims). Earlier pickups use the old rule.
    /// </param>
    public static List<WorkerTrip> Build(SessionGroup g, Func<string, DateTime, string> workerOfZone, HaulingLog log,
        IEnumerable<ManualEnd>? manualEnds = null, (DateTime Since, Dictionary<string, string> Claims)? plan = null)
    {
        var manual = (manualEnds ?? Enumerable.Empty<ManualEnd>()).ToList();
        var failedIds = g.Failed.Select(m => m.Id).ToHashSet();
        var sessionStart = g.Missions.Select(m => m.Accepted ?? m.FirstSeen ?? g.Start).DefaultIfEmpty(g.Start).Min();
        var acceptedAt = g.Missions.Concat(g.Failed).GroupBy(m => m.Id).ToDictionary(x => x.Key, x => x.First().Accepted ?? x.First().FirstSeen ?? g.Start);
        var periods = ActivePeriods(g);   // the stretches with at least one mission going: the clock of a run starts with the first mission accepted in one

        var pickups = new List<(string Worker, string Zone, string Mission, DateTime Time)>();
        var drops = new List<(string Mission, DateTime Time, string? Actor, string Zone)>();
        foreach (var m in g.Missions.Concat(g.Failed))
        {
            string? handInActor = null;   // who the logs show handing this mission in
            // a hauling job with several drop-offs (stage 4b): only its last delivery ends the run, the ones before it take a parcel off the ship and the run goes on
            var dropStops = m.ObjZone.Keys.Where(k => k.StartsWith("dropoff_", StringComparison.Ordinal)).Select(k => StopId.Replace(k, "")).Distinct().ToList();
            var several = !m.IsHauling && !m.IsOut && dropStops.Count > 1;
            bool LastDrop(DateTime at) => !several || dropStops.All(stop => m.Events.Any(kv => kv.Key.StartsWith(stop, StringComparison.Ordinal) && kv.Value.Any(e => e.State == "COMPLETED" && e.Time <= at)));
            foreach (var kv in m.Events)
            {
                if (!kv.Key.StartsWith("dropoff_", StringComparison.Ordinal)) continue;
                foreach (var e in kv.Value)
                {
                    if (!LastDrop(e.Time)) continue;   // a parcel delivered, more aboard: the run goes on to the next drop-off
                    // a to-Ruin contract is handed in at Ruin, which the station table does not list as a pickup station: named outright, so who was there counts
                    // there too (30 Oct 2026, 03:37 UTC: zzatvo handed two missions in at Ruin while zzoega was landing at Rat's Nest; zzoega's platform line was the
                    // nearer one, the hand-in went to him, and zzatvo's route stuck at "Delivering" until his next hand-in)
                    var station = m.IsHauling ? PyroDistances.Ruin : StationOfZone(m.ObjZone.GetValueOrDefault(kv.Key));
                    var actor = ActorAt(log, false, e.Time, m.Holders, station);
                    handInActor ??= actor;
                    drops.Add((m.Id, e.Time, actor, m.ObjZone.GetValueOrDefault(kv.Key) ?? ""));   // where it was handed in (the drop-off's zone)
                }
            }
            foreach (var kv in m.ObjZone)
            {
                if (!kv.Key.StartsWith("pickup_", StringComparison.Ordinal) || !StopId.IsMatch(kv.Key)) continue;
                if (!m.Events.TryGetValue(kv.Key, out var ev) || ev.Count == 0) continue;
                var first = ev.Min(e => e.Time);
                string who;
                if (plan.HasValue && first >= plan.Value.Since)
                {
                    // the log names who unloaded the cargo, else the Trip Planner decided (saved), else who handed the mission in
                    who = ActorAt(log, true, first, m.Holders, StationOfZone(kv.Value))
                        ?? (plan.Value.Claims.TryGetValue(m.Id + "|" + kv.Key, out var claimed) ? claimed : null)   // once decided it stays
                        ?? handInActor ?? "Unassigned";
                }
                else
                {
                    // the Assigned Worker of the location comes first, the logs only fill in where nobody is assigned
                    var assigned = workerOfZone(kv.Value, first);
                    who = assigned != "Unassigned" ? assigned : (ActorAt(log, true, first, m.Holders, StationOfZone(kv.Value)) ?? "Unassigned");
                }
                pickups.Add((who, kv.Value, m.Id, first));
            }
        }

        // who carried cargo for each mission, by the rule above
        var carriers = pickups
            .GroupBy(p => p.Mission)
            .ToDictionary(x => x.Key, x => x.Select(p => p.Worker).ToHashSet(StringComparer.OrdinalIgnoreCase));

        // players whose own log we have
        var logPlayers = new HashSet<string>(log.UnstowByPlayer.Keys.Concat(log.PlatformByPlayer.Keys), StringComparer.OrdinalIgnoreCase);

        // from-Ruin contracts (waste): who took the cargo out at Ruin and who delivered it at a station. Only what the logs show someone doing counts: a mission
        // shared in the party is in everybody's log, but the elevator and loading platform lines are only in the log of the player who did it. Only while the session ran.
        var collected = new List<(string Worker, TripCollect Item)>();
        var delivered = new List<(string Worker, TripDrop Item)>();
        foreach (var s in OutSteps(log))
        {
            if (s.Worker == null || s.Time < g.Start || (g.Stop.HasValue && s.Time > g.Stop.Value)) continue;
            var cargo = s.Mission.OutCargo ?? "Cargo";
            if (s.Pickup)
                collected.Add((s.Worker, new TripCollect { MissionId = s.Mission.Id, Objective = s.Objective, Cargo = cargo, Time = s.Time, Scu = s.Mission.ObjScu.GetValueOrDefault(s.Objective) }));
            else if (s.Mission.ObjZone.TryGetValue(s.Objective, out var zone))
                delivered.Add((s.Worker, new TripDrop { Zone = zone, MissionId = s.Mission.Id, Objective = s.Objective, Cargo = cargo, Time = s.Time, Scu = s.Mission.ObjScu.GetValueOrDefault(s.Objective) }));
        }

        var workers = pickups.Select(p => p.Worker).Concat(collected.Select(c => c.Worker)).Concat(delivered.Select(d => d.Worker))
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();

        var result = new List<WorkerTrip>();
        foreach (var worker in workers)
        {
            var mine = pickups.Where(p => string.Equals(p.Worker, worker, StringComparison.OrdinalIgnoreCase)).OrderBy(p => p.Time).ToList();
            var myMissions = new HashSet<string>(mine.Select(p => p.Mission));

            // events in time order: his pickups, the cargo he took out at Ruin and delivered from there, and the hand-ins that are his
            var events = new List<(DateTime Time, bool IsPickup, string Mission, string Zone, bool ActorKnown, TripCollect? Collect, TripDrop? Drop)>();
            foreach (var p in mine) events.Add((p.Time, true, p.Mission, p.Zone, true, null, null));
            foreach (var c in collected.Where(x => string.Equals(x.Worker, worker, StringComparison.OrdinalIgnoreCase)))
                events.Add((c.Item.Time, false, c.Item.MissionId, "", true, c.Item, null));
            foreach (var d in delivered.Where(x => string.Equals(x.Worker, worker, StringComparison.OrdinalIgnoreCase)))
                events.Add((d.Item.Time, false, d.Item.MissionId, d.Item.Zone, true, null, d.Item));
            foreach (var d in drops)
            {
                if (d.Actor != null)
                {
                    // the logs name who did the hand-in: it ends only his run, whatever missions it was for
                    if (string.Equals(d.Actor, worker, StringComparison.OrdinalIgnoreCase))
                        events.Add((d.Time, false, d.Mission, d.Zone, true, null, null));
                    continue;
                }

                // nobody is named. This is most likely a party member without a log,
                // otherwise the signal was missing and it is one of the workers who carried this mission.
                if (!carriers.TryGetValue(d.Mission, out var cs) || !cs.Contains(worker)) continue;
                var someoneWithoutLog = cs.Any(c => !logPlayers.Contains(c));
                if (someoneWithoutLog && logPlayers.Contains(worker)) continue;
                events.Add((d.Time, false, d.Mission, d.Zone, false, null, null));
            }
            foreach (var me in manual)
            {
                if (string.Equals(me.Worker, worker, StringComparison.OrdinalIgnoreCase))
                    events.Add((me.Time, false, me.Submitted ? SubmittedMission : me.Left ? LeftMission : ManualMission, "", true, null, null));
            }
            events = events.OrderBy(e => e.Time).ThenBy(e => e.IsPickup ? 1 : 0).ToList();

            var number = 1;
            var runStart = sessionStart;
            var carried = new HashSet<string>();
            var runPickups = new List<(string Zone, string Mission, DateTime Time)>();
            var runCollects = new List<TripCollect>();
            var runDrops = new List<TripDrop>();
            WorkerTrip? last = null;

            foreach (var e in events)
            {
                if (e.Collect != null)
                {
                    runCollects.Add(e.Collect);
                    continue;
                }
                if (e.Drop != null)
                {
                    runDrops.Add(e.Drop);
                    continue;
                }
                if (e.IsPickup)
                {
                    carried.Add(e.Mission);
                    runPickups.Add((e.Zone, e.Mission, e.Time));
                    continue;
                }

                var isManual = e.Mission == ManualMission || e.Mission == LeftMission || e.Mission == SubmittedMission;   // ended by hand, no hand-in in the log
                // a hand-in only ends a run that has cargo of his; a run by hand (or the worker leaving) can also end one that only has waste in it (nothing to hand in)
                var ends = isManual ? carried.Count > 0 || runCollects.Count > 0 || runDrops.Count > 0 : (e.ActorKnown ? carried.Count > 0 : carried.Contains(e.Mission));
                if (ends)
                {
                    // a submitted run hands in what it picked up (it is counted as it is); one ended by hand or by leaving hands in nothing
                    var trip = NewTrip(worker, number++, runStart, e.Time, runPickups, runCollects, runDrops, isManual && e.Mission != SubmittedMission ? 0 : carried.Count, failedIds, acceptedAt, periods);
                    trip.Manual = e.Mission == ManualMission;
                    trip.Left = e.Mission == LeftMission;
                    trip.EndZone = !isManual && e.Zone.Length > 0 ? e.Zone : null;   // where it was handed in
                    trip.Submitted = e.Mission == SubmittedMission;
                    result.Add(trip);
                    last = trip;
                    runStart = e.Time;
                    carried.Clear();
                    runPickups = new List<(string, string, DateTime)>();
                    runCollects = new List<TripCollect>();
                    runDrops = new List<TripDrop>();
                }
                else if (!isManual && last != null && !last.Manual && !last.Left && !last.Submitted && last.End.HasValue && e.Time - last.End.Value <= HandInGap)
                {
                    // the next mission of the same hand-in, not a new run
                    last.End = e.Time;
                    runStart = e.Time;
                }
            }

            // what is left is the run on the way (also one with only waste in it: leaving Ruin with a load for a station is the start of a run)
            if (runPickups.Count > 0 || runCollects.Count > 0 || runDrops.Count > 0)
                result.Add(NewTrip(worker, number, runStart, null, runPickups, runCollects, runDrops, 0, failedIds, acceptedAt, periods));
        }
        return result;
    }

    /// <summary>The stretches of the session with at least one mission going: from an accepted mission until the last one overlapping it has ended.</summary>
    public static List<(DateTime Start, DateTime End)> ActivePeriods(SessionGroup g)
    {
        var spans = g.Missions.Concat(g.Failed)
            .Select(m => (Start: m.Accepted ?? m.FirstSeen ?? g.Start, End: m.EndedAt ?? DateTime.MaxValue))
            .OrderBy(s => s.Start).ToList();
        var result = new List<(DateTime Start, DateTime End)>();
        foreach (var s in spans)
        {
            if (result.Count > 0 && s.Start <= result[^1].End)
                result[^1] = (result[^1].Start, s.End > result[^1].End ? s.End : result[^1].End);
            else
                result.Add(s);
        }
        return result;
    }

    static WorkerTrip NewTrip(string worker, int number, DateTime start, DateTime? end,
        List<(string Zone, string Mission, DateTime Time)> pickups, List<TripCollect> collects, List<TripDrop> drops, int handedIn, HashSet<string> failedIds,
        Dictionary<string, DateTime> acceptedAt, List<(DateTime Start, DateTime End)> periods)
    {
        // A run starts at the previous hand-in, unless the missions were accepted later (a worker's first run, or new
        // missions after a break with nothing going): then it starts when the first mission of that stretch was accepted,
        // whichever order the trips are flown in, so the break is not run time.
        var realStart = start;
        DateTime? firstEvent = null;   // the first thing done in the run: a pickup, or cargo from Ruin taken out or delivered
        foreach (var time in pickups.Select(p => p.Time).Concat(collects.Select(c => c.Time)).Concat(drops.Select(d => d.Time)))
        {
            if (firstEvent == null || time < firstEvent) firstEvent = time;
        }
        if (firstEvent.HasValue)
        {
            var first = firstEvent.Value;
            var period = periods.FirstOrDefault(p => p.Start <= first && first <= p.End);
            if (period.End != default)
            {
                if (period.Start > realStart) realStart = period.Start;
            }
            else if (pickups.Count > 0)
            {
                var accepted = pickups.Where(p => acceptedAt.ContainsKey(p.Mission)).Select(p => acceptedAt[p.Mission]).ToList();
                if (accepted.Count > 0 && accepted.Min() > realStart) realStart = accepted.Min();
            }
        }
        if (firstEvent.HasValue && firstEvent.Value < realStart) realStart = firstEvent.Value;   // never after the first thing done
        if (end.HasValue && realStart > end.Value) realStart = start;

        var t = new WorkerTrip { Worker = worker, Number = number, Start = realStart, KeyTime = start, End = end, HandedIn = handedIn };
        t.Collects.AddRange(collects.OrderBy(c => c.Time));
        t.Drops.AddRange(drops.OrderBy(d => d.Time));
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
