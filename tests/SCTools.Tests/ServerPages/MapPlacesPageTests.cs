using System.Globalization;
using System.Net;
using System.Text.Json;
using SCTools.Server;
using SCTools.Tests.Support;

namespace SCTools.Tests.ServerPages;

/// <summary>
/// Map places (admin): the destinations and start names in the logs that the map does not know, the admin saying what they are (a place, or not a place to
/// show), his own names for places and the places he adds; the map uses them at once.
/// </summary>
public class MapPlacesPageTests
{
    static string Id(DateTime t) => t.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

    static JsonElement Player(JsonElement map, string name) => map.GetProperty("players").EnumerateArray().Single(p => p.GetProperty("name").GetString() == name);

    [Fact]
    public async Task The_admin_says_what_the_map_does_not_know_and_the_map_puts_the_player_there()
    {
        using var f = new ServerFactory();
        var now = DateTime.UtcNow;
        const string Asteroid = "AsteroidCluster_Pyro_004";
        const string Encounter = "RegionB_7f3_encounter-003";
        // zzoega jumped from Ruin to an asteroid cluster and arrived; then he picked an encounter from a start the map does not know either
        var log = new GameLogWriter().At(now.AddMinutes(-6)).Login("zzoega").QuantumSelected(Asteroid).RouteStart("Ruin Station", Asteroid).Minutes(2).QuantumArrived()
            .Minutes(1).QuantumSelected(Encounter).RouteStart("Spot Seven", Encounter).Minutes(1).QuantumArrived();
        Assert.True(f.Live.Uploads.Add("zzoega", Id(now.AddMinutes(-6)), 0, 1000, log.Lines.ToList(), now).Ok);
        var admin = await f.Admin();
        var page = WebUtility.HtmlDecode(await admin.GetStringAsync("/admin/map"));
        Assert.Contains(Asteroid, page);
        Assert.Contains(Encounter, page);
        Assert.Contains("Spot Seven", page);
        async Task<JsonElement> Map() => JsonDocument.Parse(await admin.GetStringAsync("/admin/map/map.json")).RootElement;
        Assert.Equal("unknown", Player(await Map(), "zzoega").GetProperty("state").GetString());

        // every asteroid cluster of Pyro is Bloom (for this test); the encounters are not places
        Assert.Equal(HttpStatusCode.Redirect, (await ServerFactory.Send(admin, "/admin/map", "/admin/places/alias", ("text", "AsteroidCluster_Pyro_*"), ("key", "pyro/bloom"))).StatusCode);
        await ServerFactory.Send(admin, "/admin/map", "/admin/places/alias", ("text", "RegionB_*"), ("key", MapPlaces.NotAPlace));
        await ServerFactory.Send(admin, "/admin/map", "/admin/places/alias", ("text", "Somewhere"), ("key", ""));
        page = WebUtility.HtmlDecode(await admin.GetStringAsync("/admin/map"));
        Assert.Contains("Pick what Somewhere is", page);
        Assert.DoesNotContain("<code>" + Asteroid + "</code>", page);
        Assert.DoesNotContain("<code>" + Encounter + "</code>", page);

        // a place he adds for the start name; a name of his own for Bloom
        await ServerFactory.Send(admin, "/admin/map", "/admin/places/add",
            ("system", "pyro"), ("name", "Spot Seven Outpost"), ("shortName", "Spot 7"), ("kind", "Station"), ("at", "pyro/bloom"), ("x", ""), ("y", ""), ("alias", "Spot Seven"));
        await ServerFactory.Send(admin, "/admin/map", "/admin/places/rename", ("key", "pyro/bloom"), ("field", "name"), ("value", "Bloom (home)"), ("back", "/admin/places"));
        page = WebUtility.HtmlDecode(await admin.GetStringAsync("/admin/map"));
        Assert.Contains("Nothing: the map knows every destination", page);
        Assert.Contains("Bloom (home) (Pyro)", page);   // what he said about the asteroids

        var map = await Map();
        var z = Player(map, "zzoega");
        Assert.Equal(("pyro", "unknown"), (z.GetProperty("system").GetString(), z.GetProperty("state").GetString()));   // the encounter is no place: he is somewhere
        var pyro = map.GetProperty("systems").EnumerateArray().First().GetProperty("places").EnumerateArray().ToList();
        Assert.Contains(pyro, p => p.GetProperty("key").GetString() == "pyro/spot-seven-outpost" && p.GetProperty("short").GetString() == "Spot 7" && p.GetProperty("parent").GetString() == "bloom");
        Assert.Contains(pyro, p => p.GetProperty("key").GetString() == "pyro/bloom" && p.GetProperty("name").GetString() == "Bloom (home)");

        // the whereabouts with his words: from Ruin to the asteroids (Bloom), then from Spot Seven to the encounter
        var places = (MapPlaces)f.Services.GetService(typeof(MapPlaces))!;
        List<QtEvent> qt;
        lock (f.Live.Uploads.Sync) qt = f.Live.Uploads.Logs.Single().Qt.ToList();
        Assert.Equal("pyro/bloom", LocationTracker.Follow(qt.Take(2).ToList(), places.Atlas).At?.Key);
        Assert.Equal("pyro/spot-seven-outpost", LocationTracker.Follow(qt.Take(3).ToList(), places.Atlas).From?.Key);

        // taking the added place off: Spot Seven is not known again; the game's name back for Bloom
        await ServerFactory.Send(admin, "/admin/map", "/admin/places/remove", ("key", "pyro/spot-seven-outpost"));
        await ServerFactory.Send(admin, "/admin/map", "/admin/places/rename", ("key", "pyro/bloom"), ("field", "name"), ("value", ""), ("back", "/admin/places"));
        page = WebUtility.HtmlDecode(await admin.GetStringAsync("/admin/map"));
        Assert.Contains("value=\"Spot Seven\"", page);   // in Not known yet again
        Assert.Equal("Bloom", places.Atlas.Find("pyro/bloom")?.Name);

        // only the admin
        var player = await f.Player(f.AddKey("zzoega"));
        Assert.StartsWith("/login/denied", (await player.GetAsync("/admin/places")).Headers.Location!.PathOf());
        Assert.Equal("/admin/map?system=pyro", (await admin.GetAsync("/admin/places?system=pyro")).Headers.Location?.OriginalString);   // one tab with the Map since 31 Oct 2026
    }
}
