namespace SCTools.Tests.Planning;

/// <summary>The trip planner on situations from real sessions (see also tools/PlanTest, which prints the whole plans).</summary>
/// <summary>
/// The planner without a hub (RoutePlanner, the Route Planner (new) tab): every scenario of the old planner's tests, which it has to plan the same way (its
/// trips to Ruin start and end at Ruin as before), and the stage 1b scenarios with contracts to another station.
/// </summary>
public class RoutePlannerTests
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
            [Id("Nyx Gateway")] = 3, [Id("Gaslight")] = 2, [Id("Orbituary")] = 2, [Id("Starlight Service Station")] = 2, [Id("Rat's Nest")] = 2,
            [Id("Dudley & Daughters")] = 2, [Id("Stanton Gateway")] = 2, [Id("Megumi Refueling")] = 1, [Id("Endgame")] = 2,
        };
        var batchScu = open.Keys.ToDictionary(k => k, k => 146);
        batchScu[Id("Megumi Refueling")] = 125;
        var crew = new List<(string Name, int Capacity, QtDrive Drive)> { ("zzoega", 3, Ts2) };
        var shipScu = new Dictionary<string, int> { ["zzoega"] = 741 };
        var starts = new List<WorkerStart> { new() { Worker = "zzoega", Loaded = 2, LoadedScu = 417, From = Id("Rod's Fuel 'N Supplies") } };
        var drops = new List<DropChunk> { Waste("Endgame", 135), Waste("Stanton Gateway", 127) };
        return RoutePlanner.Plan(open, crew, starts, null, drops, batchScu, shipScu);
    }

    [Fact]
    public void The_trip_he_is_flying_leaves_a_station_with_a_lone_batch_for_later()
    {
        // 30 Oct 2026: zzoega at Orbituary with 3 batches on board (a C2: 5), and the plan sent him on to Endgame and Megumi for one batch each while
        // Rod's Fuel had 4 and Patch City 3. The biggest stops first: a station with one batch is left for later, where new missions can fill it up
        var open = new Dictionary<string, int> { [Id("Endgame")] = 1, [Id("Megumi Refueling")] = 1, [Id("Rod's Fuel 'N Supplies")] = 4, [Id("Patch City")] = 3 };
        var batchScu = open.Keys.ToDictionary(k => k, k => 123);
        var crew = new List<(string Name, int Capacity, QtDrive Drive)> { ("zzoega", 5, Ts2) };
        var shipScu = new Dictionary<string, int> { ["zzoega"] = 626 };
        var starts = new List<WorkerStart> { new() { Worker = "zzoega", Loaded = 3, LoadedScu = 369, From = Id("Orbituary") } };
        var trips = RoutePlanner.Plan(open, crew, starts, null, new List<DropChunk>(), batchScu, shipScu);
        // the trip he is flying (when the plan adds anything to his run at all: here it does not, he hands in what he has) never lands for a lone batch
        Assert.All(trips.Where(t => t.Special), t => Assert.DoesNotContain(t.Stops, s => s.Id == Id("Endgame") || s.Id == Id("Megumi Refueling")));
        Assert.Equal(9, trips.SelectMany(t => t.Stops).Sum(s => s.Batches));   // every batch is still planned, the lone ones in later trips
        Assert.All(trips.Where(t => t.Stops.Any(s => s.Id == Id("Endgame") || s.Id == Id("Megumi Refueling"))), t => Assert.False(t.Special));
    }

    [Fact]
    public void A_run_in_progress_does_not_take_the_pickup_where_waste_waits()
    {
        var now = RodsFuelRun().Single(t => t.Special);
        Assert.DoesNotContain(now.Stops, s => s.Id == Id("Endgame"));
        Assert.DoesNotContain(now.Stops, s => s.Id == Id("Stanton Gateway"));
    }

    [Fact]
    public void Every_load_from_Ruin_rides_along_on_a_pickup_at_its_station()
    {
        var trips = RodsFuelRun();
        foreach (var station in new[] { "Endgame", "Stanton Gateway" })
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
        Assert.Equal(3, planned[Id("Nyx Gateway")]);
        Assert.Equal(2, planned[Id("Endgame")]);
        Assert.Equal(1, planned[Id("Megumi Refueling")]);
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
        return RoutePlanner.Plan(open, crew, starts, null, null, batchScu, shipScu);
    }

    static List<int> PatchCityShares(List<PlannedTrip> trips) =>
        trips.SelectMany(t => t.Stops).Where(s => s.Id == Id("Patch City")).Select(s => s.Batches).OrderBy(x => x).ToList();

    [Fact]
    public void A_station_that_needs_two_trips_fills_one_and_the_leftover_waits()
    {
        Assert.Equal(new[] { 1, 4 }, PatchCityShares(PatchCityOnly(from: null)));   // a full ship of 4, not 3 + 2

        var trips = PatchCityOnly();   // his run has room for 2: that would be the leftover of a station the other one lands at anyway
        Assert.DoesNotContain(trips, t => t.Special);   // nothing is added to his run: he flies home with what he has
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
            [Id("Gaslight")] = 6, [Id("Patch City")] = 5, [Id("Nyx Gateway")] = 3, [Id("Endgame")] = 3, [Id("Dudley & Daughters")] = 2, [Id("Megumi Refueling")] = 2, [Id("Rod's Fuel 'N Supplies")] = 2,
        };
        var batchScu = new Dictionary<string, int>
        {
            [Id("Gaslight")] = 153, [Id("Patch City")] = 167, [Id("Nyx Gateway")] = 139, [Id("Endgame")] = 125, [Id("Dudley & Daughters")] = 188, [Id("Megumi Refueling")] = 146, [Id("Rod's Fuel 'N Supplies")] = 146,
        };
        var crew = new List<(string Name, int Capacity, QtDrive Drive)> { ("zzoega", 3, Ts2), ("zzatvo", 3, Ts2) };
        var shipScu = new Dictionary<string, int> { ["zzoega"] = 741, ["zzatvo"] = 741 };
        var starts = new List<WorkerStart>
        {
            new() { Worker = "zzoega", Loaded = 2, LoadedScu = 292, From = Id("Checkmate") },
            new() { Worker = "zzatvo", Loaded = 5, LoadedScu = 707, From = Id("Orbituary") },
        };
        return RoutePlanner.Plan(open, crew, starts, null, null, batchScu, shipScu);
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

    [Fact]
    public void A_kept_stop_gives_way_when_the_trip_gets_fuller_and_the_first_stop_stays()
    {
        // Frank, 30 Oct 2026: zzoega's trip was Stanton Gateway then Megumi (one batch each) when zzatvo picked up at Endgame. The stops after the one he is
        // flying to are soft since the evening of 30 Oct: Stanton Gateway stays first, and Megumi's single batch gives way to Gaslight's two, which fill his
        // ship; Megumi is planned later, by whoever. Every batch is still planned once
        var open = new Dictionary<string, int>
        {
            [Id("Stanton Gateway")] = 0, [Id("Megumi Refueling")] = 0,   // kept for zzoega: not among what is left to divide
            [Id("Endgame")] = 2, [Id("Gaslight")] = 2, [Id("Rat's Nest")] = 2, [Id("Dudley & Daughters")] = 1,
        };
        var batchScu = open.Keys.ToDictionary(k => k, k => 146);
        var crew = new List<(string Name, int Capacity, QtDrive Drive)> { ("zzoega", 3, Ts2), ("zzatvo", 3, Ts2) };
        var shipScu = new Dictionary<string, int> { ["zzoega"] = 741, ["zzatvo"] = 741 };
        var starts = new List<WorkerStart>
        {
            new() { Worker = "zzoega", FirstId = Id("Stanton Gateway"), FirstBatches = 1, Keep = new() { (Id("Megumi Refueling"), 1) } },
            new() { Worker = "zzatvo", Loaded = 2, LoadedScu = 292, From = Id("Endgame") },   // zzatvo picked up at Endgame: his run continues from there
        };
        var trips = RoutePlanner.Plan(open, crew, starts, null, null, batchScu, shipScu);
        var his = trips.First(t => t.Worker == "zzoega");
        Assert.True(his.Locked);
        Assert.Equal(Id("Stanton Gateway"), his.Stops[0].Id);
        Assert.True(his.Batches >= 3, "fuller than the two batches it had: " + his.Batches);   // the gateway's batch, a stop with two, and Megumi's when it still fits
        Assert.Contains(his.Stops, s => s.Batches == 2);
        Assert.Equal(7 + 2, trips.SelectMany(t => t.Stops).Sum(s => s.Batches));   // every batch is still planned: the kept two and the seven to divide
    }

    /// <summary>
    /// Frank's scenarios of 30 Oct 2026: his trip is A ×2, B ×1, C ×1 (the ship holds 4), he is flying to A, and a new mission comes in. The stop he is flying to
    /// stays; its batch count and the other stops follow the rule "as many batches per landing as possible": a kept stop gives way when that saves a landing
    /// or fills the ship better, and never otherwise.
    /// </summary>
    static List<PlannedTrip> FranksTrip(Dictionary<string, int> open)
    {
        var crew = new List<(string Name, int Capacity, QtDrive Drive)> { ("zzoega", 4, Ts2) };
        var starts = new List<WorkerStart> { new() { Worker = "zzoega", FirstId = A, FirstBatches = 2, Keep = new() { (B, 1), (C, 1) } } };
        return RoutePlanner.Plan(open, crew, starts);
    }
    static readonly string A = Id("Gaslight"), B = Id("Patch City"), C = Id("Checkmate"), D = Id("Rod's Fuel 'N Supplies"), E = Id("Endgame");
    static Dictionary<string, int> Stops(PlannedTrip t) => t.Stops.ToDictionary(s => s.Id, s => s.Batches);

    [Fact]
    public void A_new_batch_at_a_kept_stop_with_one_batch_makes_it_two_and_the_other_single_stop_goes()
    {
        var his = FranksTrip(new() { [B] = 1 }).First(t => t.Worker == "zzoega");
        Assert.Equal(A, his.Stops[0].Id);
        Assert.Equal(new Dictionary<string, int> { [A] = 2, [B] = 2 }, Stops(his));   // C's batch goes back to everybody, for a later trip
    }

    [Fact]
    public void A_new_batch_at_the_first_stop_is_taken_there_and_one_single_stop_stays()
    {
        var all = FranksTrip(new() { [A] = 1 });
        var his = all.First(t => t.Worker == "zzoega");
        Assert.Equal(A, his.Stops[0].Id);
        Assert.Equal(3, Stops(his)[A]);   // as many as possible at the stop he is flying to
        Assert.Equal(4, his.Batches);
        Assert.Equal(2, his.Stops.Count);   // and one of B and C, the closer one; the other is planned later
        Assert.Equal(5, all.Sum(t => t.Batches));
    }

    [Fact]
    public void A_new_batch_at_the_other_kept_stop_makes_the_trip_two_stops_of_two()
    {
        var his = FranksTrip(new() { [C] = 1 }).First(t => t.Worker == "zzoega");
        Assert.Equal(new Dictionary<string, int> { [A] = 2, [C] = 2 }, Stops(his));
    }

    [Fact]
    public void A_stop_elsewhere_that_gets_two_batches_replaces_both_single_stops()
    {
        var all = FranksTrip(new() { [D] = 2 });
        var his = all.First(t => t.Worker == "zzoega");
        Assert.Equal(A, his.Stops[0].Id);
        Assert.Equal(new Dictionary<string, int> { [A] = 2, [D] = 2 }, Stops(his));   // one landing less for the same cargo
        Assert.Equal(6, all.Sum(t => t.Batches));   // B's and C's batches are in later trips
    }

    [Fact]
    public void Kept_stops_stay_when_nothing_is_gained_by_a_change()
    {
        var his = FranksTrip(new() { [D] = 1, [E] = 1 }).First(t => t.Worker == "zzoega");   // other single stops about: no reason to swap
        Assert.Equal(new Dictionary<string, int> { [A] = 2, [B] = 1, [C] = 1 }, Stops(his));
    }

    /// <summary>The start of a session (nothing carried yet), one worker in a C2, waste waiting at four stations.</summary>
    static List<PlannedTrip> SessionStart()
    {
        var open = new Dictionary<string, int>
        {
            [Id("Nyx Gateway")] = 6, [Id("Patch City")] = 5, [Id("Gaslight")] = 4, [Id("Rod's Fuel 'N Supplies")] = 2, [Id("Stanton Gateway")] = 2, [Id("Starlight Service Station")] = 2,
            [Id("Endgame")] = 2, [Id("Dudley & Daughters")] = 2, [Id("Orbituary")] = 2, [Id("Rat's Nest")] = 2, [Id("Megumi Refueling")] = 1,
        };
        var batchScu = open.Keys.ToDictionary(k => k, k => 146);
        batchScu[Id("Rod's Fuel 'N Supplies")] = 208;
        var crew = new List<(string Name, int Capacity, QtDrive Drive)> { ("zzoega", 3, Ts2) };
        var shipScu = new Dictionary<string, int> { ["zzoega"] = 741 };
        var drops = new List<DropChunk> { Waste("Rod's Fuel 'N Supplies", 101), Waste("Endgame", 135), Waste("Stanton Gateway", 127), Waste("Nyx Gateway", 133) };
        return RoutePlanner.Plan(open, crew, null, null, drops, batchScu, shipScu);
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

    /// <summary>
    /// Stage 1b: a contract to another station (Gaslight and Patch City to Orbituary, 2 batches each) beside the to-Ruin contracts at the same stations, one
    /// worker at Ruin. A trip with batches for Orbituary ends there; the trip after it starts there; the batches for Ruin end at Ruin.
    /// </summary>
    static List<PlannedTrip> MixedDestinations()
    {
        var toOrbituary = RoutePlanner.GroupKey(Id("Gaslight"), Id("Orbituary"));
        var patchToOrbituary = RoutePlanner.GroupKey(Id("Patch City"), Id("Orbituary"));
        var open = new Dictionary<string, int> { [Id("Gaslight")] = 2, [Id("Patch City")] = 2, [toOrbituary] = 2, [patchToOrbituary] = 2, [Id("Endgame")] = 2 };
        var batchScu = open.Keys.ToDictionary(k => k, k => 150);
        var crew = new List<(string Name, int Capacity, QtDrive Drive)> { ("zzoega", 3, Ts2) };
        var shipScu = new Dictionary<string, int> { ["zzoega"] = 741 };
        return RoutePlanner.Plan(open, crew, null, null, null, batchScu, shipScu);
    }

    [Fact]
    public void A_trip_ends_where_its_cargo_goes_and_the_next_one_starts_there()
    {
        var trips = MixedDestinations();
        Assert.Equal(10, trips.Sum(t => t.Batches));   // every batch is planned
        Assert.All(trips, t => Assert.Equal(t.Picks.Select(p => p.To).Distinct().OrderBy(x => x), t.Ends.OrderBy(x => x)));   // the ends are where the batches go
        Assert.All(trips, t => Assert.Equal(t.Picks.Sum(p => p.Batches), t.Batches));
        Assert.Contains(trips, t => t.Ends.Contains(Id("Orbituary")));
        // the chain: every trip of the worker starts where the one before it ended (Ruin at first)
        var prev = PyroDistances.Ruin;
        foreach (var t in trips)
        {
            Assert.Equal(prev, t.Start);
            Assert.Equal(RoutePlanner.PathGm(t.Places()), t.Distance, 3);
            prev = t.End;
        }
        // a batch for Orbituary and one for Ruin picked up at the same station share the landing: the stops name the station once, with every group's batches
        Assert.All(trips, t => Assert.Equal(t.Stops.Count, t.Stops.Select(s => s.Id).Distinct().Count()));
        Assert.Equal(new[] { Id("Gaslight"), Id("Patch City") }, trips.SelectMany(t => t.Picks).Where(p => p.To == Id("Orbituary")).Select(p => p.Id).Distinct().OrderBy(x => x));
    }

    [Fact]
    public void A_parcel_from_another_system_is_planned_through_the_gateways()
    {
        // stage 2: a direct haul from Baijini Point (Stanton) to Ruin beside the to-Ruin batches at Gaslight and Patch City, one worker at Ruin
        const string baijini = "stanton/baijini-point";
        var open = new Dictionary<string, int> { [Id("Gaslight")] = 2, [Id("Patch City")] = 2, [RoutePlanner.GroupKey(baijini, PyroDistances.Ruin)] = 1 };
        var batchScu = open.Keys.ToDictionary(k => k, k => 150);
        var crew = new List<(string Name, int Capacity, QtDrive Drive)> { ("zzoega", 3, Ts2) };
        var shipScu = new Dictionary<string, int> { ["zzoega"] = 741 };
        var trips = RoutePlanner.Plan(open, crew, null, null, null, batchScu, shipScu);
        Assert.Equal(5, trips.Sum(t => t.Batches));
        var far = Assert.Single(trips, t => t.Picks.Any(p => p.Id == baijini));
        Assert.Contains(baijini, far.Places());
        Assert.Equal(PyroDistances.Ruin, far.End);
        var prev = PyroDistances.Ruin;
        foreach (var t in trips)
        {
            Assert.Equal(prev, t.Start);
            Assert.Equal(StarRoutes.PathGm(t.Places()), t.Distance, 3);   // the legs to and from the gateways
            Assert.Equal(StarRoutes.PathSeconds(Ts2, t.Places()), t.Seconds, 3);   // the crossings' time included
            prev = t.End;
        }
        Assert.True(far.Seconds > 2 * StarRoutes.GateSeconds);   // there and back through the gateways
        Assert.Contains("\u21e2", PlanText.RouteText(far));
        Assert.Contains("Baijini Point", PlanText.RouteText(far));
    }

    [Fact]
    public void A_locked_first_stop_is_kept_by_its_group()
    {
        var toOrbituary = RoutePlanner.GroupKey(Id("Gaslight"), Id("Orbituary"));
        Assert.Equal((Id("Gaslight"), Id("Orbituary")), (RoutePlanner.OriginOf(toOrbituary), RoutePlanner.DestinationOf(toOrbituary)));
        Assert.Equal((Id("Gaslight"), PyroDistances.Ruin), (RoutePlanner.OriginOf(Id("Gaslight")), RoutePlanner.DestinationOf(Id("Gaslight"))));
        var open = new Dictionary<string, int> { [Id("Endgame")] = 2 };   // the locked batch is not among the open ones (PlanService takes it out first)
        var crew = new List<(string Name, int Capacity, QtDrive Drive)> { ("zzoega", 3, Ts2) };
        var starts = new List<WorkerStart> { new() { Worker = "zzoega", FirstId = toOrbituary, FirstBatches = 1 } };
        var trips = RoutePlanner.Plan(open, crew, starts);
        var first = trips.First(t => t.Special);
        Assert.True(first.Locked);
        Assert.Equal(Id("Gaslight"), first.Stops[0].Id);
        Assert.Contains(Id("Orbituary"), first.Ends);
        Assert.Equal(3, trips.Sum(t => t.Batches));
    }

    [Fact]
    public void No_ship_is_planned_over_its_room()
    {
        foreach (var t in RodsFuelRun().Concat(SessionStart())) Assert.True(t.Fill <= 1.0001, "a trip is planned over the room of the ship: " + t.Fill);
    }
}
