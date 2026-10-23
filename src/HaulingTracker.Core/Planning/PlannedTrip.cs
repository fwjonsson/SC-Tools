namespace HaulingTracker.Planning;

/// <summary>One suggested trip of one worker: the stops in flying order, with the batches taken at each.</summary>
public sealed class PlannedTrip
{
    public string Worker = "";
    public List<(string Id, int Batches)> Stops = new();
    public int Batches => Stops.Sum(s => s.Batches);
    public double Distance;   // Gm: the start (Ruin, or where a run in progress is), the stops in order, back to Ruin
    public double Seconds;    // estimated flight time with this worker's quantum drive (the time of every jump, see QtDrive.Seconds)
    public bool Locked;       // the first stop is locked to this worker: the planner only plans the rest of the trip around it
    public bool Special;      // his first trip: it continues a run in progress and/or starts at his locked stop
    public string? From;      // a run in progress: the trip continues from this station (null = it starts at Ruin)
    public int Room;          // how many batches this trip can hold (the ship, minus what a run in progress carries already)
    public double Fill;       // how full the ship is on this trip (0 to 1, with what a run in progress carries already): a trip that is not worth flying yet goes to the back of the queue
    public List<(string Id, int Batches)> Drops = new();   // a from-Ruin contract: this trip leaves Ruin with the load for its FIRST stop (at most one entry) and unloads it there, before the pickups
    public string? DropCargo; // what it is (Waste ...)
    public int DropScu;       // how many SCU the load is (0 = not known)
    public List<int> DropParts = new();
    public List<string> DropKeys = new();   // "mission id|objective id" of every drop-off in it, in the same order   // the SCU of every drop-off in it, one by one (0 = not known)

    public PlannedTrip Copy() => new()
    {
        Worker = Worker, Stops = new List<(string Id, int Batches)>(Stops), Distance = Distance, Seconds = Seconds, Locked = Locked,
        Special = Special, From = From, Room = Room, Fill = Fill, Drops = new List<(string Id, int Batches)>(Drops), DropCargo = DropCargo, DropScu = DropScu, DropParts = new List<int>(DropParts), DropKeys = new List<string>(DropKeys),
    };
}
