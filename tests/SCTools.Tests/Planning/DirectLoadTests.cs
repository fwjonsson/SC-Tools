namespace SCTools.Tests.Planning;

/// <summary>
/// 29 Oct 20:57 (Frank): at Ruin with a trip planned to Orbituary first, he accepted a direct contract from Ruin to Orbituary; the planner then sent him to Rod's
/// Fuel and home instead of flying the load to Orbituary, dropping it and picking up there. A load that is aboard already goes with the trip to its station.
/// </summary>
public class DirectLoadTests
{
    static string Id(string name) => PyroDistances.Points.First(p => p.Name == name).Id;
    static readonly QtDrive Ts2 = QuantumDrives.Find("TS-2") ?? QuantumDrives.Assumed;

    static DropChunk Direct(string station, int scu, params string[] allowed) =>
        new() { Station = Id(station), Batches = 1, Scu = scu, Parts = new() { scu }, Keys = new() { "d|" + station }, Cargo = "Cargo", Allowed = allowed };

    static Dictionary<string, int> Open() => new()
    {
        [Id("Orbituary")] = 2, [Id("Rod's Fuel 'N Supplies")] = 2, [Id("Gaslight")] = 4, [Id("Patch City")] = 3, [Id("Nyx Gateway")] = 2,
    };

    [Fact]
    public void A_direct_load_the_ship_holds_rides_to_its_station_on_the_first_trip_alone()
    {
        // a C2 (696 SCU; the plan fills it to 90 %: 626): a direct load of 650 SCU is aboard already, it fits the ship in full
        var open = Open();
        var batchScu = open.Keys.ToDictionary(k => k, k => 146);
        var crew = new List<(string Name, int Capacity, QtDrive Drive)> { ("zzoega", 3, Ts2) };
        var trips = TripPlanner.Plan(open, crew, null, null, new[] { Direct("Orbituary", 650) }, batchScu, new Dictionary<string, int> { ["zzoega"] = 626 },
            new Dictionary<string, int> { ["zzoega"] = 696 });
        var carrier = Assert.Single(trips, t => t.Drops.Count > 0);
        Assert.Equal(Id("Orbituary"), carrier.Drops[0].Id);
        Assert.Equal(Id("Orbituary"), carrier.Stops[0].Id);   // dropped there, then the pickups there
        var without = TripPlanner.Plan(open, crew, null, null, new[] { Direct("Orbituary", 650) }, batchScu, new Dictionary<string, int> { ["zzoega"] = 626 });
        Assert.DoesNotContain(without, t => t.Drops.Count > 0);   // (what it did before: the load was over the plan's 90 %, so it waited)
    }

    [Fact]
    public void A_direct_load_only_one_worker_has_goes_with_him_even_when_the_pickups_there_were_another_s()
    {
        var open = Open();
        var batchScu = open.Keys.ToDictionary(k => k, k => 146);
        var crew = new List<(string Name, int Capacity, QtDrive Drive)> { ("zzoega", 3, Ts2), ("zzatvo", 3, Ts2) };
        var shipScu = new Dictionary<string, int> { ["zzoega"] = 626, ["zzatvo"] = 626 };
        var trips = TripPlanner.Plan(open, crew, null, null, new[] { Direct("Orbituary", 300, "zzoega") }, batchScu, shipScu);
        var carrier = Assert.Single(trips, t => t.Drops.Count > 0);
        Assert.Equal("zzoega", carrier.Worker);
        Assert.Equal(Id("Orbituary"), carrier.Stops.Count > 0 ? carrier.Stops[0].Id : carrier.Drops[0].Id);
    }
}
