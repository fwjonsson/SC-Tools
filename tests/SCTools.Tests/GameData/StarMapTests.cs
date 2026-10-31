using SCTools.Server;

namespace SCTools.Tests.GameData;

/// <summary>The star systems of the Map tool: the places of Pyro, Stanton and Nyx, which place a quantum destination of the log is and a name the game says,
/// and where a player's quantum jumps put him (through a gateway into another system as well).</summary>
public class StarMapTests
{
    static QtEvent Picked(DateTime t, string destination, string from = "") => new() { Time = t, Kind = QtKind.Selected, Destination = destination, From = from };
    static QtEvent Arrived(DateTime t) => new() { Time = t, Kind = QtKind.Arrived };

    [Fact]
    public void Every_system_has_its_star_planets_and_gateways_and_the_keys_are_unique()
    {
        Assert.Equal(new[] { "pyro", "stanton", "nyx" }, StarMap.Systems.Select(s => s.Id));
        Assert.Equal(StarMap.Places.Length, StarMap.Places.Select(p => p.Key).Distinct().Count());
        foreach (var s in StarMap.Systems)
        {
            Assert.Single(StarMap.Of(s.Id), p => p.Kind == PlaceKind.Star);
            Assert.Contains(StarMap.Of(s.Id), p => p.Kind == PlaceKind.Planet);
            Assert.Contains(StarMap.Of(s.Id), p => p.Kind == PlaceKind.Gateway);
        }
        // the Hauling Planner's stations are places of the map, as far apart as PyroDistances has them (the system is nearly flat)
        foreach (var s in PyroDistances.Points) Assert.True(StarMap.OfPyroStation(s.Id) != null, s.Name);
        var (patch, gaslight) = (StarMap.OfPyroStation("70120")!, StarMap.OfPyroStation("69936")!);
        Assert.InRange(StarMap.Distance(patch, gaslight) - PyroDistances.Between("70120", "69936"), -1, 1);
        Assert.Equal(PyroDistances.Between(PyroDistances.Ruin, "69936"), StarMap.JumpGm(StarMap.Find("pyro/ruin-station")!, StarMap.Find("pyro/gaslight")!), 6);
    }

    [Fact]
    public void The_label_of_every_place_of_the_game_data_is_its_full_name()
    {
        // Frank, 30 Oct 2026: "Dudley & Daughters" and "Rod's Fuel 'N Supplies" written out on the maps, as the Hauling Planner has them again; the short labels
        // ("Dudley", "Rod's Fuel", "HUR-L1") went. The admin can still shorten one on Map places
        Assert.All(StarMap.Places, p => Assert.Equal(p.Name, p.Short));
        Assert.Equal("Dudley & Daughters", StarMap.Find("pyro/dudley-daughters")!.Short);
        Assert.Equal("Rod's Fuel 'N Supplies", StarMap.Find("pyro/rods-fuel-n-supplies")!.Short);
    }

    [Theory]
    [InlineData("rs_ext_pyro5_l2", "pyro/gaslight")]
    [InlineData("pyro3", "pyro/bloom")]
    [InlineData("stanton4", "stanton/microtech")]
    [InlineData("LOC_RR_S1_L2", "stanton/hur-l2-faithful-dream-station")]
    [InlineData("RR_P6_L5", "pyro/megumi-refueling")]
    [InlineData("RR_P6_LEO", "pyro/ruin-station")]
    [InlineData("RR_P3_LEO", "pyro/orbituary")]
    [InlineData("RR_P3_L1", "pyro/starlight-service-station")]
    [InlineData("RR_P2_L4", "pyro/checkmate")]
    [InlineData("RR_JP_PyroStanton", "pyro/stanton-gateway")]
    [InlineData("RR_JP_PyroNyx", "pyro/nyx-gateway")]
    [InlineData("RR_JP_NyxPyro", "nyx/pyro-gateway")]
    [InlineData("RR_P9_L1", null)]
    [InlineData("RR_CRU_L1", "stanton/cru-l1-ambitious-dream-station")]
    [InlineData("RR_HUR_L2", "stanton/hur-l2-faithful-dream-station")]
    [InlineData("RR_MIC_L1", "stanton/mic-l1-shallow-frontier-station")]
    [InlineData("RR_ARC_L5", "stanton/arc-l5-yellow-core-station")]
    [InlineData("RR_HUR_LEO", "stanton/everus-harbor")]
    [InlineData("RR_CRU_LEO", "stanton/seraphim-station")]
    [InlineData("RR_ARC_LEO", "stanton/baijini-point")]
    [InlineData("RR_MIC_LEO", "stanton/port-tressler")]
    [InlineData("RR_JP_StantonPyro", "stanton/pyro-gateway")]
    [InlineData("RR_JP_NyxCastra", null)]
    [InlineData("rs_ext_pyro-nyx_jp", "pyro/nyx-gateway")]
    [InlineData("rs_ext_nyx-pyro_jp1", "nyx/pyro-gateway")]
    [InlineData("levski_all-001", "nyx/levski")]
    [InlineData("PartyMemberMarker_123456", null)]
    [InlineData("AsteroidCluster_Pyro_004", null)]
    public void A_quantum_destination_of_the_log_is_a_place_of_the_map(string target, string? key) =>
        Assert.Equal(key, StarMap.OfQuantumTarget(target)?.Key);

