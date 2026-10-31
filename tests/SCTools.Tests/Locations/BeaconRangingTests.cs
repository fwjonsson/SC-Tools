using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SCTools.Server;
using SCTools.Tests.ServerPages;
using SCTools.Tests.Support;
using static SCTools.Locations.LocationTracker;

namespace SCTools.Tests.Locations;

/// <summary>
/// A mission's quantum beacon placed by the fuel estimate of the route to it (#403): the estimate over the ship's estimate per Gm is how far the beacon is
/// from where the jump starts, and the hidden spot of the game data that far away is where he flies to. Checked on Frank's two bounties of 31 Oct 2026
/// (zzoega's log, the Perseus from Levski): the Burt Gearhart one at QV Breaker Station BRK-304, the Hideaki Antal one in the Glaciem ring.
/// </summary>
public class BeaconRangingTests
{
    static readonly DateTime T = new(2026, 10, 31, 3, 44, 3, DateTimeKind.Utc);
    const string Burt = "MISSION_QT_Quantum_Beacon_LongRange_865528657557";
    const string Hideaki = "MISSION_QT_Quantum_Beacon_LongRange_864272617968";
    const string Perseus = "RSI_Perseus";
    const double PerseusBurn = 120.33;   // per km, as the game shows his crafted TS-2

    static QtEvent Picked(DateTime t, string destination, string from, double? fuel, string ship = Perseus) =>
        new() { Time = t, Kind = QtKind.Selected, Destination = destination, From = from, FuelEstimate = fuel, Ship = ship };
    static QtEvent Arrived(DateTime t, string ship = Perseus) => new() { Time = t, Kind = QtKind.Arrived, Ship = ship };

    static MissionPoint Bounty(string beacon, string text) => new(beacon, "22226", "Reclusive Bounty", text, "nyx", null, T.AddHours(-1), null);

    static readonly MissionPoint[] Bounties = { Bounty(Burt, "Go to QV Breaker Station"), Bounty(Hideaki, "Go to Glaciem Ring Cluster") };

    static double? Fleet(QtEvent e) => e.Ship == Perseus ? PerseusBurn * BeaconRanging.EstimatePerKmGm : null;

    static Place Glaciem => Atlas.Default.Spots.OrderBy(s => Math.Abs(s.X - 12.920) + Math.Abs(s.Y + 7.619)).First();

    [Fact]
    public void The_log_gives_every_pick_the_fuel_estimate_of_its_route()
    {
        // zzoega's log at 03:44:03: the route is worked out before the pick and again after it, with the same estimate
        var w = new GameLogWriter().At(T).Login("zzoega")
            .RouteStart("Levski", Hideaki).RouteFuel(Hideaki, 738770.25, Perseus, "910650678563")
            .QuantumSelected(Hideaki, Perseus, "910650678563").RouteStart("Levski", Hideaki).RouteFuel(Hideaki, 738770.25, Perseus, "910650678563");
        Assert.All(w.Lines, l => Assert.True(new LineFilter().Keep(l), l));   // the trackers send the estimate lines already
        var pick = Assert.Single(w.Read().Qt);
        Assert.Equal((Hideaki, "Levski", 738770.25, Perseus), (pick.Destination, pick.From, pick.FuelEstimate, pick.Ship));
    }

    [Fact]
    public void A_beacon_is_the_hidden_spot_as_far_from_the_start_as_the_fuel_estimate_says()
    {
        // the Hideaki Antal bounty: 738,770 from Levski with the Perseus is 22.89 Gm, a mission spot of the Glaciem ring
        var w = Follow(new List<QtEvent> { Picked(T, Hideaki, "Levski", 738770.25) }, Atlas.Default, missions: Bounties, fuelPerGm: Fleet);
        Assert.Equal(("nyx/levski", Glaciem.Key, "Go to Glaciem Ring Cluster"), (w.From?.Key, w.To?.Key, w.ToMission?.Text));
        Assert.True(w.To!.Hidden);
        Assert.Equal("Glaciem Ring Mission Location", w.To.Name);

        // the Burt Gearhart bounty: 1,450,849 from Levski is 44.96 Gm, the hidden QV Breaker Station BRK-304 (where his debug overlay had him, to a Mm)
        w = Follow(new List<QtEvent> { Picked(T, Burt, "Levski", 1450849.375) }, Atlas.Default, missions: Bounties, fuelPerGm: Fleet);
        Assert.Equal("nyx/qv-breaker-station-brk-304", w.To?.Key);
        Assert.Equal("nyx/qv-breaker-station-brk-304", w.Ranged?[Burt].Key);   // kept for the Map tool's objectives

        // there: at the spot, at the mission; the game then names only the system as the start of his next route, and he is still there
        w = Follow(new List<QtEvent> { Picked(T, Hideaki, "Levski", 738770.25), Arrived(T.AddMinutes(3)), Picked(T.AddMinutes(4), Burt, "Nyx", null) },
            Atlas.Default, missions: Bounties, fuelPerGm: Fleet);
        Assert.Equal((Glaciem.Key, Glaciem.Key), (w.At?.Key, w.From?.Key));
        w = Follow(new List<QtEvent> { Picked(T, Hideaki, "Levski", 738770.25), Arrived(T.AddMinutes(3)) }, Atlas.Default, missions: Bounties, fuelPerGm: Fleet);
        Assert.Equal((Glaciem.Key, "Go to Glaciem Ring Cluster", false), (w.At?.Key, w.AtMission?.Text, w.Jumped));
        var now = LocationTracker.Now(w, QuantumDrives.Find("TS-2 S3")!, T.AddMinutes(5));
        Assert.Equal((PlayerState.At, "the mission: Go to Glaciem Ring Cluster"), (now.State, now.AtName));
    }

