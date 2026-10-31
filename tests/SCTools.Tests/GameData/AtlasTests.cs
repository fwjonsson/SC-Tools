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
    [InlineData("Stanton2_Unknown_Outpost", "stanton", "stanton/crusader")]   // a place the map does not have: on the planet its code names (until 31 Oct 2026: the system alone)
    [InlineData("Stanton2_Orison", "stanton", "stanton/orison")]               // a named place before its planet
    [InlineData("Nyx_Kaboos", "nyx", null)]                                    // no code and no place: the system alone
    [InlineData("Somewhere", null, null)]
    // the outposts and facilities on the moons, by the moon's letter (the three accounts' logs, Aug-Oct 2026)
    [InlineData("Stanton2c_DrugLab_Jumptown", "stanton", "stanton/yela")]
    [InlineData("Stanton2a_RayariHydro_HickesResearch", "stanton", "stanton/cellin")]
    [InlineData("Stanton4a_RayariHydro_Kaltag", "stanton", "stanton/calliope")]
    [InlineData("Stanton4b_RayariHydro_Cantwell", "stanton", "stanton/clio")]
    [InlineData("Stanton4_DistributionCentre_Greycat_ComplexA", "stanton", "stanton/microtech")]
    [InlineData("PrisonMine_Stanton1b", "stanton", "stanton/aberdeen")]       // Klescher, the code in the middle
    [InlineData("Outpost_OLP_Stanton1b_Vivere", "stanton", "stanton/aberdeen")]
    [InlineData("Pyro2_Outpost_col_m_trdp_indy_001", "pyro", "pyro/monox")]
    [InlineData("Pyro5a_Outpost_col_m_trdpst_otlw_001", "pyro", "pyro/ignis")]
    [InlineData("TheCollectorAsteroid_Stanton4", "stanton", "stanton/wikelo-emporium-kinga-station")]
    public void An_inventory_location_is_a_place(string id, string? system, string? key)
    {
        var (sys, place) = Atlas.Default.OfLocation(id);
        Assert.Equal((system, key), (sys, place?.Key));
    }

    [Theory]
    // what the game named as the start after arriving at each, in the three accounts' logs (Aug-Oct 2026)
    [InlineData("LOC_rs_ext_stan-pyro_jp1", "stanton/pyro-gateway")]      // Stanton's own written short (56 picks)
    [InlineData("rs_ext_cru-leo1", "stanton/seraphim-station")]          // Seraphim Station, seven times
    [InlineData("rs_ext_hur-leo1", "stanton/everus-harbor")]
    [InlineData("rs_ext_arc-l001", "stanton/arc-l2-lively-pathway-station")]   // ARC-L2 Lively Pathway Station, three times
    [InlineData("pyro5a", "pyro/ignis")]
    [InlineData("pyro5c", "pyro/adir")]
    [InlineData("pyro5e", "pyro/fuego")]
    [InlineData("stanton2b", "stanton/daymar")]
    [InlineData("pyro3", "pyro/bloom")]                                  // a planet, as before
    [InlineData("OOC_Stanton_2c_Yela", "stanton/yela")]
    [InlineData("OOC_Stanton_1d_Ita", "stanton/ita")]
    [InlineData("OOC_Stanton_4b_Clio", "stanton/clio")]
    [InlineData("ab_collector_gas_Stanton4", "stanton/wikelo-emporium-kinga-station")]   // six routes to it put it 1 Mm from the station
    [InlineData("ab_mine_stanton4_med_002", "stanton/microtech")]         // a mining base near microTech: on the planet
    [InlineData("rs_ext_nyx-castra_jp1", null)]                          // a gateway the map does not have
    [InlineData("RegionB_2base_ab_pyro_final_set_encounter-001", null)]  // an encounter: the game data does not say where
    [InlineData("ObjectContainer_RestStop", null)]                        // the same name for many stations
    public void A_quantum_destination_in_the_games_own_code_is_a_place(string target, string? key) =>
        Assert.Equal(key, Atlas.Default.OfQuantumTarget(target)?.Key);

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