    [Fact]
    public void The_games_code_for_a_pyro_station_is_read_as_an_area_and_as_a_start_name()
    {
        Assert.Equal(("pyro", "pyro/patch-city"), (Atlas.Default.OfLocation("RR_P3_L3").System, Atlas.Default.OfLocation("RR_P3_L3").Place?.Key));
        Assert.Equal("pyro/patch-city", StarMap.OfName("PYR3 L3").Place?.Key);
        Assert.Equal("stanton/mic-l1-shallow-frontier-station", StarMap.OfName("MIC L1").Place?.Key);
        Assert.Equal(("stanton", "stanton/everus-harbor"), (Atlas.Default.OfLocation("RR_HUR_LEO").System, Atlas.Default.OfLocation("RR_HUR_LEO").Place?.Key));
        Assert.Equal("pyro/stanton-gateway", StarMap.OfName("RR_JP_PyroStanton").Place?.Key);
        Assert.Equal((null, null), Atlas.Default.OfLocation("RR_P9_L1"));   // no such station: still on Map places
    }

    [Fact]
    public void A_name_the_game_says_is_a_place_or_a_system_and_a_name_of_two_systems_is_the_one_he_is_in()
    {
        Assert.Equal(("pyro", null), StarMap.OfName("Pyro System"));
        Assert.Equal("stanton/aberdeen", StarMap.OfName("Aberdeen").Place?.Key);
        Assert.Equal("pyro/ruin-station", StarMap.OfName("Ruin Station").Place?.Key);
        Assert.Equal("stanton/nyx-gateway", StarMap.OfName("Nyx Gateway", "stanton").Place?.Key);
        Assert.Equal("pyro/nyx-gateway", StarMap.OfName("Nyx Gateway", "pyro").Place?.Key);
        Assert.Equal((null, null), StarMap.OfName("Nyx Gateway"));   // in Pyro and in Stanton: not known
        // through Pyro's Nyx Gateway he is at Nyx's Pyro Gateway
        Assert.Equal("nyx/pyro-gateway", StarMap.OfName("Pyro Gateway", "pyro", StarMap.Find("pyro/nyx-gateway")).Place?.Key);
        Assert.Equal((null, null), StarMap.OfName("Somewhere"));
    }

    [Fact]
    public void A_player_goes_from_Ruin_through_the_Nyx_Gateway_to_Levski()
    {
        var t = new DateTime(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);
        var qt = new List<QtEvent> { Picked(t, "rs_ext_pyro-nyx_jp", "Ruin Station") };
        var w = MapService.Follow(qt);
        Assert.Equal(("pyro", "pyro/ruin-station", "pyro/nyx-gateway"), (w.System, w.From?.Key, w.To?.Key));

        qt.Add(Arrived(t.AddMinutes(3)));
        w = MapService.Follow(qt);
        Assert.Equal(("pyro", "pyro/nyx-gateway", t.AddMinutes(3)), (w.System, w.At?.Key, w.Since));
        Assert.Null(w.Picked);

        qt.Add(Picked(t.AddMinutes(8), "levski_all-001", "Pyro Gateway"));   // through the gateway: in Nyx
        w = MapService.Follow(qt);
        Assert.Equal(("nyx", "nyx/pyro-gateway", "nyx/levski"), (w.System, w.From?.Key, w.To?.Key));

        qt.Add(Arrived(t.AddMinutes(10)));
        w = MapService.Follow(qt);
        Assert.Equal(("nyx", "nyx/levski"), (w.System, w.At?.Key));

        // he opened his inventory at Levski: he is there, also with no quantum jump at all
        var levski = MapService.Follow(new List<QtEvent>(), new[] { new LocationSeen(t, "Nyx_Levski") });
        Assert.Equal(("nyx", "nyx/levski", t), (levski.System, levski.At?.Key, levski.Since));
        // after a jump to a party member (somewhere the map does not know), the inventory puts him at Ruin
        var ruin = MapService.Follow(new List<QtEvent> { Picked(t, "PartyMemberMarker_1", "Levski"), Arrived(t.AddMinutes(1)) },
            new[] { new LocationSeen(t.AddMinutes(5), "Pyro_RuinStation") });
        Assert.Equal(("pyro", "pyro/ruin-station"), (ruin.System, ruin.At?.Key));
        // picked a destination, then opened the inventory where the jump starts: still on his way
        var going = MapService.Follow(new List<QtEvent> { Picked(t, "rs_ext_pyro5_l2", "Ruin Station") }, new[] { new LocationSeen(t.AddSeconds(5), "Pyro_RuinStation") });
        Assert.Equal(("pyro/ruin-station", "pyro/gaslight"), (going.From?.Key, going.To?.Key));

        // in open space ("Pyro System") he is not at a place; a jump to a party member puts him nowhere the map knows
        var lost = MapService.Follow(new List<QtEvent> { Picked(t, "pyro3", "Pyro System") });
        Assert.Equal(("pyro", null, "pyro/bloom"), (lost.System, lost.From?.Key, lost.To?.Key));
        lost = MapService.Follow(new List<QtEvent> { Picked(t, "PartyMemberMarker_1", "Ruin Station"), Arrived(t.AddMinutes(1)) });
        Assert.Equal(("pyro", null), (lost.System, lost.At?.Key));
    }
}