    [Fact]
    public void A_ships_estimate_per_Gm_is_learnt_from_its_jumps_between_known_places()
    {
        // zzoega's C2 from Ruin to the Nyx Gateway (29 Oct 2026): 2,995,934 over the leg's distance; then a beacon in Nyx with no fleet to ask
        var c2 = "CRUS_Starlifter_C2";
        var ruin = StarMap.Find("pyro/ruin-station")!;
        var perGm = 2995934.0 / StarMap.JumpGm(ruin, StarMap.Find("pyro/nyx-gateway")!);
        var brk304 = Atlas.Default.Find("nyx/qv-breaker-station-brk-304")!;
        var estimate = perGm * StarMap.Distance(StarMap.Find("nyx/levski")!, brk304);
        var qt = new List<QtEvent>
        {
            Picked(T.AddHours(-2), "rs_ext_pyro-nyx_jp", "Ruin Station", 2995934, c2), Arrived(T.AddHours(-1.9), c2),
            Picked(T, Burt, "Levski", estimate, c2),
        };
        Assert.Equal(brk304.Key, Follow(qt, Atlas.Default, missions: Bounties).To?.Key);
        // another ship learns nothing from the C2's jump
        qt[^1] = Picked(T, Burt, "Levski", estimate);
        Assert.Null(Follow(qt, Atlas.Default, missions: Bounties).To);
    }

    [Fact]
    public void Without_the_ships_fuel_or_a_spot_at_that_distance_the_beacon_stays_unplaced()
    {
        var qt = new List<QtEvent> { Picked(T, Hideaki, "Levski", 738770.25) };
        Assert.Null(Follow(qt, Atlas.Default, missions: Bounties).To);   // no fleet, nothing learnt
        var w = Follow(new List<QtEvent> { Picked(T, Hideaki, "Levski", 600000) }, Atlas.Default, missions: Bounties, fuelPerGm: Fleet);   // 18.6 Gm: no spot there
        Assert.Equal(((Place?)null, "Go to Glaciem Ring Cluster"), (w.To, w.ToMission?.Text));
        // a destination that is not a mission's beacon is never placed so (it goes to the admin's Map places instead)
        Assert.Null(Follow(new List<QtEvent> { Picked(T, "social_001_keeger_segment_rckcrk_095", "Levski", 738770.25) }, Atlas.Default, fuelPerGm: Fleet).To);
        // the hidden spots are not found by a name or a destination of the log, and the map has none of them unless asked
        Assert.Null(Atlas.Default.OfName("Glaciem Ring Mission Location", "nyx").Place);
        Assert.DoesNotContain(Atlas.Default.Places, p => p.Hidden);
    }

