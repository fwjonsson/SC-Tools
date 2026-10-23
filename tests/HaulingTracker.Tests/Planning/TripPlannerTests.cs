namespace HaulingTracker.Tests.Planning;

/// <summary>The trip planner on situations from real sessions (see also tools/PlanTest, which prints the whole plans).</summary>
public class TripPlannerTests
{
    static string Id(string name) => PyroDistances.Points.First(p => p.Name == name).Id;

    static readonly QtDrive Ts2 = QuantumDrives.Find("TS-2") ?? QuantumDrives.Assumed;

    static DropChunk Waste(string station, int scu) =>
        new() { Station = Id(station), Batches = 1, Scu = scu, Parts = new() { scu }, Keys = new() { "m|" + station }, Cargo = "Waste" };

    /// <summary>23 Oct 00:47: a run in progress at Rod's Fuel (417 SCU on board), waste waiting at Endgame and Stanton GW.</summary>
    static List<PlannedTrip> RodsFuelRun()
    {
        var open = new Dictionary<string, int>
        {
            [Id("Nyx GW")] = 3, [Id("Gaslight")] = 2, [Id("Orbituary")] = 2, [Id("Starlight")] = 2, [Id("Rat's Nest")] = 2,
            [Id("Dudley")] = 2, [Id("Stanton GW")] = 2, [Id("Megumi")] = 1, [Id("Endgame")] = 2,
        };
        var batchScu = open.Keys.ToDictionary(k => k, k => 146);
        batchScu[Id("Megumi")] = 125;
        var crew = new List<(string Name, int Capacity, QtDrive Drive)> { ("zzoega", 3, Ts2) };
        var shipScu = new Dictionary<string, int> { ["zzoega"] = 741 };
        var starts = new List<WorkerStart> { new() { Worker = "zzoega", Loaded = 2, LoadedScu = 417, From = Id("Rod's Fuel") } };
        var drops = new List<DropChunk> { Waste("Endgame", 135), Waste("Stanton GW", 127) };
        return TripPlanner.Plan(open, crew, starts, null, drops, batchScu, shipScu);
    }

    [Fact]
    public void A_run_in_progress_does_not_take_the_pickup_where_waste_waits()
    {
        var now = RodsFuelRun().Single(t => t.Special);
        Assert.DoesNotContain(now.Stops, s => s.Id == Id("Endgame"));
        Assert.DoesNotContain(now.Stops, s => s.Id == Id("Stanton GW"));
    }

    [Fact]
    public void Every_load_from_Ruin_rides_along_on_a_pickup_at_its_station()
    {
        var trips = RodsFuelRun();
        foreach (var station in new[] { "Endgame", "Stanton GW" })
        {
            var carrier = trips.Single(t => t.Drops.Any(d => d.Id == Id(station)));
            Assert.Equal(Id(station), carrier.Stops[0].Id);   // the station of the load is the first stop of that trip
        }
    }

    [Fact]
    public void Every_open_batch_is_planned_once()
    {
        var trips = RodsFuelRun();
        var planned = trips.SelectMany(t => t.Stops).GroupBy(s => s.Id).ToDictionary(g => g.Key, g => g.Sum(s => s.Batches));
        Assert.Equal(3, planned[Id("Nyx GW")]);
        Assert.Equal(2, planned[Id("Endgame")]);
        Assert.Equal(1, planned[Id("Megumi")]);
        Assert.Equal(18, planned.Values.Sum());
    }

    /// <summary>Patch City alone, 5 batches of 167 SCU: more than one ship holds. zzoega at Checkmate (or nowhere: both at Ruin), with this much on board.</summary>
    static List<PlannedTrip> PatchCityOnly(int loadedScu = 292, string? from = "Checkmate")
    {
        var open = new Dictionary<string, int> { [Id("Patch City")] = 5 };
        var batchScu = new Dictionary<string, int> { [Id("Patch City")] = 167 };
        var crew = new List<(string Name, int Capacity, QtDrive Drive)> { ("zzoega", 3, Ts2), ("zzatvo", 3, Ts2) };
        var shipScu = new Dictionary<string, int> { ["zzoega"] = 741, ["zzatvo"] = 741 };
        var starts = from == null ? null : new List<WorkerStart> { new() { Worker = "zzoega", Loaded = loadedScu > 0 ? 2 : 0, LoadedScu = loadedScu, From = Id(from) } };
        return TripPlanner.Plan(open, crew, starts, null, null, batchScu, shipScu);
    }

    static List<int> PatchCityShares(List<PlannedTrip> trips) =>
        trips.SelectMany(t => t.Stops).Where(s => s.Id == Id("Patch City")).Select(s => s.Batches).OrderBy(x => x).ToList();

    [Fact]
    public void A_station_that_needs_two_trips_fills_one_and_the_leftover_waits()
    {
        Assert.Equal(new[] { 1, 4 }, PatchCityShares(PatchCityOnly(from: null)));   // a full ship of 4, not 3 + 2

        var trips = PatchCityOnly();   // his run has room for 2: that would be the leftover of a station the other one lands at anyway
        Assert.DoesNotContain(trips.Single(t => t.Special).Stops, s => s.Id == Id("Patch City"));
        Assert.Equal(new[] { 1, 4 }, PatchCityShares(trips));
    }

