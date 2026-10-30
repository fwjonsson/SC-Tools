using SCTools.GameData;
using SCTools.Planning;

// Scenarios from real sessions: the open batches, the crew and what they carry, and the plan the planner makes for them.

static string Name(string id) => PyroDistances.NameOf(id);
static string Id(string name) => PyroDistances.Points.First(p => p.Name == name).Id;

// The plan of the planner without a hub (RoutePlanner, the Route Planner (new) tab); where the old planner (TripPlanner, the Route Planner tab) plans the same
// scenario differently, its trips follow, so a difference between the two is seen here (they are tried out side by side, docs/planner-any-hauling.md).
static void Show(string title, List<PlannedTrip> trips, List<PlannedTrip>? old = null)
{
    Console.WriteLine("== " + title);
    var lines = trips.Select(Line).ToList();
    lines.ForEach(Console.WriteLine);
    if (old != null && !old.Select(Line).SequenceEqual(lines))
    {
        Console.WriteLine("-- the old planner:");
        old.ForEach(t => Console.WriteLine(Line(t)));
    }
    Console.WriteLine();
}

static string Line(PlannedTrip t)
{
    var drop = t.Drops.Count > 0 ? "  [waste for " + Name(t.Drops[0].Id) + "]" : "";
    var from = t.From != null ? Name(t.From) + " -> " : "Ruin -> ";
    var ends = string.Join(" -> ", PlanText.EndsOf(t).Select(e => e == PyroDistances.Ruin ? "Ruin" : Name(e)));   // where the trip ends: Ruin, or the station its cargo goes to (stage 1b)
    return $"{t.Worker,-8} {(t.Special ? "NOW " : "    ")}{from}{string.Join(" -> ", t.Stops.Select(s => Name(s.Id) + " x" + s.Batches))} -> {ends}  fill {t.Fill:P0}  {t.Seconds / 60:0.0} min{drop}";
}

var ts2 = QuantumDrives.Find("TS-2") ?? QuantumDrives.Assumed;

// 23 Oct 00:47: zzoega has picked up 2 batches (417 SCU) at Rod's Fuel and delivered its waste there; Endgame and Stanton GW still have waste waiting.
// The old plan sent him on to Endgame (its 2 batches) and left Endgame's waste without a trip.
{
    var open = new Dictionary<string, int>
    {
        [Id("Nyx Gateway")] = 3, [Id("Gaslight")] = 2, [Id("Orbituary")] = 2, [Id("Starlight Service Station")] = 2, [Id("Rat's Nest")] = 2,
        [Id("Dudley & Daughters")] = 2, [Id("Stanton Gateway")] = 2, [Id("Megumi Refueling")] = 1, [Id("Endgame")] = 2,
    };
    var batchScu = open.Keys.ToDictionary(k => k, k => 146);
    batchScu[Id("Megumi Refueling")] = 125;
    var crew = new List<(string Name, int Capacity, QtDrive Drive)> { ("zzoega", 3, ts2) };
    var shipScu = new Dictionary<string, int> { ["zzoega"] = 741 };
    var starts = new List<WorkerStart> { new() { Worker = "zzoega", Loaded = 2, LoadedScu = 417, From = Id("Rod's Fuel 'N Supplies") } };
    var drops = new List<DropChunk>
    {
        new() { Station = Id("Endgame"), Batches = 1, Scu = 135, Parts = new() { 135 }, Keys = new() { "m|e" }, Cargo = "Waste" },
        new() { Station = Id("Stanton Gateway"), Batches = 1, Scu = 127, Parts = new() { 127 }, Keys = new() { "m|s" }, Cargo = "Waste" },
    };
    Show("Rod's Fuel run in progress, waste waiting at Endgame and Stanton GW", RoutePlanner.Plan(open, crew, starts, null, drops, batchScu, shipScu), TripPlanner.Plan(open, crew, starts, null, drops, batchScu, shipScu));
}