    [Fact]
    public async Task The_map_has_the_spot_where_a_player_flies_and_draws_nothing_else()
    {
        using var f = new ServerFactory();
        var fleet = f.Services.GetRequiredService<FleetService>();
        Assert.Null(fleet.Add("zzoega", "RSI Perseus", "TS-2 S3", "857.7", DateTime.UtcNow, "876.7"));
        Assert.Equal(120.33, fleet.FuelPerKmOf("zzoega", Perseus)!.Value, 2);
        Assert.Null(fleet.FuelPerKmOf("zzoega", "CRUS_Starlifter_C2"));   // not in his fleet

        var m = GameLogWriter.MissionId(9);
        var start = DateTime.UtcNow.AddSeconds(-20);
        f.Upload("zzoega", new GameLogWriter().At(start.AddMinutes(-5)).Login("zzoega")
            .MissionMarker(m, GameLogWriter.BountyContract, "obj-1", "22226").ObjectivePopup(m, "obj-1", "Go to Glaciem Ring Cluster").BeaconPaired("22226", Hideaki)
            .At(start).RouteStart("Levski", Hideaki).RouteFuel(Hideaki, 738770.25, Perseus, "910650678563")
            .QuantumSelected(Hideaki, Perseus, "910650678563").RouteStart("Levski", Hideaki).RouteFuel(Hideaki, 738770.25, Perseus, "910650678563").Lines.ToList());
        var admin = await f.Admin();
        var map = JsonDocument.Parse(await admin.GetStringAsync("/map/map.json")).RootElement;
        var nyx = map.GetProperty("systems").EnumerateArray().Single(s => s.GetProperty("id").GetString() == "nyx");
        var hidden = nyx.GetProperty("places").EnumerateArray().Where(p => p.GetProperty("hidden").GetBoolean()).ToList();
        Assert.Equal(Glaciem.Key, Assert.Single(hidden).GetProperty("key").GetString());   // only the spot he flies to
        var player = map.GetProperty("players").EnumerateArray().Single(p => p.GetProperty("name").GetString() == "zzoega");
        var where = player.GetProperty("state").GetString() == "jump" ? player.GetProperty("to").GetString() : player.GetProperty("at").GetString();
        Assert.Equal(Glaciem.Key, where);
        // the objective he set the route to is at the spot too (its gold diamond), not "the game does not say where"
        var objective = Assert.Single(map.GetProperty("objectives").EnumerateArray());
        Assert.Equal((Glaciem.Key, "Go to Glaciem Ring Cluster"), (objective.GetProperty("place").GetString(), objective.GetProperty("where").GetString()));
    }

    [Fact]
    public async Task An_objective_is_at_the_spot_once_anybody_set_a_route_to_it_or_to_another_in_its_cluster()
    {
        using var f = new ServerFactory();
        f.Services.GetRequiredService<FleetService>().Add("zzoega", "RSI Perseus", "TS-2 S3", "857.7", DateTime.UtcNow, "876.7");
        var m = GameLogWriter.MissionId(9);
        var other = GameLogWriter.MissionId(10);
        var start = DateTime.UtcNow.AddMinutes(-30);
        // zzoega's log of 31 Oct 2026: the bounty's first objective in zone 906735902202, its beacon, a route to it set at Levski (no jump yet);
        // then a second objective of the mission in the same zone ("Return to Glaciem Ring Cluster"), and another bounty's objective somewhere else
        var w = new GameLogWriter().At(start).Login("zzoega")
            .MissionMarker(m, GameLogWriter.BountyContract, "obj-1", "22226", "906735902202").ObjectivePopup(m, "obj-1", "Go to Glaciem Ring Cluster").BeaconPaired("22226", Hideaki)
            .MissionMarker(other, GameLogWriter.BountyContract, "obj-9", "3846", "906735904623").ObjectivePopup(other, "obj-9", "Go to QV Breaker Station")
            .Minutes(1).RouteStart("Levski", Hideaki).RouteFuel(Hideaki, 738770.25, Perseus, "910650678563")
            .QuantumSelected(Hideaki, Perseus, "910650678563").RouteStart("Levski", Hideaki).RouteFuel(Hideaki, 738770.25, Perseus, "910650678563")
            .Minutes(5).QuantumArrived(Perseus, "910650678563").Seconds(5).Objective(m, "obj-1", "COMPLETED")
            .Minutes(10).MissionMarker(m, GameLogWriter.BountyContract, "obj-2", "22227", "906735902202").ObjectivePopup(m, "obj-2", "Return to Glaciem Ring Cluster");
        f.Upload("zzoega", w.Lines.ToList());
        var admin = await f.Admin();
        var map = JsonDocument.Parse(await admin.GetStringAsync("/map/map.json")).RootElement;
        var objectives = map.GetProperty("objectives").EnumerateArray().ToDictionary(o => o.GetProperty("where").GetString()!);
        Assert.Equal(Glaciem.Key, objectives["Return to Glaciem Ring Cluster"].GetProperty("place").GetString());   // the same cluster: at the spot
        Assert.False(objectives["Go to QV Breaker Station"].TryGetProperty("place", out _));   // nobody set a route to it: in the list only
        Assert.DoesNotContain("Go to Glaciem Ring Cluster", objectives.Keys);   // done
        var nyx = map.GetProperty("systems").EnumerateArray().Single(s => s.GetProperty("id").GetString() == "nyx");
        Assert.Contains(nyx.GetProperty("places").EnumerateArray(), p => p.GetProperty("key").GetString() == Glaciem.Key && p.GetProperty("hidden").GetBoolean());
    }
}
