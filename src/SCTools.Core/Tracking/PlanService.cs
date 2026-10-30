using System.Globalization;
using static SCTools.Planning.PlanText;

namespace SCTools.Tracking;

/// <summary>
/// The plan of the session shown (the Route Planner): it divides the open batches over the crew (the active party, each with his ship) with a planner, keeps
/// the stops of everybody's planned trip for him, credits the pickups no log names, and makes the rows and lines the tab and the web page show.
/// The trips are only worked out again when the batches, the crew or the kept stops change. The planner is RoutePlanner, the planner without a hub
/// (docs/planner-any-hauling.md; the one planner since the evening of 30 Oct 2026, when Frank made it the Route Planner and the old TripPlanner went).
/// </summary>
public sealed class PlanService
{
    readonly TrackerState _state;
    readonly Places _places;
    readonly Crew _crew;
    readonly Contracts _contracts;
    readonly Earnings _earnings;
    readonly DropOffs _dropOffs;
    readonly CargoRuns _runs;
    readonly Whereabouts _where;
    PlanCore? _planCore;
    string _hub = PyroDistances.Ruin;   // where a worker nobody can place starts (stage 3): the place most of the open batches go to (Ruin in a to-Ruin session)
    readonly Dictionary<string, List<(string Key, int Scu)>> _parcelsOf = new(StringComparer.Ordinal);   // group key -> the drop-offs of the job parcels in it ("mission|objective") and their SCU (0: not known), for the typable Deliver lines

    public PlanService(TrackerState state, Places places, Crew crew, Contracts contracts, Earnings earnings, DropOffs dropOffs, CargoRuns runs, Whereabouts where)
    {
        _state = state;
        _places = places;
        _crew = crew;
        _contracts = contracts;
        _earnings = earnings;
        _dropOffs = dropOffs;
        _runs = runs;
        _where = where;
    }

    AppData _data => _state.Data;

    /// <summary>The stops kept for every worker (the plan is sticky by itself). Empty while no session is shown.</summary>
    Dictionary<string, List<LockedStop>> Locks => _state.Inputs?.LockedTrips ?? new Dictionary<string, List<LockedStop>>();

    /// <summary>The trips of the planner (RoutePlanner).</summary>
    static List<PlannedTrip> PlanTrips(IReadOnlyDictionary<string, int> remaining, IReadOnlyList<(string Name, int Capacity, QtDrive Drive)> crew, IReadOnlyList<WorkerStart> starts,
        IReadOnlyDictionary<string, List<(string[] Workers, int Batches)>> limits, IReadOnlyList<DropChunk> chunks, IReadOnlyDictionary<string, int> batchScu, IReadOnlyDictionary<string, int> shipScu, IReadOnlyDictionary<string, int> shipFullScu) =>
        RoutePlanner.Plan(remaining, crew, starts, limits, chunks, batchScu, shipScu, shipFullScu);

    /// <summary>The trips of the last plan, or null when none is worked out (yet, or since Invalidate).</summary>
    public PlanCore? Core => _planCore;

    /// <summary>The plan is worked out again next time (the payouts, the drop-offs or the missions changed).</summary>
    public void Invalidate() => _planCore = null;

    /// <summary>
    /// The plan is sticky by itself (Frank, 30 Oct 2026; the Next Stops button of the admin's Session page went with it): the stops of every worker's planned
    /// trip are kept for him (LockedTrips), so planning again when somebody else picks up or hands in never takes a stop away from him. A kept stop goes
    /// when its batches are picked up or gone, or when he picks up somewhere else (he went his own way); a station set by hand releases them all (ReleaseLock).
    /// </summary>
    /// <summary>He was placed somewhere by hand: his kept stops are released, so the plan is free to start with a stop near him.</summary>
    public void ReleaseLock(string name)
    {
        var lockKey = LockKey(name);
        if (lockKey != null) Locks.Remove(lockKey);
        _planCore = null;
    }

    /// <summary>The first stop of a trip that only has a drop-off there (or nothing to pick up) is not a pickup, so there is nothing to lock.</summary>
    public static bool NothingToLock(PlannedTrip t) => t.Stops.Count == 0 || (t.Drops.Count > 0 && t.Stops[0].Id != t.Drops[0].Id);

    /// <summary>
    /// The order of every worker's trips (his first is his Current Route, the others his Queued routes). First the trip he is flying or heading for (it continues
    /// his run, or starts at his locked stop or where he is), then the one with the waste he has taken out at Ruin already. Then:
    /// <list type="number">
    /// <item>the trips that take waste out, so he is rid of it and can take a new from-Ruin contract (every load is a payout more);</item>
    /// <item>then the trips that land at one station: while he flies those, missions get done and new ones come in, and the stations of the bigger trips can get
    /// enough batches to be one stop trips too;</item>
    /// <item>then the most valuable: its pickups and the waste it takes out.</item>
    /// </list>
    /// A trip with three stops or more is the last resort: it goes after all the others, also when it takes waste out (then before the other big ones).
    /// Trips the same on all of that keep the planner's order (see RoutePlanner.Plan). The workers keep their order.
    /// </summary>
    public static List<PlannedTrip> Queue(IEnumerable<PlannedTrip> trips, Func<PlannedTrip, decimal> value, Func<string, bool>? wasteAboard = null)
    {
        var list = trips.ToList();
        var workers = list.Select(t => t.Worker).Distinct().ToList();
        var aboard = workers.Where(n => wasteAboard?.Invoke(n) == true).ToHashSet();
        // a worker whose trips all start and end at Ruin (his first one where he is) can fly them in any order: the order above. One with a trip that ends at
        // another station keeps the planner's order, where every trip starts where the one before it ended (stage 1b)
        var free = workers.Where(n => list.Where(t => t.Worker == n).All(t => (t.End == PyroDistances.Ruin || t.Stops.Count == 0) && (t.Special || t.From == null))).ToHashSet();
        var planned = list.Select((t, i) => (t, i)).ToDictionary(x => x.t, x => x.i);
        return list
            .OrderBy(t => workers.IndexOf(t.Worker))
            .ThenBy(t => t.Special ? 0 : free.Contains(t.Worker) && t.Drops.Count > 0 && aboard.Contains(t.Worker) ? 1 : 2)
            .ThenBy(t => free.Contains(t.Worker) ? Landings(t) >= 3 ? 1 : 0 : 0)   // three stops or more: the last resort
            .ThenBy(t => free.Contains(t.Worker) ? t.Drops.Count > 0 ? 0 : 1 : 0)
            .ThenBy(t => free.Contains(t.Worker) ? Landings(t) <= 1 ? 0 : 1 : 0)
            .ThenByDescending(t => free.Contains(t.Worker) ? value(t) : 0)
            .ThenBy(t => planned[t])
            .ToList();
    }

    /// <summary>The first stop of a trip as the lock keeps it: the group of its batches (RoutePlanner.GroupKey) and how many.</summary>
    public static (string Group, int Batches) FirstPick(PlannedTrip t)
    {
        var first = t.Stops[0];
        var pick = t.Picks.FirstOrDefault(p => p.Id == first.Id);
        return pick.Group != null ? (pick.Group, pick.Batches) : (first.Id, first.Batches);
    }

    /// <summary>The trips in the order they are flown (Queue), each starting where the one before it ends (RoutePlanner.Chain): a run in progress ends where its cargo goes, else the worker's location.</summary>
    List<PlannedTrip> Ordered(IEnumerable<PlannedTrip> trips, Func<PlannedTrip, decimal> value, Func<string, bool> wasteAboard)
    {
        var list = Queue(trips, value, wasteAboard);
        RoutePlanner.Chain(list, name => _runs.ActiveRunOf(name) != null ? _runs.LoadedDestinationsOf(name)[^1] : _where.LocationOf(name, true, _hub).Id, _crew.QtOf);
        return list;
    }

