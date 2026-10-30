namespace SCTools.Tracking;

/// <summary>
/// Where every worker is and what he is doing: his quantum travel (only in his own log), the station he is flying to or still loading at, and his Current
/// location (where the plan starts his next trip). The logs keep it up to date; a station set by hand holds until the logs show him moving. The map's
/// places (TrackerState.Positions: a crew member's jumps through the pilot's log, the destinations and areas named on Map places, the mission objectives)
/// count where they are newer than anything the planner saw him do (Frank, 29 Oct 2026: the map and the planner learn from each other).
/// </summary>
public sealed class Whereabouts
{
    readonly TrackerState _s;
    readonly CargoRuns _runs;
    readonly DropOffs _drops;

    public Whereabouts(TrackerState state, CargoRuns runs, DropOffs drops)
    {
        _s = state;
        _runs = runs;
        _drops = drops;
    }

    /// <summary>The quantum travel steps in the log of this worker, or null when there is no log of him.</summary>
    public List<QtEvent>? TravelOf(string name)
    {
        if (_s.Log == null) return null;
        foreach (var kv in _s.Log.QtByPlayer)
        {
            if (SameName(kv.Key, name)) return kv.Value;
        }
        return null;
    }

    /// <summary>Now: the clock while the game is watched live, else the time of the last line of the logs.</summary>
    DateTime Now => _s.Live ? DateTime.UtcNow : _s.Log!.LastTime;

    /// <summary>
    /// True while a worker whose log shows his quantum travel has taken cargo out (or delivered it) at a place and has not selected the next destination yet: he is still
    /// loading. A worker without a log of his own is never loading, and neither is one whose log has no quantum travel before this moment (the game did not write it).
    /// </summary>
    public bool StillLoading(string name, DateTime since)
    {
        if (since == default || _s.Log == null) return false;
        var travel = TravelOf(name);
        if (travel == null || travel.Count == 0) return false;
        if (!travel.Any(e => e.Time < since)) return false;
        if (travel.Any(e => e.Kind == QtKind.Selected && e.Time > since)) return false;
        return Now - since < TimeSpan.FromMinutes(15);   // somebody who stopped playing is not loading for hours
    }

    /// <summary>
    /// The station a worker is flying to, or has arrived at and is loading at: the latest quantum destination in his own log, when he set it after his last pickup,
    /// delivery or hand-in. Null when there is no log of him, when it is Ruin or a place that is not in the distance table, or when it was set long ago.
    /// </summary>
    public string? HeadingFor(string name)
    {
        if (_s.Log == null) return null;
        var travel = TravelOf(name);
        var last = travel?.Where(e => e.Kind == QtKind.Selected).OrderBy(e => e.Time).LastOrDefault();
        var moved = LastMoveOf(name);
        (string? Id, DateTime Time)? own = last != null && last.Time > moved ? (PyroDistances.IdOfQuantumTarget(last.Destination), last.Time) : null;
        // the map's destination for him (a crew member's jump is in the pilot's log, a destination named on Map places), when it is the newer one
        (string? Id, DateTime Time)? byMap = MapPosition(name) is { HeadingStation: { } to, HeadingSince: { } since } && since > moved ? (to, since) : null;
        var pick = byMap != null && (own == null || byMap.Value.Time > own.Value.Time) ? byMap : own;
        if (pick == null) return null;
        if (Now - pick.Value.Time > TimeSpan.FromHours(1)) return null;   // somebody who stopped playing is not on his way any more
        var id = pick.Value.Id;
        return id == null || id == PyroDistances.Ruin ? null : id;
    }

    /// <summary>The latest time the logs show the worker picking up, handing in or delivering a load from Ruin (the minimum time = nothing yet).</summary>
    public DateTime LastMoveOf(string name)
    {
        var last = DateTime.MinValue;
        foreach (var t in _s.Trips.Where(t => SameName(t.Worker, name)))
        {
            if (t.End.HasValue && t.End.Value > last) last = t.End.Value;
            foreach (var p in t.Pickups)
            {
                if (p.Time > last) last = p.Time;
            }
        }
        var drop = _drops.LastDropOf(name);
        return drop != null && drop.Value.Time > last ? drop.Value.Time : last;
    }

