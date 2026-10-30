namespace SCTools.Tests.GameData;

/// <summary>
/// The way between any two places of the map for the Route Planner (StarRoutes, stage 2 of docs/planner-any-hauling.md): the Pyro legs as the distance table has
/// them, a route to another system through its gateways with the crossing's time, and the names and keys of the planner's ids.
/// </summary>
public class StarRoutesTests
{
    static string Id(string name) => PyroDistances.Points.First(p => p.Name == name).Id;
    static readonly QtDrive Ts2 = QuantumDrives.Find("TS-2") ?? QuantumDrives.Assumed;

    [Fact]
    public void A_leg_between_two_Pyro_stations_is_the_distance_tables()
    {
        var leg = Assert.Single(StarRoutes.Legs(Id("Gaslight"), Id("Patch City")));
        Assert.Equal((Id("Gaslight"), Id("Patch City"), PyroDistances.Between(Id("Gaslight"), Id("Patch City")), false), (leg.From, leg.To, leg.Gm, leg.Gate));
        Assert.Equal(PyroDistances.Between(PyroDistances.Ruin, Id("Endgame")), StarRoutes.Gm(PyroDistances.Ruin, Id("Endgame")));
        Assert.Equal(Ts2.Seconds(PyroDistances.Between(PyroDistances.Ruin, Id("Endgame"))), StarRoutes.Seconds(Ts2, PyroDistances.Ruin, Id("Endgame")));
        Assert.Empty(StarRoutes.Legs(Id("Gaslight"), Id("Gaslight")));
        Assert.False(StarRoutes.Crosses(PyroDistances.Ruin, Id("Gaslight")));
    }

    [Fact]
    public void A_route_to_another_system_goes_through_the_gateways_and_the_crossing_takes_its_time()
    {
        var legs = StarRoutes.Legs(PyroDistances.Ruin, "stanton/baijini-point");
        Assert.Equal(3, legs.Count);
        Assert.Equal((PyroDistances.Ruin, Id("Stanton Gateway"), false), (legs[0].From, legs[0].To, legs[0].Gate));
        Assert.Equal(PyroDistances.Between(PyroDistances.Ruin, Id("Stanton Gateway")), legs[0].Gm);   // the table's leg to the gateway
        Assert.Equal((Id("Stanton Gateway"), "stanton/pyro-gateway", 0.0, true), (legs[1].From, legs[1].To, legs[1].Gm, legs[1].Gate));
        Assert.Equal(("stanton/pyro-gateway", "stanton/baijini-point", false), (legs[2].From, legs[2].To, legs[2].Gate));
        Assert.Equal(StarMap.Distance(StarMap.Find("stanton/pyro-gateway")!, StarMap.Find("stanton/baijini-point")!), legs[2].Gm, 6);
        Assert.Equal(legs[0].Gm + legs[2].Gm, StarRoutes.Gm(PyroDistances.Ruin, "stanton/baijini-point"), 6);
        Assert.Equal(Ts2.Seconds(legs[0].Gm) + StarRoutes.GateSeconds + Ts2.Seconds(legs[2].Gm), StarRoutes.Seconds(Ts2, PyroDistances.Ruin, "stanton/baijini-point"), 6);
        Assert.Equal(204, StarRoutes.GateSeconds);   // 3.4 minutes (Frank, 30 Oct 2026)
        Assert.True(StarRoutes.Crosses(PyroDistances.Ruin, "stanton/baijini-point"));
        // from the gateway itself there is no leg to it; to a gateway of the other system none from it
        Assert.Equal(2, StarRoutes.Legs(Id("Stanton Gateway"), "stanton/baijini-point").Count);
        Assert.Equal(2, StarRoutes.Legs(PyroDistances.Ruin, "stanton/pyro-gateway").Count);
        Assert.Equal(("stanton/nyx-gateway", "nyx/stanton-gateway"), StarRoutes.GatesBetween("stanton", "nyx"));
        Assert.Equal((Id("Nyx Gateway"), "nyx/pyro-gateway"), StarRoutes.GatesBetween("pyro", "nyx"));
        // a route of places in order, the crossing in its time
        var places = new[] { PyroDistances.Ruin, Id("Gaslight"), "stanton/baijini-point", "stanton/port-tressler" };
        Assert.Equal(StarRoutes.Gm(places[0], places[1]) + StarRoutes.Gm(places[1], places[2]) + StarRoutes.Gm(places[2], places[3]), StarRoutes.PathGm(places), 6);
        Assert.Equal(StarRoutes.Seconds(Ts2, places[0], places[1]) + StarRoutes.Seconds(Ts2, places[1], places[2]) + StarRoutes.Seconds(Ts2, places[2], places[3]), StarRoutes.PathSeconds(Ts2, places), 6);
    }

    [Fact]
    public void The_planners_ids_are_the_Pyro_stations_ids_and_the_other_places_keys()
    {
        Assert.Equal("Rod's Fuel 'N Supplies", StarRoutes.NameOf(Id("Rod's Fuel 'N Supplies")));   // the planner's own name for a station
        Assert.Equal("Ruin Station", StarRoutes.NameOf(PyroDistances.Ruin));
        Assert.Equal("Baijini Point", StarRoutes.NameOf("stanton/baijini-point"));
        Assert.Equal("nowhere", StarRoutes.NameOf("nowhere"));   // not a place: the id as it is
        Assert.Equal(Id("Gaslight"), StarRoutes.IdOf(StarMap.Find("pyro/gaslight")!));
        Assert.Equal("stanton/baijini-point", StarRoutes.IdOf(StarMap.Find("stanton/baijini-point")!));
        Assert.Equal("pyro/ruin-station", StarRoutes.KeyOf(PyroDistances.Ruin));
        Assert.Equal(("pyro", "stanton", null), (StarRoutes.SystemOf(Id("Gaslight")), StarRoutes.SystemOf("stanton/baijini-point"), StarRoutes.SystemOf("nowhere")));
        Assert.True(StarRoutes.Known(PyroDistances.Ruin));
        Assert.False(StarRoutes.Known("nowhere"));
        var leg = Assert.Single(StarRoutes.Legs(PyroDistances.Ruin, "nowhere"));   // a place the map does not know: a leg of nothing, so a table never fails on it
        Assert.Equal((0.0, false), (leg.Gm, leg.Gate));
    }

    [Fact]
    public void A_place_source_that_has_no_places_any_more_reads_the_game_data()
    {
        // a test server that is going away answers no places (its database is closed): the routes read the game data's, not nothing
        var was = StarRoutes.AtlasSource;
        try
        {
            StarRoutes.AtlasSource = () => null!;
            Assert.NotNull(StarRoutes.PlaceOf(Id("Gaslight")));
        }
        finally
        {
            StarRoutes.AtlasSource = was;
        }
    }
}
