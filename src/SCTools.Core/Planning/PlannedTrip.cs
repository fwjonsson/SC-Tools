namespace SCTools.Planning;

/// <summary>
/// One suggested trip of one worker: where it starts, the pickup stops in flying order with the batches taken at each, and the places it delivers at (its
/// ends), in flying order. Since stage 1b of docs/planner-any-hauling.md a trip has no hub: it starts where the worker is (or where his trip before it
/// ended) and ends at the last place it delivers at; for the contracts to Ruin that is Ruin both times, as it always was.
/// </summary>
public sealed class PlannedTrip
{
    public string Worker = "";
    public List<(string Id, int Batches)> Stops = new();   // the pickup stations in flying order, with the batches picked up at each (every group there together)
    public List<(string Group, string Id, string To, int Batches)> Picks = new();   // the same by group (RoutePlanner.GroupKey): the station, where the batches go, how many
    public List<string> Ends = new();   // the places delivered at after the pickups, in flying order (Ruin for the contracts to Ruin); the trip ends at the last
    public int Batches => Stops.Sum(s => s.Batches);
    public double Distance;   // Gm: the start, the stops in order, the ends in order
    public double Seconds;    // estimated flight time with this worker's quantum drive (the time of every jump, see QtDrive.Seconds)
    public bool Locked;       // the first stop is locked to this worker: the planner only plans the rest of the trip around it
    public bool Special;      // his first trip: it continues a run in progress and/or starts at his locked stop
    public string? From;      // where the trip starts when that is not Ruin: the station of a run in progress, where he is, or where his trip before it ended
    public int Room;          // how many batches this trip can hold (the ship, minus what a run in progress carries already)
    public double Fill;       // how full the ship is on this trip (0 to 1, with what a run in progress carries already): a trip that is not worth flying yet goes to the back of the queue
    public List<(string Id, int Batches)> Drops = new();   // a from-Ruin contract: this trip leaves Ruin with the load for its FIRST stop (at most one entry) and unloads it there, before the pickups
    public string? DropCargo; // what it is (Waste ...)
    public int DropScu;       // how many SCU the load is (0 = not known)
    public List<int> DropParts = new();     // the SCU of every drop-off in it, one by one (0 = not known)
    public List<string> DropKeys = new();   // "mission id|objective id" of every drop-off in it, in the same order

    /// <summary>Where the trip starts: From, or Ruin.</summary>
    public string Start => From ?? PyroDistances.Ruin;

    /// <summary>Where the trip ends: its last end, else the station of its load (a trip just for a load), else Ruin (a trip made without ends: everything goes to Ruin).</summary>
    public string End => Ends.Count > 0 ? Ends[^1] : Stops.Count == 0 && Drops.Count > 0 ? Drops[0].Id : PyroDistances.Ruin;

    /// <summary>Every place of the trip in flying order: the start, the stop with only the load (if any), the stops, the ends (a place is never twice in a row).</summary>
    public List<string> Places()
    {
        var all = new List<string> { Start };
        void Add(string id) { if (all[^1] != id) all.Add(id); }
        if (Drops.Count > 0 && (Stops.Count == 0 || Stops[0].Id != Drops[0].Id)) Add(Drops[0].Id);
        foreach (var (id, _) in Stops) Add(id);
        foreach (var id in Ends) Add(id);
        return all;
    }

    public PlannedTrip Copy() => new()
    {
        Worker = Worker, Stops = new List<(string Id, int Batches)>(Stops), Picks = new List<(string Group, string Id, string To, int Batches)>(Picks), Ends = new List<string>(Ends),
        Distance = Distance, Seconds = Seconds, Locked = Locked, Special = Special, From = From, Room = Room, Fill = Fill, Drops = new List<(string Id, int Batches)>(Drops),
        DropCargo = DropCargo, DropScu = DropScu, DropParts = new List<int>(DropParts), DropKeys = new List<string>(DropKeys),
    };
}