    /// <summary>The stations a trip lands at before Ruin (its stops): where it drops off the waste and where it picks up (waste and pickups at the same station is one stop).</summary>
    public static int Landings(PlannedTrip t) => t.Drops.Select(d => d.Id).Concat(t.Stops.Select(s => s.Id)).Distinct().Count();

    /// <summary>The waste he has taken out at Ruin and not brought anywhere yet (recently): his run in progress with nothing else in it, or null.</summary>
    WorkerTrip? WasteTakenOut(string name) => _runs.WasteTakenOut(name);

    /// <summary>The name a worker has in the locked trips of this session, or null when he has none.</summary>
    public string? LockKey(string name) => Locks.Keys.FirstOrDefault(k => SameName(k, name));

    /// <summary>
    /// A pickup that no log names (a member without a log) is credited to somebody once, and the credit is saved, so it never changes again (not when
    /// the program restarts either). Who gets it: the worker whose locked stop it is, then the worker whose planned trip needed a batch at that station
    /// (in the order of the plan, from before the batch was picked up), and else the only member without a log (a member with a log would have shown
    /// up in it). Nobody gets more batches than his ship holds. What the logs show always wins.
    /// </summary>
    public void ClaimNewPickups()
    {
        if (_state.Current == null || _state.Inputs == null || _state.Log == null) return;
        var since = _data.PlanCreditSince ??= DateTime.UtcNow;

        var fresh = new List<(string Key, string Station, DateTime Time)>();
        foreach (var m in _state.Current.Missions)
        {
            foreach (var kv in m.ObjZone)
            {
                if (!WorkerTripBuilder.IsPickupStop(kv.Key)) continue;
                if (!m.Events.TryGetValue(kv.Key, out var ev) || ev.Count == 0) continue;
                var first = ev.Min(e => e.Time);
                if (first < since) continue;
                var key = m.Id + "|" + kv.Key;
                if (_state.Inputs.PickupClaims.ContainsKey(key)) continue;
                if (WorkerTripBuilder.ActorAt(_state.Log, true, first, m.Holders, _places.StationOf(kv.Value)) != null) continue;   // a log shows who it was
                var station = _places.StationOf(kv.Value);
                if (station == null) continue;
                fresh.Add((key, station, first));
            }
        }
        if (fresh.Count == 0) return;

        var crew = _crew.ActiveCrew().Where(n => _crew.ShipCapacityOf(n) > 0).ToList();
        var used = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);   // claimed just now
        bool HasRoom(string n) => _crew.ShipCapacityOf(n) - (_runs.ActiveRunOf(n)?.Loaded ?? 0) - used.GetValueOrDefault(n) > 0;
        var logged = new HashSet<string>(_state.Log.UnstowByPlayer.Keys.Concat(_state.Log.PlatformByPlayer.Keys), StringComparer.OrdinalIgnoreCase);
        var core = _planCore;
        var changed = false;

