namespace SCTools.Tracking;

/// <summary>
/// The runs of the session (Cargo Runs): who did them (from the logs and the Trip Planner, see WorkerTripBuilder.Build), what they did in what order, how long
/// they took and what they earned per hour, their flight time, the cargo lost in them, and the run each worker is flying now.
/// </summary>
public sealed class CargoRuns
{
    /// <summary>A run that has only waste taken out at Ruin or delivered in it (nothing picked up) is not a run on the way any more once it is this old: nobody is flying it.</summary>
    public static readonly TimeSpan StaleLeg = TimeSpan.FromHours(3);

    /// <summary>The SCU of a hauling mission when the game has not written it (its "Deliver 0/N SCU" popup): they are all about 500 SCU, give or take 30.</summary>
    public const int AssumedMissionScu = 500;

    readonly TrackerState _s;
    readonly Places _places;
    readonly Crew _crew;
    readonly Earnings _earnings;

    public CargoRuns(TrackerState state, Places places, Crew crew, Earnings earnings)
    {
        _s = state;
        _places = places;
        _crew = crew;
        _earnings = earnings;
    }

    // ------------------------------------------------------------------ the runs

    /// <summary>The runs of a session: who did them comes from the logs and the Trip Planner (see WorkerTripBuilder.Build). Someone who left the party ended his run then.</summary>
    public List<WorkerTrip> BuildRuns(SessionGroup g, SessionInputs inp)
    {
        var data = _s.Data;
        data.PlanCreditSince ??= DateTime.UtcNow;
        // someone who leaves the party stops flying: the run he has on the way ends then (not a failed run)
        var ends = inp.ManualEnds.Concat(data.PartyChanges.Where(c => !c.Active).Select(c => new ManualEnd { Worker = c.Name, Time = c.TimeUtc, Left = true })).ToList();
        return WorkerTripBuilder.Build(g, (z, t) => WorkerTripBuilder.WorkerAt(inp, z, t), _s.Log!, ends, (data.PlanCreditSince.Value, inp.PickupClaims));
    }