    [Fact]
    public void The_trip_he_is_flying_fills_up()
    {
        var trips = PatchCityOnly(loadedScu: 0);   // at Checkmate with an empty ship (he delivered waste there): room for 4
        Assert.Equal(4, trips.Single(t => t.Special).Stops.Single(s => s.Id == Id("Patch City")).Batches);
        Assert.Equal(new[] { 1, 4 }, PatchCityShares(trips));
    }

    /// <summary>
    /// 23 Oct 14:54: zzoega is flying a run, at Checkmate with 2 batches on board (292 SCU, room for about 450 more); zzatvo is on his way home from Orbituary
    /// with a full ship. The plan sent zzoega to Patch City for one batch while zzatvo's next trip took the other four there.
    /// </summary>
    public static List<PlannedTrip> CheckmateRun()
    {
        var open = new Dictionary<string, int>
        {
            [Id("Gaslight")] = 6, [Id("Patch City")] = 5, [Id("Nyx GW")] = 3, [Id("Endgame")] = 3, [Id("Dudley")] = 2, [Id("Megumi")] = 2, [Id("Rod's Fuel")] = 2,
        };
        var batchScu = new Dictionary<string, int>
        {
            [Id("Gaslight")] = 153, [Id("Patch City")] = 167, [Id("Nyx GW")] = 139, [Id("Endgame")] = 125, [Id("Dudley")] = 188, [Id("Megumi")] = 146, [Id("Rod's Fuel")] = 146,
        };
        var crew = new List<(string Name, int Capacity, QtDrive Drive)> { ("zzoega", 3, Ts2), ("zzatvo", 3, Ts2) };
        var shipScu = new Dictionary<string, int> { ["zzoega"] = 741, ["zzatvo"] = 741 };
        var starts = new List<WorkerStart>
        {
            new() { Worker = "zzoega", Loaded = 2, LoadedScu = 292, From = Id("Checkmate") },
            new() { Worker = "zzatvo", Loaded = 5, LoadedScu = 707, From = Id("Orbituary") },
        };
        return TripPlanner.Plan(open, crew, starts, null, null, batchScu, shipScu);
    }

    [Fact]
    public void A_run_in_progress_fills_up_where_it_takes_the_station_and_never_lands_for_a_leftover()
    {
        var trips = CheckmateRun();
        var now = trips.Single(t => t.Special && t.Worker == "zzoega");
        Assert.DoesNotContain(now.Stops, s => s.Id == Id("Patch City"));
        var all = trips.SelectMany(t => t.Stops).GroupBy(s => s.Id).ToDictionary(g => g.Key, g => g.Sum(s => s.Batches));
        Assert.All(now.Stops, s => Assert.True(s.Batches * 2 >= all[s.Id], PyroDistances.NameOf(s.Id) + " is a leftover"));   // the bigger part of every station it lands at
        Assert.Equal(23, all.Values.Sum());   // every batch is still planned
    }

    /// <summary>The start of a session (nothing carried yet), one worker in a C2, waste waiting at four stations.</summary>
    static List<PlannedTrip> SessionStart()
    {
        var open = new Dictionary<string, int>
        {
            [Id("Nyx GW")] = 6, [Id("Patch City")] = 5, [Id("Gaslight")] = 4, [Id("Rod's Fuel")] = 2, [Id("Stanton GW")] = 2, [Id("Starlight")] = 2,
            [Id("Endgame")] = 2, [Id("Dudley")] = 2, [Id("Orbituary")] = 2, [Id("Rat's Nest")] = 2, [Id("Megumi")] = 1,
        };
        var batchScu = open.Keys.ToDictionary(k => k, k => 146);
        batchScu[Id("Rod's Fuel")] = 208;
        var crew = new List<(string Name, int Capacity, QtDrive Drive)> { ("zzoega", 3, Ts2) };
        var shipScu = new Dictionary<string, int> { ["zzoega"] = 741 };
        var drops = new List<DropChunk> { Waste("Rod's Fuel", 101), Waste("Endgame", 135), Waste("Stanton GW", 127), Waste("Nyx GW", 133) };
        return TripPlanner.Plan(open, crew, null, null, drops, batchScu, shipScu);
    }

    [Fact]
    public void At_the_start_every_waste_load_rides_along_and_no_trip_carries_two()
    {
        var trips = SessionStart();
        Assert.Equal(4, trips.Count(t => t.Drops.Count > 0));
        Assert.All(trips, t => Assert.True(t.Drops.Count <= 1));
        Assert.All(trips.Where(t => t.Drops.Count > 0), t => Assert.Equal(t.Drops[0].Id, t.Stops[0].Id));
    }

    [Fact]
    public void The_queue_starts_with_a_one_stop_trip_that_fills_the_ship()
    {
        var first = SessionStart().First();
        Assert.Single(first.Stops);
        Assert.True(first.Fill >= 0.75, "the first trip fills only " + first.Fill.ToString("P0"));
    }

    [Fact]
    public void No_ship_is_planned_over_its_room()
    {
        foreach (var t in RodsFuelRun().Concat(SessionStart())) Assert.True(t.Fill <= 1.0001, "a trip is planned over the room of the ship: " + t.Fill);
    }
}