    /// <summary>
    /// Where the logs say the worker is: at the station of his last pickup (a run in progress), or of the last load of a from-Ruin mission he delivered, whichever
    /// is later; else at Ruin Station (nothing yet, or his run is handed in).
    /// </summary>
    public string AutoLocationOf(string name)
    {
        var run = _runs.ActiveRunOf(name);
        var drop = _drops.DropStartOf(name);
        // the map's place for him when it is newer than anything the planner saw him do (a crew member's jump through the pilot's log, a destination or an
        // area named on Map places, a mission's objective: TrackerState.Positions), but not in the middle of a run: his trip is planned from the station of his
        // last pickup, and the waste he has taken out at Ruin is planned from Ruin (Frank, 30 Oct 2026: with the map saying Patch City while he flew the
        // waste there, the Ruin "Collect Waste" row and the "Deliver Waste" of the Patch City row vanished until he had delivered)
        if (run == null && _runs.WasteTakenOut(name) == null && MapPosition(name) is { AtStation: { } at, Since: { } since } && since > LastMoveOf(name)) return at;
        if (run?.From != null && (drop == null || run.Value.Last >= drop.Value.Time)) return run.Value.From!;
        return drop?.Station ?? PyroDistances.Ruin;
    }

    /// <summary>Where the map has this worker (TrackerState.Positions), or null when it has nothing on him.</summary>
    WorkerPosition? MapPosition(string name) => _s.Positions.FirstOrDefault(kv => SameName(kv.Key, name)).Value;

    /// <summary>
    /// True when the logs show the worker doing something after this time that a station set by hand cannot outlast: taking waste out at Ruin (he is at Ruin then),
    /// or selecting a quantum destination in his own log (he is on his way, and the logs follow him from there).
    /// </summary>
    public bool ActedSince(string name, DateTime since)
    {
        if (_s.Trips.Any(t => SameName(t.Worker, name) && t.Collects.Any(c => c.Time > since))) return true;
        var travel = TravelOf(name);
        if (travel != null && travel.Any(e => e.Kind == QtKind.Selected && e.Time > since)) return true;
        return (MapPosition(name)?.HeadingSince ?? DateTime.MinValue) > since;   // the map saw him set off (a crew member's jump)
    }

    /// <summary>
    /// The Current location of a worker: where he is now, and where the plan starts his next trip. The logs keep it up to date (AutoLocationOf: every pickup, every
    /// delivery from Ruin, every hand-in). A station set by hand (for example at the start of a session, or when the logs miss something) holds until the logs
    /// show him doing something after that, then the logs take over again.
    /// </summary>
    public (string Id, bool ByHand) LocationOf(string name)
    {
        var kv = _s.Data.StartPoints.FirstOrDefault(k => SameName(k.Key, name));
        if (kv.Value != null)
        {
            if (LastMoveOf(name) > kv.Value.Since || ActedSince(name, kv.Value.Since) || (MapPosition(name)?.Since ?? DateTime.MinValue) > kv.Value.Since)
            {
                _s.Data.StartPoints.Remove(kv.Key);   // he has picked up, delivered, handed in, taken waste out, set a quantum destination or arrived somewhere (the map) since: the logs know better
                _s.Save();
            }
            else if (PyroDistances.Points.Any(s => s.Id == kv.Value.Id))
            {
                return (kv.Value.Id, true);
            }
        }
        return (AutoLocationOf(name), false);
    }

    /// <summary>Sets (or, with null, clears) where a worker is by hand. The same station as the logs say is no choice at all; Ruin Station by hand is one.</summary>
    public void SetLocation(string name, string? stationId)
    {
        foreach (var k in _s.Data.StartPoints.Keys.Where(k => SameName(k, name)).ToList()) _s.Data.StartPoints.Remove(k);
        // Ruin Station by hand: the station of his last delivery from Ruin does not take over then
        if (stationId != null && stationId != AutoLocationOf(name)) _s.Data.StartPoints[name] = new StartPoint { Id = stationId, Since = DateTime.UtcNow };
    }
}