// The same at the start of a session (nothing carried): the waste trips and the full one-stop trips.
{
    var open = new Dictionary<string, int>
    {
        [Id("Nyx Gateway")] = 6, [Id("Patch City")] = 5, [Id("Gaslight")] = 4, [Id("Rod's Fuel 'N Supplies")] = 2, [Id("Stanton Gateway")] = 2, [Id("Starlight Service Station")] = 2,
        [Id("Endgame")] = 2, [Id("Dudley & Daughters")] = 2, [Id("Orbituary")] = 2, [Id("Rat's Nest")] = 2, [Id("Megumi Refueling")] = 1,
    };
    var batchScu = open.Keys.ToDictionary(k => k, k => 146);
    batchScu[Id("Rod's Fuel 'N Supplies")] = 208;
    var crew = new List<(string Name, int Capacity, QtDrive Drive)> { ("zzoega", 3, ts2) };
    var shipScu = new Dictionary<string, int> { ["zzoega"] = 741 };
    var drops = new List<DropChunk>
    {
        new() { Station = Id("Rod's Fuel 'N Supplies"), Batches = 1, Scu = 101, Parts = new() { 101 }, Keys = new() { "m|r" }, Cargo = "Waste" },
        new() { Station = Id("Endgame"), Batches = 1, Scu = 135, Parts = new() { 135 }, Keys = new() { "m|e" }, Cargo = "Waste" },
        new() { Station = Id("Stanton Gateway"), Batches = 1, Scu = 127, Parts = new() { 127 }, Keys = new() { "m|s" }, Cargo = "Waste" },
        new() { Station = Id("Nyx Gateway"), Batches = 1, Scu = 133, Parts = new() { 133 }, Keys = new() { "m|n" }, Cargo = "Waste" },
    };
    Show("Start of a session, one worker", RoutePlanner.Plan(open, crew, null, null, drops, batchScu, shipScu), TripPlanner.Plan(open, crew, null, null, drops, batchScu, shipScu));
}

// 23 Oct 14:54: zzoega is flying a run, at Checkmate with 2 batches on board (292 SCU); zzatvo is on his way home from Orbituary with a full ship. The old plan sent
// zzoega to Patch City for one batch (5 batches there, zzatvo's next trip took the other four).
{
    var open = new Dictionary<string, int>
    {
        [Id("Gaslight")] = 6, [Id("Patch City")] = 5, [Id("Nyx Gateway")] = 3, [Id("Endgame")] = 3, [Id("Dudley & Daughters")] = 2, [Id("Megumi Refueling")] = 2, [Id("Rod's Fuel 'N Supplies")] = 2,
    };
    var batchScu = new Dictionary<string, int>
    {
        [Id("Gaslight")] = 153, [Id("Patch City")] = 167, [Id("Nyx Gateway")] = 139, [Id("Endgame")] = 125, [Id("Dudley & Daughters")] = 188, [Id("Megumi Refueling")] = 146, [Id("Rod's Fuel 'N Supplies")] = 146,
    };
    var crew = new List<(string Name, int Capacity, QtDrive Drive)> { ("zzoega", 3, ts2), ("zzatvo", 3, ts2) };
    var shipScu = new Dictionary<string, int> { ["zzoega"] = 741, ["zzatvo"] = 741 };
    var starts = new List<WorkerStart>
    {
        new() { Worker = "zzoega", Loaded = 2, LoadedScu = 292, From = Id("Checkmate") },
        new() { Worker = "zzatvo", Loaded = 5, LoadedScu = 707, From = Id("Orbituary") },
    };
    Show("Checkmate run in progress, Patch City has 5 batches (more than a ship)", RoutePlanner.Plan(open, crew, starts, null, null, batchScu, shipScu), TripPlanner.Plan(open, crew, starts, null, null, batchScu, shipScu));
}

// Stage 1b (30 Oct): a contract from Gaslight and Patch City to Orbituary (2 batches each) beside the to-Ruin batches at the same stations and at Endgame, one
// worker at Ruin. The batches for Orbituary go in trips that end at Orbituary; the trip after one of those starts there.
{
    var open = new Dictionary<string, int>
    {
        [Id("Gaslight")] = 2, [Id("Patch City")] = 2, [Id("Endgame")] = 2,
        [RoutePlanner.GroupKey(Id("Gaslight"), Id("Orbituary"))] = 2, [RoutePlanner.GroupKey(Id("Patch City"), Id("Orbituary"))] = 2,
    };
    var batchScu = open.Keys.ToDictionary(k => k, k => 150);
    var crew = new List<(string Name, int Capacity, QtDrive Drive)> { ("zzoega", 3, ts2) };
    var shipScu = new Dictionary<string, int> { ["zzoega"] = 741 };
    Show("A contract to Orbituary beside the to-Ruin contracts", RoutePlanner.Plan(open, crew, null, null, null, batchScu, shipScu));   // the old planner knows no contracts to another station
}
