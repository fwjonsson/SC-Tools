namespace SCTools.Tests.GameData;

/// <summary>The places of the map with the admin's changes: what a text of the log is (a place, or not a place to show; * for every one that starts so), his own
/// names (the game's still find the place), and the places he added.</summary>
public class AtlasTests
{
    [Fact]
    public void The_admins_word_comes_first_and_a_star_is_every_text_that_starts_so()
    {
        var atlas = new Atlas(StarMap.Places, new[]
        {
            new PlaceAlias("AsteroidCluster_Pyro_*", "pyro/bloom"),
            new PlaceAlias("AsteroidCluster_Pyro_009", "pyro/monox"),   // exact beats a beginning
            new PlaceAlias("RegionB_*", null),
            new PlaceAlias("pyro3", "pyro/terminus"),                   // the admin over the game data
            new PlaceAlias("Old Spot", "pyro/gone"),                    // a place that is not there any more: not said
        });
        Assert.Equal("pyro/bloom", atlas.OfQuantumTarget("AsteroidCluster_Pyro_004")?.Key);
        Assert.Equal("pyro/monox", atlas.OfQuantumTarget("asteroidcluster_pyro_009")?.Key);
        Assert.Equal((true, (Place?)null), atlas.AliasOf("RegionB_7f3_encounter-003"));
        Assert.Null(atlas.OfQuantumTarget("RegionB_7f3_encounter-003"));
        Assert.Equal("pyro/terminus", atlas.OfQuantumTarget("pyro3")?.Key);
        Assert.Equal((false, (Place?)null), atlas.AliasOf("Old Spot"));
        Assert.Equal((true, (Place?)null), atlas.AliasOf("PartyMemberMarker_123"));   // a party member is never a place
        Assert.Equal((false, (Place?)null), Atlas.Default.AliasOf("AsteroidCluster_Pyro_004"));
    }

    [Theory]
    [InlineData("Nyx_Levski", "nyx", "nyx/levski")]                       // zzoii's log, 24 Oct
    [InlineData("Stanton1_Lorville", "stanton", "stanton/lorville")]
    [InlineData("Stanton4_NewBabbage", "stanton", "stanton/new-babbage")]  // a name without spaces
    [InlineData("Pyro_RuinStation", "pyro", "pyro/ruin-station")]
    [InlineData("Stanton2_Unknown_Outpost", "stanton", null)]               // the system alone
    [InlineData("Somewhere", null, null)]
    public void An_inventory_location_is_a_place(string id, string? system, string? key)
    {
        var (sys, place) = Atlas.Default.OfLocation(id);
        Assert.Equal((system, key), (sys, place?.Key));
    }

    [Fact]
    public void A_renamed_place_is_still_found_by_the_games_name_and_an_added_one_by_its_own()
    {
        var bloom = StarMap.Find("pyro/bloom")!;
        var added = new Place("pyro", "asteroid-base", "Asteroid Base", "AB", PlaceKind.Station, "bloom", bloom.X, bloom.Y);
        var atlas = new Atlas(StarMap.Places.Select(p => p.Key == bloom.Key ? p with { Name = "Bloom (home)", Short = "Home" } : p).Append(added),
            new[] { new PlaceAlias("Spot 7", "pyro/asteroid-base") });
        Assert.Equal("Bloom (home)", atlas.OfQuantumTarget("pyro3")?.Name);
        Assert.Equal("pyro/bloom", atlas.OfName("Bloom").Place?.Key);
        Assert.Equal("pyro/bloom", atlas.OfName("Bloom (home)").Place?.Key);
        Assert.Equal("pyro/asteroid-base", atlas.OfName("Asteroid Base").Place?.Key);
        Assert.Equal("pyro/asteroid-base", atlas.OfName("Spot 7").Place?.Key);
        Assert.Null(Atlas.Default.Find("pyro/asteroid-base"));
        Assert.Equal("rods-fuel-n-supplies", StarMap.Slug("Rod's Fuel 'N Supplies"));
    }
}
