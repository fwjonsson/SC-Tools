using SCTools.GameData;

namespace SCTools.Tests.GameData;

/// <summary>What a station has, from UEX (StationServices): found for the stations of the map by name, whatever the two sources call them.</summary>
public class StationServicesTests
{
    [Fact]
    public void Ruin_Station_has_a_refinery_a_cargo_deck_and_a_gun_shop()
    {
        var ruin = StationServices.ByKey["pyro/ruin-station"];
        Assert.Equal("Ruin Station", ruin.Name);
        Assert.Contains("Refinery", ruin.Services);
        Assert.Contains("Cargo deck", ruin.Services);
        Assert.Contains("Guns", ruin.Shops);
        Assert.DoesNotContain(ruin.Shops, s => s.Contains("Ruin Station"));   // the shops' own names, without the place's
    }

    [Fact]
    public void The_names_of_the_two_sources_meet()
    {
        Assert.Equal("Checkmate Station", StationServices.ByKey["pyro/checkmate"].Name);                    // UEX says Station
        Assert.Equal("Green Imperial Housing Exchange", StationServices.ByKey["stanton/grim-hex"].Name);    // by UEX's nickname
        Assert.Equal("Nyx Gateway (Pyro)", StationServices.ByKey["pyro/nyx-gateway"].Name);                // the system in brackets
        Assert.Equal("Pyro Gateway (Nyx)", StationServices.ByKey["nyx/pyro-gateway"].Name);
        Assert.Equal("Area 18", StationServices.ByKey["stanton/area18"].Name);                             // a city
        Assert.Equal("Levski", StationServices.ByKey["nyx/levski"].Name);                                  // a city for UEX, a station for the map
        Assert.True(StationServices.ByKey.Count >= 45, StationServices.ByKey.Count + " places with services");
        Assert.False(StationServices.ByKey.ContainsKey("stanton/onyx-facility-s1a1"));   // UEX does not know the Onyx facilities: the name alone on the map
    }
}
