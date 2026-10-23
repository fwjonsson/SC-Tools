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

    [Fact]
    public void No_ship_is_planned_over_its_room()
    {
        foreach (var t in RodsFuelRun()) Assert.True(t.Fill <= 1.0001, "a trip is planned over the room of the ship: " + t.Fill);
    }
}