        foreach (var group in fresh.GroupBy(f => f.Station))
        {
            // 1. whose locked stop this is (one entry for every batch of the lock)
            var locked = new List<string>();
            foreach (var n in crew)
            {
                var lk = LockKey(n);
                if (lk == null) continue;
                var stored = Locks[lk];
                if (stored.Count == 0 || RoutePlanner.OriginOf(stored[0].Id) != group.Key) continue;
                for (var i = 0; i < stored[0].Batches; i++) locked.Add(n);
            }
            // 2. whose planned trip expects a batch here, in the order of the plan
            var queue = new List<string>();
            if (core != null)
            {
                foreach (var trip in core.Trips)
                    foreach (var (id, n) in trip.Stops)
                    {
                        if (id != group.Key) continue;
                        for (var i = 0; i < n; i++) queue.Add(trip.Worker);
                    }
            }
            // 3. members without a log: his pickups are the ones no log names
            var noLog = crew.Where(n => !logged.Any(l => SameName(l, n))).ToList();

            foreach (var f in group.OrderBy(x => x.Time))
            {
                string? who = null;
                foreach (var list in new[] { locked, queue })
                {
                    var i = list.FindIndex(n => HasRoom(n));
                    if (i < 0) continue;
                    who = list[i];
                    list.RemoveAt(i);
                    break;
                }
                if (who == null)
                {
                    var able = noLog.Where(HasRoom).ToList();
                    if (able.Count == 1) who = able[0];
                }
                if (who == null) continue;
                _state.Inputs.PickupClaims[f.Key] = who;
                used[who] = used.GetValueOrDefault(who) + 1;
                changed = true;
            }
        }
        if (changed) _state.Save();
    }

    /// <summary>
    /// The stops of a planned trip one by one, for the Current Route table: where it starts (Ruin, collecting the load from Ruin if there is one, or the station he is at),
    /// what is done at every stop, and the places it delivers at (Ruin, or another station: stage 1b). Every line has the leg that leads to it, so the legs add up to the whole route.
    /// </summary>
    List<StopLine> LinesOf(PlannedTrip trip, string worker, Func<string, decimal> perGroup, Func<string, double> scuPerGroup, string? at = null)
    {
        var drive = _crew.QtOf(worker);
        var lines = new List<StopLine>();
        var prev = trip.From ?? at ?? PyroDistances.Ruin;
        var hasDrop = trip.Drops.Count > 0;
        var drop = hasDrop ? trip.Drops[0] : default;
        var cargo = trip.DropCargo ?? "cargo";
        var firstStop = trip.Stops.Count > 0 ? trip.Stops[0].Id : null;
        decimal ValueAt(string id) => trip.Picks.Where(p => p.Id == id).Sum(p => p.Batches * perGroup(p.Group));   // every group picked up there
        double ScuAt(string id) => trip.Picks.Where(p => p.Id == id).Sum(p => p.Batches * scuPerGroup(p.Group));

        StopLine Add(string id, string name, bool stop, IEnumerable<string> actions, int batches = 0, decimal value = 0, double scu = 0)
        {
            var gm = StarRoutes.Gm(prev, id);
            var line = new StopLine { Id = id, Name = name, Stop = stop, Batches = batches, Value = value, Scu = scu, Gm = gm, Seconds = StarRoutes.Seconds(drive, prev, id), Gate = StarRoutes.Crosses(prev, id) };
            line.Actions.AddRange(actions);
            lines.Add(line);
            prev = id;
            return line;
        }

        if (trip.From == null)
        {
            // his first trip starts at Ruin but he is somewhere else: first the flight back to Ruin
            if (at != null && at != PyroDistances.Ruin) Add(at, StarRoutes.NameOf(at), false, Array.Empty<string>());
            // the load for the first stop is collected at Ruin
            var collect = hasDrop
                ? new[] { "Collect " + cargo + (drop.Batches > 1 ? " \u00d7" + drop.Batches : "") }
                : Array.Empty<string>();
            var ruin = Add(PyroDistances.Ruin, "Ruin", false, collect);
            if (hasDrop)
            {
                // the load's SCU and boxes are on the line of the stop it goes to (Frank, 29 Oct 2026: the hauler types the SCU there); the total counts it here
                ruin.WasteScu = trip.DropParts.Where(x => x > 0).Sum();
                ruin.Value = _earnings.WasteValue(trip.DropKeys);     // what the load pays (reward / drop-offs of its contract), from the From Ruin rows of the rewards table
            }
        }
        else if (firstStop != trip.From) Add(trip.From, StarRoutes.NameOf(trip.From), false, Array.Empty<string>());   // where he is now

        var dropShown = !hasDrop;
        if (hasDrop && firstStop != drop.Id)
        {
            Load(Add(drop.Id, StarRoutes.NameOf(drop.Id), true, new[] { "Deliver " + cargo }), trip);   // a stop with only the drop-off, with its SCU and boxes
            dropShown = true;
        }
        foreach (var (id, batches) in trip.Stops)
        {
            var actions = new List<string>();
            if (!dropShown && id == drop.Id)
            {
                actions.Add("Deliver " + cargo);
                dropShown = true;
            }
            if (batches > 0) actions.Add("Collect");
            var line = Add(id, StarRoutes.NameOf(id), true, actions, batches, ValueAt(id), ScuAt(id));
            line.Count = batches;
            if (actions.Count > 0 && actions[0].StartsWith("Deliver", StringComparison.Ordinal)) Load(line, trip);   // the drop-off here: its SCU and boxes
        }
        // the places it delivers at: everything on board for each is handed in there (the SCU column says how much where it is collected)
        foreach (var end in EndsOf(trip))
        {
            var going = trip.Picks.Where(p => p.To == end).Sum(p => p.Batches);
            var line = Add(end, PlaceName(end), false, going > 0 || (trip.Batches > 0 && trip.Ends.Count == 0) ? new[] { "Deliver" } : Array.Empty<string>());
            // a job's drop-offs delivered here (Frank, 30 Oct 2026): their SCU and keys on the line, typable by the hauler like a load from Ruin, so the boxes follow
            JobParts(line, trip.Picks.Where(p => p.To == end).SelectMany(p => _parcelsOf.GetValueOrDefault(p.Group) ?? new List<(string Key, int Scu)>()));
        }
        return lines;
    }

    /// <summary>The drop-offs of hauling jobs on a Deliver line: their SCU (0 = not known, typable) and keys, like the loads from Ruin (Load).</summary>
    static void JobParts(StopLine line, IEnumerable<(string Key, int Scu)> parcels)
    {
        var list = parcels.ToList();
        if (list.Count == 0) return;
        line.Parts.AddRange(list.Select(p => p.Scu));
        line.Keys.AddRange(list.Select(p => p.Key));
        line.Typable = true;
        line.Unknown.AddRange(list.Where(p => p.Scu <= 0).Select(p => p.Key));
    }

    /// <summary>The loads of a planned trip on the line of the stop they go to: their SCU and keys, typable by the hauler on the page.</summary>
    static void Load(StopLine line, PlannedTrip trip)
    {
        line.Parts.AddRange(trip.DropParts);
        line.Keys.AddRange(trip.DropKeys);
        line.Typable = true;
        line.Unknown.AddRange(trip.DropKeys.Where((k, i) => i < trip.DropParts.Count && trip.DropParts[i] <= 0));
    }

    /// <summary>Puts what the plan still collects at a station after the lines of a run in progress (at the station where the last pickup was it joins that line).</summary>
    void AddCollectLine(List<StopLine> lines, string worker, string id, int batches, decimal value, double scu)
    {
        var last = lines.Count > 0 ? lines[^1] : null;
        if (last != null && last.Id == id)
        {
            last.Actions.Add("Collect");
            last.Pending = true;   // the colour stays here until this is collected too
            last.Count += batches;
            last.Batches += batches;
            last.Value += value;
            last.Scu += scu;
            return;
        }
        var prev = lines.LastOrDefault(l => l.Id.Length > 0)?.Id ?? PyroDistances.Ruin;
        var gm = StarRoutes.Gm(prev, id);
        var line = new StopLine { Id = id, Name = StarRoutes.NameOf(id), Batches = batches, Value = value, Scu = scu, Gm = gm, Seconds = StarRoutes.Seconds(_crew.QtOf(worker), prev, id), Gate = StarRoutes.Crosses(prev, id) };
        line.Actions.Add("Collect");
        line.Count = batches;
        lines.Add(line);
    }

    /// <summary>The last lines of a run in progress: the places its cargo goes to (Ruin, or another station since stage 1b), where everything on board for each is handed in.</summary>
    void AddEndLines(List<StopLine> lines, string worker, int batches, IEnumerable<string> ends)
    {
        foreach (var end in ends)
        {
            var prev = lines.LastOrDefault(l => l.Id.Length > 0)?.Id ?? PyroDistances.Ruin;
            var gm = StarRoutes.Gm(prev, end);
            var line = new StopLine { Id = end, Name = PlaceName(end), Stop = false, Gm = gm, Seconds = StarRoutes.Seconds(_crew.QtOf(worker), prev, end), Gate = StarRoutes.Crosses(prev, end) };
            if (batches > 0) line.Actions.Add("Deliver");   // the SCU column only says how much where it is collected
            JobParts(line, _runs.LoadedJobDrops(worker).Where(d => d.Place == end).Select(d => (d.Key, d.Scu)));   // a job's cargo aboard for here: its SCU, typable
            lines.Add(line);
        }
    }

    /// <summary>
    /// The line of the Current Route that has the worker colour: the step he is doing. A run in progress: the stop he is still busy at (Collecting), else the
    /// stop after the last one that is done (he is on his way there), or the stop he set by hand; a trip that has not started: the line it starts from.
    /// </summary>
    public static int HereLine(List<StopLine> lines, string location, bool byHand)
    {
        if (lines.Count == 0) return -1;
        // what is done at the last stop he got to is finished (Collected, Delivered Waste): his colour moves on to the next step, the one he is on his way to;
        // while he is still at it (Collecting) it stays on that stop
        var lastDone = lines.FindLastIndex(l => l.Done);
        if (byHand)
        {
            // the place you set: the first line with it from the last stop that is done on (Ruin at the start of a run is not the Ruin he hands in at)
            var at = lines.FindIndex(Math.Max(0, lastDone), l => l.Id == location);
            if (at >= 0) return at;
        }
        if (lastDone < 0) return 0;
        return !lines[lastDone].Busy && !lines[lastDone].Pending && lastDone + 1 < lines.Count ? lastDone + 1 : lastDone;
    }

    /// <summary>The planner id of a zone for a run's lines: a Pyro station, else the place of a job's stop in any system (stage 4b); null when neither.</summary>
    string? PlaceOfZone(string zone) => _places.StationOf(zone) ?? (_state.JobZonePlaces.TryGetValue(zone, out var place) ? place : null);

    /// <summary>How full the plan fills a ship (by the estimated SCU of the batches): the rest is headroom for missions that are bigger than the estimate.</summary>
    const double PlanFill = 0.9;

    /// <summary>
    /// The runs of one worker in the selected session, oldest first, as lines for his table. The lines are everything that was done in the run so far, in the order it
    /// happened, and stay until the run is handed in: Ruin (with the waste taken out there), the stations where waste was delivered, and the pickups.
    /// </summary>
    public List<PlanRow> RunRows(string name)
    {
        var rows = new List<PlanRow>();
        foreach (var t in _state.Trips.Where(x => SameName(x.Worker, name) && !x.End.HasValue).OrderBy(CargoRuns.RunStartedAt))   // finished runs are only on Cargo Runs
        {
            // a run with only waste in it: taking it out at Ruin is not a route yet (the plan still collects it), and a delivery from long ago is no run on the way
            if (t.Pickups.Count == 0 && (t.Drops.Count == 0 || _state.Log == null || _state.Log.LastTime - CargoRuns.RunLastActivity(t) > CargoRuns.StaleLeg)) continue;

            var level = t.Manual ? "All" : _runs.LostLevel(t.Key);
            var stops = new List<string>();
            var lines = new List<StopLine>();   // what is done, stop by stop (things done at the same place in a row are one line)
            var done = new Dictionary<StopLine, DoneAt>();
            var picked = 0;
            var drive = _crew.QtOf(name);
            // the run starts at the session's hub (Ruin for a to-Ruin session: the waste taken out there is on this line; where the cargo of a party in another system goes, stage 4b)
            var start = _hub;
            var from = start;
            var home = new StopLine { Id = start, Name = PlaceName(start), Stop = false, Done = true };
            lines.Add(home);
            done[home] = new DoneAt();

            StopLine LineFor(string? id, string shown, bool atRuin)
            {
                if (lines[^1].Name == shown) return lines[^1];   // the same place again: one line
                var gm = id != null ? StarRoutes.Gm(from, id) : 0;
                var seconds = id != null ? StarRoutes.Seconds(drive, from, id) : 0;
                var gate = id != null && StarRoutes.Crosses(from, id);
                if (id != null) from = id;
                var line = new StopLine { Id = id ?? "", Name = shown, Stop = !atRuin, Done = true, Gm = gm, Seconds = seconds, Gate = gate };
                lines.Add(line);
                done[line] = new DoneAt();
                return line;
            }

            var steps = new List<(DateTime Time, Action Do)>();
            foreach (var cluster in CargoRuns.Clusters(t.Collects, x => x.Time, x => x.Cargo))
            {
                var batch = cluster;
                steps.Add((batch[0].Time, () =>
                {
                    var ruinLine = LineFor(PyroDistances.Ruin, "Ruin", true);
                    var d = done[ruinLine];
                    d.Taken += 1;   // the game completes every pickup of the contract at once, however much is taken out: one take-out, how much is not known
                    d.TakenCargo = batch[0].Cargo;
                    d.Last = batch.Max(x => x.Time);
                }));
            }
            foreach (var cluster in CargoRuns.Clusters(t.Drops, x => x.Time, x => x.Cargo + "|" + x.Zone))
            {
                var batch = cluster;
                steps.Add((batch[0].Time, () =>
                {
                    var id = PlaceOfZone(batch[0].Zone);
                    var line = LineFor(id, _places.LocationName(batch[0].Zone), false);
                    if (id != null && (stops.Count == 0 || stops[^1] != id)) stops.Add(id);
                    var d = done[line];
                    d.Delivered += batch.Count;
                    d.DeliveredScu.AddRange(batch.Select(x => x.Scu));
                    d.DeliveredCargo = batch[0].Cargo;
                    d.Last = batch.Max(x => x.Time);
                }));
            }
            foreach (var p in t.Pickups)
            {
                var pick = p;
                steps.Add((pick.Time, () =>
                {
                    var n = pick.MissionIds.Count > 0 ? pick.MissionIds.Count : pick.Missions;
                    picked += n;
                    var id = PlaceOfZone(pick.Zone);
                    var line = LineFor(id, _places.LocationName(pick.Zone), false);
                    if (id != null && (stops.Count == 0 || stops[^1] != id)) stops.Add(id);
                    line.Batches += n;
                    line.Value += _earnings.PickupValue(pick);
                    line.Scu += _runs.PickupScu(pick);
                    done[line].Picked += n;
                    done[line].Last = pick.Time;
                }));
            }
            foreach (var step in steps.OrderBy(x => x.Time)) step.Do();

            // the run in progress: what is already done says so. At the line where he is, it says "Collecting" until he selects the next destination (only when his log shows that)
            foreach (var line in lines)
            {
                var d = done[line];
                // waste sent down the elevator is delivered at once; only what still has to go into the ship (taken out at Ruin, picked up) keeps him busy
                var busy = ReferenceEquals(line, lines[^1]) && (d.Taken > 0 || d.Picked > 0) && _where.StillLoading(name, d.Last);
                var tick = busy ? "" : " \u2713";
                line.Busy = busy;
                if (d.Taken > 0)
                {
                    line.Actions.Add((busy ? "Collecting " : "Collected ") + d.TakenCargo + tick);
                    // what was taken out is only known from what was delivered since: those loads, with their containers
                    if (t.Drops.Count > 0)
                    {
                        line.Value += _earnings.WasteValue(t);   // what the waste delivered so far pays (the value of the run counts it too)
                        line.Parts.AddRange(t.Drops.Select(x => x.Scu));
                        line.Keys.AddRange(t.Drops.Select(x => x.MissionId + "|" + x.Objective));
                        line.WasteScu += t.Drops.Where(x => x.Scu > 0).Sum(x => x.Scu);
                        line.Unknown.AddRange(t.Drops.Where(x => x.Scu <= 0).Select(x => x.MissionId + "|" + x.Objective));
                    }
                }
                if (d.Delivered > 0)
                {
                    line.Actions.Add("Delivered " + d.DeliveredCargo + (d.Delivered > 1 ? " \u00d7" + d.Delivered : "") + " \u2713");   // its SCU is on the Ruin line where it was collected
                }
                if (d.Picked > 0)
                {
                    line.Actions.Add((busy ? "Collecting" : "Collected") + tick);
                    line.Count += d.Picked;
                }
            }
            rows.Add(new PlanRow
            {
                Label = "Run #" + t.Number,
                Route = string.Join("  \u2192  ", lines.Select(x => x.Name)),
                Lines = lines,
                Batches = level == "All" ? "failed" : t.Left ? "left" : t.End.HasValue ? t.HandedIn.ToString(CultureInfo.InvariantCulture) : picked.ToString(CultureInfo.InvariantCulture),
                BatchCount = picked,
                Gm = stops.Count > 0 ? RoutePlanner.PathGm(new[] { start }.Concat(stops).Concat(_runs.LoadedDestinationsOf(name))) : 0,
                Seconds = t.End.HasValue ? Math.Max(0, (t.End.Value - t.Start).TotalSeconds) : 0,
                Level = level == "All" ? "all" : level == "Little" ? "little" : t.End.HasValue ? "done" : "active",
                Stops = stops,
                Places = new[] { start }.Concat(stops).Concat(_runs.LoadedDestinationsOf(name)).ToList(),
                Value = _earnings.RunValue(t),
                Flight = _runs.FlightOf(name, stops, _runs.LoadedDestinationsOf(name)),
            });
        }
        return rows;
    }

    /// <summary>False when he has picked up somewhere else since the lock was set (he did not go where the lock said); true while he is still on his way, or after he picked up at the locked stop.</summary>
    bool FollowsLock(string name, IReadOnlyCollection<string> keptIds, DateTime? since)
    {
        // only the pickups after the stops were kept count: stops kept while he was already flying a run are for the next pickups
        var picked = false;
        foreach (var t in _state.Trips.Where(t => SameName(t.Worker, name) && !t.End.HasValue))
        {
            foreach (var p in t.Pickups.Where(p => since == null || p.Time > since))
            {
                picked = true;
                var at = _places.StationOf(p.Zone);
                if (at != null && keptIds.Any(k => RoutePlanner.OriginOf(k) == at)) return true;   // he picked up at one of his kept stops (their groups' stations): he follows the plan
            }
        }
        return !picked;   // no pickup since: he is still on his way
    }

    /// <summary>
    /// The trips for the open batches. Only the next stop of a worker is locked, and only while he is on his way to it: its batches are taken
    /// out first and his first trip goes there first, but the rest of that trip is planned freely, so a better stop can still be put after it.
    /// A worker who is flying a run has it counted in his ship (room left, and the trip continues from where he is) and the plan adds to it;
    /// while there is room, the next stop of that continuation is locked too (it is where he is heading). A lock ends when he picks up somewhere else, when its batches are picked up, and when he leaves the party; when his run
    /// is handed in, the first stop of his next planned trip is locked. Only worked out again when the batches, the crew or the locks change.
    /// </summary>
    PlanCore CoreFor(List<(string Name, int Capacity, QtDrive Drive)> crew, Dictionary<string, int> open, List<string> unknown, Dictionary<string, List<(string[] Workers, int Batches)>> limits, IReadOnlyList<DropChunk> chunks,
        Dictionary<string, int> batchScu, Dictionary<string, int> shipScu, Dictionary<string, int> shipFullScu, Func<PlannedTrip, decimal> value)
    {
        bool WasteAboard(string name) => WasteTakenOut(name) != null;
        // how many batches of this station still fit in his ship, with what he carries already (by SCU)
        int RoomAt(string name, string station, int loadedScu) =>
            Math.Max(0, (shipScu.GetValueOrDefault(name) - loadedScu) / Math.Max(1, batchScu.GetValueOrDefault(station, 170)));
        var settled = new HashSet<string>();
        for (var pass = 0; ; pass++)
        {
            var remaining = new Dictionary<string, int>(open);
            var starts = new List<WorkerStart>();
            var waiting = new List<string>();   // on his way to nothing locked: the first stop of his next planned trip gets locked once it is planned
            if (_state.Inputs != null)
            {
                var changed = false;
                // somebody who left the party (or does not fly) has no kept stops: his batches go back to the others
                foreach (var gone in Locks.Keys.Where(k => !crew.Any(c => c.Capacity > 0 && SameName(c.Name, k))).ToList())
                {
                    Locks.Remove(gone);
                    changed = true;
                }
                foreach (var c in crew.Where(x => x.Capacity > 0))
                {
                    var run = _runs.ActiveRunOf(c.Name);
                    var loc = _where.LocationOf(c.Name, true, _hub).Id;   // where he is: the station of his last pickup or delivery (the logs), the map's place in any system, or the one set by hand; else the hub
                    var start = new WorkerStart { Worker = c.Name, Loaded = run?.Loaded ?? 0, LoadedScu = _runs.LoadedScuOf(c.Name), From = loc == PyroDistances.Ruin ? null : loc };
                    if (run != null) start.LoadedTo = _runs.LoadedDestinationsOf(c.Name);   // his run ends where its cargo goes (the planner without a hub)
                    // everybody who flies keeps the stops of his planned trip (Frank, 30 Oct 2026: zzoega's Megumi stop went to zzatvo's plan when zzatvo picked up at
                    // Endgame): they are planned first, in their order, as long as their batches are there and fit; a pickup of his somewhere else lets them go
                    var lockKey = LockKey(c.Name);
                    if (lockKey == null)
                    {
                        Locks[c.Name] = new List<LockedStop>();
                        lockKey = c.Name;
                        changed = true;
                    }
                    var stored = Locks[lockKey];
                    if (stored.Count > 0 && run != null && !FollowsLock(c.Name, stored.Select(x => x.Id).ToList(), stored[0].Since))
                    {
                        stored = new List<LockedStop>();   // he picked up somewhere else: his trips are free again
                        Locks[lockKey] = stored;
                        changed = true;
                    }
                    // a kept stop whose batches are picked up (by him or by somebody else) or gone is done with; the others keep what is still there
                    var keep = new List<LockedStop>();
                    foreach (var x in stored)
                    {
                        var left = Math.Min(x.Batches, remaining.GetValueOrDefault(x.Id) - keep.Where(y => y.Id == x.Id).Sum(y => y.Batches));
                        if (left > 0) keep.Add(new LockedStop { Id = x.Id, Batches = left, Since = x.Since });
                    }
                    if (!SameStops(stored, keep))
                    {
                        Locks[lockKey] = stored = keep;
                        changed = true;
                    }
                    if (stored.Count > 0)
                    {
                        var take = Math.Min(stored[0].Batches, RoomAt(c.Name, stored[0].Id, start.LoadedScu));
                        if (take > 0)
                        {
                            start.FirstId = stored[0].Id;
                            start.FirstBatches = take;
                            remaining[stored[0].Id] -= take;
                            foreach (var x in stored.Skip(1))
                            {
                                start.Keep.Add((x.Id, x.Batches));   // the planner gives back what does not fit in his ship
                                remaining[x.Id] -= x.Batches;
                            }
                        }
                        else
                        {
                            Locks[lockKey] = new List<LockedStop>();   // no room for his first kept stop: a new trip is planned for him
                            changed = true;
                        }
                    }
                    if (start.FirstId == null && (run == null || start.LoadedScu < shipScu.GetValueOrDefault(c.Name))) waiting.Add(c.Name);   // a run with room left keeps the stops of its continuation too
                    // his own log shows the station he is flying to (or has arrived at) since his last pickup or delivery: what he can take there stays his,
                    // locked or not, so planning again for somebody else never takes it away from him while he is on his way or about to load
                    var heading = start.FirstId == null ? GroupAt(remaining, _where.HeadingFor(c.Name)) : null;
                    if (heading != null && remaining.GetValueOrDefault(heading) > 0)
                    {
                        var notHis = limits.TryGetValue(heading, out var hl) ? hl.Where(x => !x.Workers.Contains(c.Name)).Sum(x => x.Batches) : 0;
                        var take = Math.Min(remaining[heading] - notHis, RoomAt(c.Name, heading, start.LoadedScu));
                        if (take > 0)
                        {
                            start.FirstId = heading;
                            start.FirstBatches = take;
                            remaining[heading] -= take;
                        }
                    }
                    if (start.FirstId == null && run == null && start.From != null && GroupAt(remaining, start.From) is { } here && remaining.GetValueOrDefault(here) > 0)
                    {
                        // he said where he is: what is left at that station is right there, so his first trip takes it first (what only the others have is left to them)
                        var onlyOthers = limits.TryGetValue(here, out var gl) ? gl.Where(x => !x.Workers.Contains(c.Name)).Sum(x => x.Batches) : 0;
                        var take = Math.Min(Math.Min(remaining[here] - onlyOthers, remaining[here]), RoomAt(c.Name, here, 0));
                        if (take > 0)
                        {
                            start.FirstId = here;
                            start.FirstBatches = take;
                            remaining[here] -= take;
                        }
                    }
                    if (start.FirstId != null || start.Loaded > 0 || start.From != null) starts.Add(start);
                }
                if (changed) _state.Save();
            }

            var key = string.Join("|", crew.Select(c => c.Name + ":" + c.Capacity + ":" + shipScu.GetValueOrDefault(c.Name) + ":" + c.Drive.Key))
                + "#" + string.Join("|", batchScu.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Key + ":" + kv.Value))
                + "#" + string.Join("|", remaining.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Key + ":" + kv.Value))
                + "#" + string.Join("|", unknown)
                + "#" + string.Join("|", chunks.Select(x => x.Station + ":" + x.Batches + ":" + x.Scu + ":" + x.Cargo + ":" + string.Join(",", x.Allowed)))
                + "#" + string.Join("|", limits.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Key + "=" + string.Join(";", kv.Value.Select(g => string.Join(",", g.Workers) + "x" + g.Batches))))
                + "#" + string.Join("|", starts.Select(t => t.Worker + "=" + t.FirstId + "x" + t.FirstBatches + "+" + string.Join(",", t.Keep.Select(k => k.Id + "x" + k.Batches)) + "/" + t.Loaded + "/" + t.LoadedScu + "@" + t.From + ">" + string.Join(",", t.LoadedTo)));
            if (_planCore != null && _planCore.Key == key && waiting.All(n => _planCore.Settled.Contains(n)))
            {
                _planCore.Trips = Ordered(_planCore.Trips, value, WasteAboard);   // the same trips; what they are worth can be typed again in the rewards table
                return _planCore;
            }

            var note = unknown.Count > 0 ? "Not in the distance table, so left out: " + string.Join(", ", unknown) + "." : "";
            var only = limits.SelectMany(kv => kv.Value.Select(g => StarRoutes.NameOf(kv.Key) + " ×" + g.Batches + " only " + string.Join(" / ", g.Workers))).ToList();
            if (only.Count > 0) note = (note.Length > 0 ? note + "  " : "") + "Not everybody has every mission: " + string.Join(", ", only) + ".";
            var core = new PlanCore
            {
                Key = key,
                Trips = Ordered(PlanTrips(remaining, crew, starts, limits, chunks, batchScu, shipScu, shipFullScu), value, WasteAboard),
                Batches = remaining.Where(kv => kv.Value > 0).Sum(kv => kv.Value) + starts.Sum(t => t.FirstBatches + t.Keep.Sum(k => k.Batches)),
                Note = note,
                Settled = settled,
            };
            _planCore = core;
            if (pass > 0 || _state.Inputs == null) return core;

            // the stops of everybody's first planned trip are his from now on: the ones kept already stay in front, what the planner put after them is kept
            // too (so the whole trip settles; a stop never leaves the list here, only above when it is done with). A worker on his way to nothing kept
            // (his run is handed in, or his kept stops were used up) gets the trip planned for him; nothing planned = nothing to keep
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in crew.Where(x => x.Capacity > 0).Select(x => x.Name))
            {
                var lockKey = LockKey(name);
                if (lockKey == null || !seen.Add(name)) continue;
                var next = core.Trips.FirstOrDefault(t => t.Worker == name);
                var flying = _runs.ActiveRunOf(name);
                var stored = Locks[lockKey];
                if (next == null || NothingToLock(next))
                {
                    if (waiting.Contains(name)) settled.Add(name);
                    if (flying == null && stored.Count == 0) Locks.Remove(lockKey);   // with a run in progress the marker stays: his next trip is kept once the run is handed in
                    continue;
                }
                var since = stored.Count > 0 ? stored[0].Since : flying?.Last;
                var kept = new List<LockedStop>(stored);
                foreach (var (group, _, _, batches) in next.Picks)   // by group (a station's batches to Ruin, or to another station): the keys the kept stops have
                {
                    var had = kept.FirstOrDefault(x => x.Id == group);
                    if (had == null) kept.Add(new LockedStop { Id = group, Batches = batches, Since = since });
                    else if (batches > had.Batches) had.Batches = batches;   // the planner filled the stop up: that is his too
                }
                if (!SameStops(stored, kept)) Locks[lockKey] = kept;
            }
            _state.Save();
        }
    }

    /// <summary>The group of batches at a station with the most left (RoutePlanner.GroupKey: a station's batches to Ruin, or to another station), or null.</summary>
    static string? GroupAt(IReadOnlyDictionary<string, int> remaining, string? station) =>
        station == null ? null : remaining.Where(kv => kv.Value > 0 && RoutePlanner.OriginOf(kv.Key) == station).OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => kv.Key).FirstOrDefault();

    static bool SameStops(List<LockedStop> a, List<LockedStop> b) => a.Count == b.Count && a.Zip(b).All(p => p.First.Id == p.Second.Id && p.First.Batches == p.Second.Batches);

    /// <summary>
    /// The parcels of a hauling job (stage 2): one per pickup when the job has one drop-off (the Multi-to-single hauls), one per drop-off when it has one pickup (the
    /// Single-to-multi ones, the direct hauls, the couriers), each with the stop's SCU. Null when the job cannot be planned: a stop the map has no place for (or only
    /// roughly, the planet), or several pickups and several drop-offs (the game does not say which goes where).
    /// </summary>
    static List<(HaulStop Pickup, HaulStop Drop, double Scu)>? JobParcels(HaulJob j)
    {
        if (j.Pickups.Count == 0 || j.Drops.Count == 0 || (j.Pickups.Count > 1 && j.Drops.Count > 1)) return null;
        if (j.Pickups.Concat(j.Drops).Any(s => s.Place == null || s.Near)) return null;
        return j.Drops.Count == 1
            ? j.Pickups.Select(p => (p, j.Drops[0], p.Scu)).ToList()
            : j.Drops.Select(d => (j.Pickups[0], d, d.Scu)).ToList();
    }

    /// <summary>The plan for the open batches of the selected session, with the runs so far. The trips are only worked out again when the batches or the crew change.</summary>
    public PlanView GetPlan()
    {
        var crew = _crew.ActiveCrew().Select(n => (Name: n, Capacity: _crew.ShipCapacityOf(n), Drive: _crew.QtOf(n))).ToList();
        var remaining = new Dictionary<string, int>();
        var unknown = new List<string>();
        var worth = new Dictionary<string, (decimal Value, int Batches)>();   // station -> what its batches are worth together (the value of a stop is reward / stops of its mission)
        var volume = new Dictionary<string, (double Scu, int Stops)>();   // station -> the SCU of its open stops together (a guess, see StopScu) and how many stops that is
        var limits = new Dictionary<string, List<(string[] Workers, int Batches)>>();   // station -> batches that only some of the crew can take (they have those missions running, the others do not)
        var flyers = crew.Where(c => c.Capacity > 0).Select(c => c.Name).ToList();
        var later = new List<string>();   // the stops of the contracts to a station the distance table does not know, and the jobs with a stop the map has no place for: not planned
        _parcelsOf.Clear();
        if (_state.Inputs != null)
        {
            foreach (var r in _state.OpenRows)
            {
                var left = r.Missions - r.Loaded;   // batches not picked up yet
                if (left <= 0) continue;
                if (r.Job) continue;   // a hauling job of another kind (stage 4): planned from the job itself (below, with its places in any system)
                var station = _places.StationOf(r.Zone);
                // the batches of a contract to another station are a group of their own at the station (RoutePlanner.GroupKey): the planner takes them to that station (stage 1b)
                var to = r.ToRuin ? PyroDistances.Ruin : r.DropZone != null ? _places.StationOf(r.DropZone) : null;
                if (!r.ToRuin && to == null)   // a contract to a station the distance table does not know
                {
                    later.Add(_places.LocationName(r.Zone) + " \u00d7" + left + " to " + (r.DropName ?? (r.DropZone != null ? _places.LocationName(r.DropZone) : "another station")));
                    continue;
                }
                var id = station == null ? null : RoutePlanner.GroupKey(station, to!);
                if (id != null && r.Missions > 0)
                {
                    var was = worth.GetValueOrDefault(id);
                    worth[id] = (was.Value + _earnings.StopValue(r), was.Batches + r.Missions);
                }
                if (id == null)
                {
                    unknown.Add(_places.LocationName(r.Zone) + " (" + left + ")");
                    continue;
                }
                remaining[id] = remaining.GetValueOrDefault(id) + left;
                foreach (var open in r.OpenMissions)
                {
                    var share = CargoRuns.StopScu(open);
                    if (share <= 0) continue;
                    var had = volume.GetValueOrDefault(id);
                    volume[id] = (had.Scu + share, had.Stops + 1);
                }
                foreach (var m in r.OpenMissions)
                {
                    var can = flyers.Where(n => _contracts.HasMission(m, n)).ToArray();   // the Contracts tab: who has this mission
                    if (can.Length == 0 || can.Length == flyers.Count) continue;
                    if (!limits.TryGetValue(id, out var gl)) limits[id] = gl = new List<(string[] Workers, int Batches)>();
                    var same = gl.FindIndex(x => x.Workers.SequenceEqual(can));
                    if (same >= 0) gl[same] = (can, gl[same].Batches + 1);
                    else gl.Add((can, 1));
                }
            }
            // the hauling jobs of the party (the Other hauling board: every mission with drop-offs that is not one of the planner's own contracts) are planned by
            // the planner without a hub (stage 2 of docs/planner-any-hauling.md): a parcel per pickup to the one drop-off, or per drop-off from the one pickup, each
            // a batch of a group of its own (from the pickup's place to the drop-off's, in any system), with its SCU as the board has it and the mission's reward
            // over its parcels. A job with a stop the map has no place for stays out, with a note; one whose cargo is picked up already waits for stage 4 (the runs)
            {
                foreach (var j in HaulJobs.Open(_state).Where(j => _state.Focus == null || _state.Focus(j.Mission)))
                {
                    var parcels = JobParcels(j);
                    if (parcels == null)
                    {
                        later.Add(j.Title + " (" + HaulJobs.StopsText(j.Pickups) + " to " + HaulJobs.StopsText(j.Drops) + ")");
                        continue;
                    }
                    var reward = (decimal)(_state.Jobs.KindOf(j.Mission)?.Auec ?? _state.Jobs.WikiOf(j.Mission)?.Auec ?? 0) / parcels.Count;
                    var can = flyers.Where(n => _contracts.HasMission(j.Mission, n)).ToArray();
                    foreach (var p in parcels.Where(p => !p.Pickup.Done && !p.Drop.Done))
                    {
                        var id = RoutePlanner.GroupKey(StarRoutes.IdOf(p.Pickup.Place!), StarRoutes.IdOf(p.Drop.Place!));
                        if (!_parcelsOf.TryGetValue(id, out var keys)) _parcelsOf[id] = keys = new List<(string Key, int Scu)>();
                        var dropKey = HaulJobs.DropKey(j.Mission, p.Drop);
                        if (keys.All(k => k.Key != dropKey)) keys.Add((dropKey, p.Drop.Exact ? (int)Math.Round(p.Drop.Scu) : 0));
                        var was = worth.GetValueOrDefault(id);
                        worth[id] = (was.Value + reward, was.Batches + 1);
                        remaining[id] = remaining.GetValueOrDefault(id) + 1;
                        var had = volume.GetValueOrDefault(id);
                        volume[id] = (had.Scu + (p.Scu > 0 ? p.Scu : CargoRuns.AssumedMissionScu / parcels.Count), had.Stops + 1);
                        if (can.Length == 0 || can.Length == flyers.Count) continue;
                        if (!limits.TryGetValue(id, out var gl)) limits[id] = gl = new List<(string[] Workers, int Batches)>();
                        var same = gl.FindIndex(x => x.Workers.SequenceEqual(can));
                        if (same >= 0) gl[same] = (can, gl[same].Batches + 1);
                        else gl.Add((can, 1));
                    }
                }
            }
        }

        // cargo that goes out from Ruin (from-Ruin contracts): the planner gives every worker the loads of his missions, each in a trip of its own that unloads at its first stop
        var drops = _dropOffs.OpenDrops();
        var chunks = new List<DropChunk>();
        var dropNote = "";
        if (drops.Count > 0)
        {
            var cap = flyers.Count == 0 ? 0 : crew.Where(c => c.Capacity > 0).Min(c => c.Capacity);
            // the loads for one station go together as far as the biggest ship holds them (the plan's 90%, like the pickups); only a ship that holds a load gets it
            var maxScu = flyers.Count == 0 ? 0 : crew.Where(c => c.Capacity > 0).Max(c => (int)((_crew.ShipOf(c.Name)?.Total ?? c.Capacity * 232) * PlanFill));
            chunks = _dropOffs.DropChunks(drops, flyers, cap, maxScu);
            var lost = drops.Count(d => d.Station == null);
            if (lost > 0) dropNote = "From Ruin: " + lost + (lost == 1 ? " drop-off is" : " drop-offs are") + " to a place that is not in the distance table, so left out.";
        }
        // the ships are filled by SCU: what a batch of every station takes (its open stops on average: mission SCU / stops), and what every ship holds
        var batchScu = remaining.Keys.ToDictionary(id => id, id => Math.Max(1, (int)Math.Round(volume.TryGetValue(id, out var v) && v.Stops > 0 ? v.Scu / v.Stops : CargoRuns.AssumedMissionScu / 3.0)));
        // what every ship holds, less some headroom: the SCU of a batch is an estimate (missions are 500 SCU give or take, and not split evenly over their stations),
        // so the plan fills a ship to 90%, not to the last SCU (a C2: 742 of 824). No ship set: his batches times a third of a C2.
        var shipScu = crew.Where(c => c.Capacity > 0).ToDictionary(c => c.Name, c => (int)((_crew.ShipOf(c.Name)?.Total ?? c.Capacity * 232) * PlanFill));
        var shipFullScu = crew.Where(c => c.Capacity > 0).ToDictionary(c => c.Name, c => _crew.ShipOf(c.Name)?.Total ?? c.Capacity * 232);   // a load from Ruin may fill the ship in full
        decimal PerBatch(string id) => worth.TryGetValue(id, out var x) && x.Batches > 0 ? x.Value / x.Batches : 0;   // the average batch at that station
        decimal TripValue(PlannedTrip t) => t.Picks.Sum(p => p.Batches * PerBatch(p.Group)) + _earnings.WasteValue(t.DropKeys);   // its pickups and the waste it takes out
        // the session's hub: where most of the open batches go (Ruin for the to-Ruin contracts; Port Tressler for a party hauling to it in Stanton). A worker the logs
        // and the map place nowhere starts there
        _hub = remaining.Where(kv => kv.Value > 0).GroupBy(kv => RoutePlanner.DestinationOf(kv.Key)).OrderByDescending(g => g.Sum(kv => kv.Value)).ThenBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => g.Key).FirstOrDefault()
            ?? flyers.SelectMany(_runs.LoadedDestinationsOf).FirstOrDefault(p => p != PyroDistances.Ruin)   // nothing open: where the cargo aboard goes (stage 4b)
            ?? PyroDistances.Ruin;
        var core = CoreFor(crew, remaining, unknown, limits, chunks, batchScu, shipScu, shipFullScu, TripValue);
        var dropTrips = core.Trips;
        // the waste only rides along on the pickup trips: a load that none of them can take along waits (the note says so)
        var carried = dropTrips.SelectMany(pt => pt.Drops.Select(dd => dd.Id)).ToList();
        var waits = new List<string>();
        foreach (var load in chunks)
        {
            var at = carried.IndexOf(load.Station);
            if (at >= 0) carried.RemoveAt(at);
            else waits.Add(load.Cargo + " for " + StarRoutes.NameOf(load.Station));
        }
        if (waits.Count > 0) dropNote = (dropNote + "  Waiting to go along on a pickup trip: " + string.Join(", ", waits) + ".").Trim();
        double ScuPerBatch(string id) => volume.TryGetValue(id, out var y) && y.Stops > 0 ? y.Scu / y.Stops : 0;   // the average stop at that station, in SCU

        var laterNote = later.Count == 0 ? "" : "Not planned (the map has no place for a stop): " + string.Join(", ", later) + ".";
        var plan = new PlanView { Note = (core.Note + "  " + dropNote + "  " + laterNote).Trim() };
        var flying = crew.Where(c => c.Capacity > 0).ToList();
        for (var i = 0; i < flying.Count; i++)
        {
            var c = flying[i];
            var drive = _crew.DriveOf(c.Name);
            var w = new PlanWorkerView
            {
                Name = c.Name,
                Capacity = c.Capacity,
                DriveNote = drive == null ? "no QT drive set (assuming " + QuantumDrives.Assumed.Key + ")" : "",
                Drive = drive == null ? "no QT drive set (assuming " + QuantumDrives.Assumed.Key + ")" : drive.Key + ", " + (drive.Speed / 1e6).ToString("0", CultureInfo.InvariantCulture) + " Mm/s",
            };
            w.RunsSoFar = _state.Trips.Count(x => SameName(x.Worker, c.Name));
            var standing = _where.LocationOf(c.Name, true, _hub);
            w.LocationId = standing.Id;
            w.LocationByHand = standing.ByHand;
            w.AutoLocationId = _where.AutoLocationOf(c.Name, true, _hub);
            w.Rows.AddRange(RunRows(c.Name));
            // the run in progress is his Current trip (a locked stop is marked in the Route cell of a planned trip)
            var runRow = w.Rows.LastOrDefault(r => r.Level == "active");
            if (runRow != null)
            {
                runRow.Label = "Current";
                runRow.First = true;
            }
            var mine = dropTrips.Where(t => t.Worker == c.Name).ToList();
            for (var k = 0; k < mine.Count; k++)
            {
                w.Rows.Add(new PlanRow
                {
                    Label = k > 0 ? "Plan " + (k + 1) : runRow == null ? "Current" : mine[k].Special ? "Continue" : "Next",
                    First = k == 0 && runRow == null,
                    Solid = k == 0 && (runRow == null || mine[k].Special),
                    Locked = mine[k].Locked,
                    Route = RouteText(mine[k]),
                    Batches = mine[k].Batches.ToString(CultureInfo.InvariantCulture),
                    BatchCount = mine[k].Batches,
                    Value = TripValue(mine[k]),
                    Gm = mine[k].Distance,
                    Seconds = mine[k].Seconds,
                    Flight = mine[k].Seconds,
                    Level = "plan",
                    Stops = mine[k].Drops.Select(d => d.Id).Concat(mine[k].Stops.Select(s => s.Id)).Aggregate(new List<string>(), (l, id) => { if (l.Count == 0 || l[^1] != id) l.Add(id); return l; }),
                    Places = mine[k].Places(),
                    From = mine[k].From,
                    Trip = mine[k],
                    Lines = LinesOf(mine[k], c.Name, PerBatch, ScuPerBatch, k == 0 && runRow == null ? w.LocationId : null),
                });
            }
            // what the plan adds to the run in progress is part of his Current row: the stops already picked up, then the ones still to pick up
            if (runRow != null && mine.Count > 0 && mine[0].Special && mine[0].Stops.Count > 0)
            {
                var cont = mine[0];
                runRow.BatchCount += cont.Batches;
                runRow.Value += cont.Picks.Sum(p => p.Batches * PerBatch(p.Group));
                runRow.Batches = runRow.BatchCount.ToString(CultureInfo.InvariantCulture);
                runRow.Stops = runRow.Stops.Concat(cont.Stops.Select(s => s.Id)).ToList();
                var ends = EndsOf(cont);
                runRow.Places = new[] { _hub }.Concat(runRow.Stops).Concat(ends).ToList();
                runRow.Gm = RoutePlanner.PathGm(runRow.Places);
                runRow.Flight = _runs.FlightOf(c.Name, runRow.Stops, ends);   // the whole run, from Ruin to where its cargo goes
                foreach (var (stopId, stopBatches) in cont.Stops)
                    AddCollectLine(runRow.Lines, c.Name, stopId, stopBatches, cont.Picks.Where(p => p.Id == stopId).Sum(p => p.Batches * PerBatch(p.Group)), cont.Picks.Where(p => p.Id == stopId).Sum(p => p.Batches * ScuPerBatch(p.Group)));
                w.Rows.First(r => r.Trip == cont).Hidden = true;
                runRow.Ends = ends.ToList();
            }
            else if (runRow != null && runRow.Stops.Count > 0)
            {
                runRow.OnMap = true;   // nothing is added to it (full ship): the run itself is his solid line
                runRow.Solid = true;
                runRow.Passed = runRow.Stops.Count;   // everything on it is picked up: he is on his way home
                // ... and home already when the map has him at Ruin: the leg home is flown too, so nothing of the run is left to draw (Frank, 30 Oct 2026: the
                // arrow with the waves from the last stop to Ruin stayed on the map after he had arrived, until he handed in)
                if (_where.MapStationOf(c.Name) == PyroDistances.Ruin) runRow.Passed++;
            }
            if (runRow != null && runRow.Lines.Count > 0)
            {
                AddEndLines(runRow.Lines, c.Name, runRow.BatchCount, runRow.Ends.Count > 0 ? runRow.Ends : _runs.LoadedDestinationsOf(c.Name));   // the run ends where its cargo goes
                runRow.Route = string.Join("  \u2192  ", runRow.Lines.Select(l => l.Name));
            }
            // he has taken the waste out at Ruin already (his run has nothing else in it yet): the Ruin line of his first trip says so. "Collecting Waste" while he is
            // still at it (he has not set his next quantum destination, only known from his own log), then "Collected Waste ✓" and his colour moves on to the next stop
            if (runRow == null && mine.Count > 0 && mine[0].From == null && mine[0].Drops.Count > 0)
            {
                var takenRun = WasteTakenOut(c.Name);
                var firstRow = w.Rows.FirstOrDefault(r => r.Trip == mine[0]);
                var startLine = firstRow?.Lines.FirstOrDefault(l => l.Id == PyroDistances.Ruin && !l.Stop);
                if (takenRun != null && startLine != null)
                {
                    var loadedAt = takenRun.Collects.Max(x => x.Time);
                    // the game completes every pickup of the contract at once, however much is taken out, so the load of this trip is taken to be what he took
                    // (its SCU and containers stay as planned)
                    var stillLoading = _where.StillLoading(c.Name, loadedAt);
                    var loads = mine[0].Drops[0].Batches;
                    startLine.Actions.Clear();
                    startLine.Actions.Add((stillLoading ? "Collecting " : "Collected ") + (mine[0].DropCargo ?? takenRun.Collects[0].Cargo) + (loads > 1 ? " \u00d7" + loads : "") + (stillLoading ? "" : " \u2713"));
                    startLine.Done = true;
                    startLine.Busy = stillLoading;
                }
            }
            // the first line is his Current trip (the Current Route table), the others are his Queued routes, numbered 1, 2, ...
            var number = 0;
            foreach (var r in w.Rows.Where(x => !x.Hidden))
            {
                r.Label = number == 0 ? "Current" : number.ToString(CultureInfo.InvariantCulture);
                number++;
            }
            // the worker colour is on the line of the Current Route where he is
            var currentRow = w.Rows.FirstOrDefault(x => !x.Hidden);
            if (currentRow != null) currentRow.Here = HereLine(currentRow.Lines, w.LocationId, w.LocationByHand);
            if (currentRow != null && currentRow.Here > 0 && currentRow.Here < currentRow.Lines.Count)
            {
                // the step he is on is a delivery: he is on his way there (the step before it is done), so it says Delivering, like Collecting at a pickup
                var here = currentRow.Lines[currentRow.Here];
                for (var a = 0; a < here.Actions.Count; a++)
                {
                    if (here.Actions[a] == "Deliver" || here.Actions[a].StartsWith("Deliver ", StringComparison.Ordinal)) here.Actions[a] = "Delivering" + here.Actions[a]["Deliver".Length..];
                }
            }
            plan.Workers.Add(w);
        }

        var planned = plan.Workers.SelectMany(w => w.Planned).ToList();
        if (flying.Count == 0)
        {
            plan.Summary = "Nobody is flying. Tick Active and set a Batch Capacity for the crew in Options → Party…";
        }
        else if (planned.Count == 0)
        {
            plan.Summary = "No open batches to pick up.";
        }
        else
        {
            var longest = plan.Workers.OrderByDescending(w => w.Planned.Sum(r => r.Seconds)).First();
            plan.Summary = planned.Count + " trips planned, " + Gm(planned.Sum(r => r.Gm)) + " in total for " + core.Batches + " batches. Longest: "
                + longest.Name + " with " + Gm(longest.Planned.Sum(r => r.Gm)) + ", about " + FlightSpan(longest.Planned.Sum(r => r.Seconds)) + " of flying.";
        }

        plan.Signature = plan.Summary + "¦" + string.Join("|", plan.Workers.Select(x => x.Name + "@" + x.LocationId + (x.LocationByHand ? "!" : "") + ">" + x.AutoLocationId)) + "¦" + string.Join("¦", plan.Workers.SelectMany(w =>
            w.Rows.Select(r => w.Name + "|" + w.Capacity + "|" + r.Label + "|" + r.Hidden + "|" + r.Locked + "|" + r.First + "|" + r.Solid + "|" + r.Route + "|" + r.Batches + "|" + r.Value.ToString("F0", CultureInfo.InvariantCulture) + "|" + r.Level + "|" + r.Gm.ToString("F1", CultureInfo.InvariantCulture) + "|" + r.Flight.ToString("F0", CultureInfo.InvariantCulture) + "|" + r.Seconds.ToString("F0", CultureInfo.InvariantCulture) + "|" + r.Here + "|" + string.Join(";", r.Lines.Select(l => l.Name + "~" + l.ActionText + "~" + l.ScuText + "~" + l.BoxesText + "~" + l.Batches + "~" + l.Value.ToString("F0", CultureInfo.InvariantCulture) + "~" + l.Gm.ToString("F1", CultureInfo.InvariantCulture) + "~" + l.Seconds.ToString("F0", CultureInfo.InvariantCulture))) + "|" + w.Drive + "|" + w.RunsSoFar)));
        return plan;
    }
}
