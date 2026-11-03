using System.Net;
using Microsoft.Extensions.DependencyInjection;
using SCTools.Server;
using SCTools.Tests.ServerPages;
using SCTools.Tests.Support;

namespace SCTools.Tests.GameData;

/// <summary>
/// Pyro's asteroid bases on the map (Frank, 3 Nov 2026: "all RAB and RMB should be in the drop down menu, they're surrounded by missions"): the 29 RAB and
/// 86 RMB bases of the game data are hidden spots, the log's names for them are found, and the admin can pick one for a code the map does not know.
/// </summary>
public class AsteroidBasesTests
{
    const string Encounter = "RegionB_2base_ab_pyro_final_set_encounter-001";   // zzoega's jump from Bloom, 2 Nov 2026

    [Fact]
    public void Every_RAB_and_RMB_base_of_the_game_data_is_a_hidden_spot()
    {
        Assert.Equal(29, AsteroidBases.All.Count(p => p.Name.StartsWith("RAB-")));
        Assert.Equal(86, AsteroidBases.All.Count(p => p.Name.StartsWith("RMB-")));
        Assert.All(AsteroidBases.All, p => Assert.True(p.Hidden && p.System == "pyro" && HiddenSpots.All.Contains(p)));
        var gulf = Atlas.Default.Find("pyro/rab-gulf")!;
        Assert.Equal(("RAB-GULF", -13.17, -24.0824), (gulf.Name, Math.Round(gulf.X, 4), Math.Round(gulf.Y, 4)));
        Assert.DoesNotContain(gulf, Atlas.Default.Places);   // not drawn unless a player is there
        Assert.Equal(new[] { gulf }, Atlas.Default.BasesOf("pyro").Where(p => p.Name == "RAB-GULF"));
        Assert.Empty(Atlas.Default.BasesOf("nyx"));
    }

    [Fact]
    public void The_logs_name_for_a_base_is_that_base()
    {
        // "Projected Start Location is RAB-GULF", "routing from PYR1 L3 to RAB-YORK" (zzoega, Oct 2026)
        Assert.Equal("pyro/rab-gulf", Atlas.Default.OfName("RAB-GULF", "pyro").Place?.Key);
        Assert.Equal("pyro/rab-york", Atlas.Default.OfQuantumTarget("RAB-YORK", "pyro")?.Key);
        Assert.Equal("pyro/rmb-bale", Atlas.Default.OfName("rmb-bale").Place?.Key);
        Assert.Null(Atlas.Default.OfQuantumTarget(Encounter, "pyro"));   // an encounter's code is not a base's name: the admin says which it is
    }

    [Fact]
    public void A_beacon_at_a_bases_distance_is_placed_there()
    {
        // from Bloom, his Perseus's fuel estimate for the encounter (311746.09) at its 32273 per Gm is RAB-GULF's distance
        var bloom = Atlas.Default.Find("pyro/bloom")!;
        Assert.Equal("pyro/rab-gulf", BeaconRanging.Pick(Atlas.Default, bloom, 311746.09375 / 32273)?.Key);
    }

    [Fact]
    public async Task The_admin_picks_a_base_for_a_code_the_map_does_not_know()
    {
        using var f = new ServerFactory();
        var places = f.Services.GetRequiredService<MapPlaces>();
        var groups = MapPlaces.PickGroups(places.Atlas).ToList();
        Assert.Equal(new[] { "Pyro", "Pyro: asteroid bases" }, groups.Select(g => g.Name).Take(2));
        Assert.Equal(115, groups[1].Places.Count);
        Assert.Equal("RAB-ALPHA", groups[1].Places[0].Name);

        var admin = await f.Admin();
        var page = WebUtility.HtmlDecode(await admin.GetStringAsync("/admin/map"));
        Assert.Contains("<optgroup label=\"Pyro: asteroid bases\">", page);
        Assert.Contains("value=\"pyro/rab-gulf\"", page);
        Assert.Equal(HttpStatusCode.Redirect, (await ServerFactory.Send(admin, "/admin/map", "/admin/places/alias", ("text", Encounter), ("key", "pyro/rab-gulf"))).StatusCode);
        Assert.Equal("pyro/rab-gulf", places.Atlas.OfQuantumTarget(Encounter, "pyro")?.Key);
    }
}