    /// <summary>
    /// The runs of the session shown, latest activity on top: a run that gets completed jumps to the top. Runs that were handed in before the session started belong to
    /// an earlier session. Empty while no session is shown.
    /// </summary>
    public List<WorkerTrip> SessionRuns()
    {
        if (_s.Current == null || _s.Inputs == null || _s.Log == null) return new List<WorkerTrip>();
        var sessionStart = _s.Current.Start;
        return BuildRuns(_s.Current, _s.Inputs)
            .Where(t => !t.End.HasValue || t.End.Value >= sessionStart)
            .OrderByDescending(RunLastActivity).ThenByDescending(RunStartedAt).ThenByDescending(t => t.Start).ThenBy(t => t.Worker, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>The latest thing that happened in a run: its hand-in when it is completed, otherwise the last pickup.</summary>
    public static DateTime RunLastActivity(WorkerTrip t)
    {
        var last = t.End ?? t.Start;
        if (t.LastEvent is DateTime latest && latest > last) last = latest;
        return last;
    }

    /// <summary>When a run really started: the first thing done in it (cargo taken out at Ruin, delivered from there, or picked up). A run begins at the previous hand-in, but the cargo run itself starts when cargo is moved. A run with nothing in it uses its start time.</summary>
    public static DateTime RunStartedAt(WorkerTrip t) => t.FirstEvent ?? t.Start;

    /// <summary>Groups things that happened close together (and are alike) into one: the three batches of waste taken out at Ruin at the same moment are one step.</summary>
    public static List<List<T>> Clusters<T>(IEnumerable<T> items, Func<T, DateTime> time, Func<T, string> key)
    {
        var all = new List<List<T>>();
        foreach (var x in items.OrderBy(time))
        {
            var open = all.Count > 0 ? all[^1] : null;
            if (open != null && key(open[0]) == key(x) && time(x) - time(open[0]) <= TimeSpan.FromMinutes(2)) open.Add(x);
            else all.Add(new List<T> { x });
        }
        return all;
    }

    // ------------------------------------------------------------------ lost cargo

    /// <summary>The cargo lost in a run, as ticked on Cargo Runs: None, Little or All.</summary>
    public static string LostLevelOf(SessionInputs inp, string key)
    {
        if (inp.LostCargo.TryGetValue(key, out var level)) return level;
        return inp.LostRuns.Contains(key) ? "All" : "None";
    }

    /// <summary>The cargo lost in a run of the session shown.</summary>
    public string LostLevel(string key) => _s.Inputs == null ? "None" : LostLevelOf(_s.Inputs, key);

    /// <summary>A run is failed when it was ended by hand or all of its cargo was lost.</summary>
    public static bool RunFailed(WorkerTrip t, SessionInputs inp) => t.Manual || LostLevelOf(inp, t.Key) == "All";

    /// <summary>
    /// Lost Cargo of a run was picked: None, Little or All. All lost = the run failed: one that is still on the way ends here, and picking something else brings it back.
    /// True when that changed the runs (they have to be worked out again).
    /// </summary>
    public bool SetLostLevel(WorkerTrip trip, string level)
    {
        var inp = _s.Inputs;
        if (inp == null) return false;
        inp.LostRuns.Remove(trip.Key);
        if (level == "None") inp.LostCargo.Remove(trip.Key);
        else inp.LostCargo[trip.Key] = level;
        if (level == "All" && !trip.End.HasValue)
        {
            inp.ManualEnds.Add(new ManualEnd { Worker = trip.Worker, Time = DateTime.UtcNow });
            return true;
        }
        if (level != "All" && trip.Manual && trip.End.HasValue)
        {
            var at = trip.End.Value;
            inp.ManualEnds.RemoveAll(x => SameName(x.Worker, trip.Worker) && x.Time == at);
            return true;
        }
        return false;
    }

    // ------------------------------------------------------------------ what a run did, its time and flight

    /// <summary>
    /// What a run did, in the order it happened, for the Pickup Location &amp; Time column: waste taken out at Ruin ("Waste from Ruin 13:19"), waste delivered to a station
    /// ("Waste → Rat's Nest 15:55") and the pickups ("Rod's Fuel 15:36").
    /// </summary>
    public string RunStepsText(WorkerTrip t)
    {
        var steps = new List<(DateTime Time, string Text)>();
        foreach (var c in Clusters(t.Collects, x => x.Time, x => x.Cargo))
            steps.Add((c[0].Time, c[0].Cargo + " from Ruin " + FormatTime(c[0].Time)));   // the game completes every pickup of the contract at once, however much is taken: no count
        foreach (var c in Clusters(t.Drops, x => x.Time, x => x.Cargo + "|" + x.Zone))
            steps.Add((c[0].Time, c[0].Cargo + (c.Count > 1 ? " ×" + c.Count : "") + " → " + _places.LocationName(c[0].Zone) + " " + FormatTime(c[0].Time)));
        foreach (var p in t.Pickups) steps.Add((p.Time, _places.LocationName(p.Zone) + " " + FormatTime(p.Time)));
        return string.Join(",  ", steps.OrderBy(s => s.Time).Select(s => s.Text));
    }

    /// <summary>Only the places of a run, in the order it went there ("Ruin, Gaslight, Dudley"), for the short Cargo Runs of the Trip Planner.</summary>
    public string RunPlacesText(WorkerTrip t)
    {
        var steps = new List<(DateTime Time, string Place)>();
        foreach (var c in Clusters(t.Collects, x => x.Time, x => x.Cargo)) steps.Add((c[0].Time, "Ruin"));
        foreach (var c in Clusters(t.Drops, x => x.Time, x => x.Cargo + "|" + x.Zone)) steps.Add((c[0].Time, _places.LocationName(c[0].Zone)));
        foreach (var p in t.Pickups) steps.Add((p.Time, _places.LocationName(p.Zone)));
        var places = new List<string>();
        foreach (var (_, place) in steps.OrderBy(x => x.Time))
        {
            if (places.Count == 0 || places[^1] != place) places.Add(place);   // waste dropped and cargo picked up at the same station is one place
        }
        return string.Join(", ", places);
    }

    /// <summary>The Batches cell of a run: failed, left (he left the party), the missions handed in, or nothing while it is on the way.</summary>
    public string BatchesText(WorkerTrip t)
    {
        var level = LostLevel(t.Key);
        return t.Manual || level == "All" ? "failed" : t.Left ? "left" : t.End.HasValue ? t.HandedIn.ToString() : "";
    }

    /// <summary>Time from the start of the run to its end, or to now while the run is still on the way (while the clocks are running).</summary>
    public string RunDuration(WorkerTrip t)
    {
        if (t.End.HasValue) return LongDuration(t.End.Value - t.Start);
        if (!_s.ClocksRunning) return "";   // no active contract: nothing is counting
        var d = DateTime.UtcNow - t.Start;
        return d > TimeSpan.Zero ? LongDuration(d) : "";
    }

    /// <summary>What a run earned per hour: its value over its time (a run on the way: so far). Empty while it is under a minute old or has no value.</summary>
    public string RunHourlyText(WorkerTrip t)
    {
        TimeSpan span;
        if (t.End.HasValue) span = t.End.Value - t.Start;
        else
        {
            if (!_s.ClocksRunning) return "";
            span = DateTime.UtcNow - t.Start;
        }
        var value = _earnings.RunValue(t);
        if (span < TimeSpan.FromMinutes(1) || value <= 0) return "";
        return (value / (decimal)span.TotalHours).ToString("N0");
    }

    /// <summary>Gm flown in one run: Ruin, the stations it went to (waste delivered, pickups) in the order they were visited, and back to Ruin. Locations missing from the table are skipped.</summary>
    public static double RunDistance(WorkerTrip t, Func<string, string?> stationOf) =>
        PyroDistances.Run(t.VisitedZones().Select(z => stationOf(z)).OfType<string>());

    /// <summary>The estimated flying time of Ruin, these stations and back to Ruin with the drive of the worker.</summary>
    public double FlightOf(string name, IEnumerable<string> stops)
    {
        var drive = _crew.QtOf(name);
        var from = PyroDistances.Ruin;
        double seconds = 0;
        foreach (var id in stops)
        {
            seconds += drive.Seconds(PyroDistances.Between(from, id));
            from = id;
        }
        return seconds + drive.Seconds(PyroDistances.Between(from, PyroDistances.Ruin));
    }

    /// <summary>The estimated flying time of a run on the Cargo Runs board: Ruin, the stations it went to (waste delivered, pickups) in order, and back to Ruin.</summary>
    public string RunFlightText(WorkerTrip t)
    {
        var stops = t.VisitedZones().Select(z => _places.StationOf(z)).Where(id => id != null).Select(id => id!).ToList();
        var seconds = stops.Count > 0 ? FlightOf(t.Worker, stops) : 0;
        return seconds <= 0 ? "" : "~" + FlightSpan(seconds);
    }

    // ------------------------------------------------------------------ the run a worker is flying now

    /// <summary>The run the worker is flying (picked up, not handed in yet): the batches on board and the station of the last pickup, or null. A run with only waste in it has no cargo of the plan on board.</summary>
    public (int Loaded, string? From, DateTime Last)? ActiveRunOf(string name)
    {
        var loaded = 0;
        string? from = null;
        DateTime? latest = null;
        var any = false;
        foreach (var t in _s.Trips.Where(t => SameName(t.Worker, name) && !t.End.HasValue && t.Pickups.Count > 0))
        {
            any = true;
            foreach (var p in t.Pickups)
            {
                loaded += p.Missions;
                if (latest == null || p.Time > latest)
                {
                    latest = p.Time;
                    from = _places.StationOf(p.Zone);
                }
            }
        }
        return any ? (loaded, from, latest ?? DateTime.MinValue) : null;
    }

    /// <summary>True when the worker has a run that has pickups but is not handed in yet.</summary>
    public bool HasActiveRun(string name) => ActiveRunOf(name) != null;

    /// <summary>The run a worker is flying now (not handed in). A run with only waste in it counts while it is recent.</summary>
    public WorkerTrip? OpenRunOf(string name) =>
        _s.Trips.Where(t => SameName(t.Worker, name) && !t.End.HasValue
                && (t.Pickups.Count > 0 || (t.Drops.Count > 0 && _s.Log != null && _s.Log.LastTime - RunLastActivity(t) <= StaleLeg)))
            .OrderBy(t => t.Start).LastOrDefault();

    // ------------------------------------------------------------------ SCU

    /// <summary>
    /// One stop's share of the SCU of a hauling mission: the SCU of the whole mission (the "Deliver 0/490 SCU of Carbon to Ruin Station" popup) divided by its pickup stops.
    /// The log does not say how much is loaded at each stop, so this is a guess; the game did not say the SCU (yet): these contracts are about 500 SCU.
    /// </summary>
    public static double StopScu(Mission m)
    {
        if (m.Multi <= 0) return 0;
        var all = m.ObjScu.Values.Sum();
        return (all > 0 ? all : AssumedMissionScu) / (double)m.Multi;
    }

    /// <summary>The SCU still to pick up at a location: its stops that are not picked up yet (a guess, see StopScu).</summary>
    public static double WaitingScu(LocationRow r) => r.OpenMissions.Sum(StopScu);

    /// <summary>The Cargo cell of the Stops board: "~835 SCU", or nothing when everything there is picked up.</summary>
    public static string WaitingScuText(LocationRow r)
    {
        var scu = WaitingScu(r);
        return scu < 0.5 ? "" : "~" + Math.Round(scu).ToString("N0") + " SCU";
    }

    /// <summary>The SCU of the batches of one pickup (a guess, see StopScu).</summary>
    public double PickupScu(TripPickup p)
    {
        if (_s.Current == null) return 0;
        double total = 0;
        foreach (var id in p.MissionIds)
        {
            var m = _s.Current.Missions.FirstOrDefault(x => x.Id == id) ?? _s.Current.Failed.FirstOrDefault(x => x.Id == id);
            if (m != null) total += StopScu(m);
        }
        return total;
    }

    /// <summary>The SCU he carries in his run in progress (an estimate: mission SCU / stops for every batch).</summary>
    public int LoadedScuOf(string name) =>
        (int)Math.Round(_s.Trips.Where(t => SameName(t.Worker, name) && !t.End.HasValue).SelectMany(t => t.Pickups).Sum(PickupScu));
}
