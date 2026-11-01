using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SCTools.Server;
using SCTools.Tests.Support;

namespace SCTools.Tests.ServerPages;

/// <summary>
/// /showlocation on the site (Frank, 1 Nov 2026): the tracker posts the position, the server keeps it with the player's log, and a player at a mission's
/// site saves the site as a hidden spot the map knows from then on (MissionSpots).
/// </summary>
public class ShowLocationTests
{
    static string Id(DateTime t) => t.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
    const string Beacon = "MISSION_QT_Quantum_Beacon_864684665978";
    const double X = -43498588585.727356, Y = -18397314648.922775;   // zzoega's /showlocation in Nyx, 1 Nov 2026

    static JsonElement Player(JsonElement map, string name) => map.GetProperty("players").EnumerateArray().Single(p => p.GetProperty("name").GetString() == name);

    [Fact]
    public async Task A_showlocation_at_a_missions_site_saves_the_site_and_puts_him_there()
    {
        using var f = new ServerFactory();
        var now = DateTime.UtcNow;
        var m = GameLogWriter.MissionId(11);
        const long Levski = 3723364946, NyxSpace = 2627827560, BaseArea = 126935608;
        // he took the mission at Levski and got to the base (the game wrote no quantum line for that jump; it took the objective's marker away from him)
        var w = new GameLogWriter().At(now.AddMinutes(-10)).Login("zzoega").Inventory("zzoega", "Nyx_Levski")
            .MissionMarker(m, GameLogWriter.BountyContract, "go-to", "1952").ObjectivePopup(m, "go-to", "Go to Asteroid Mining Base").BeaconPaired("1952", Beacon)
            .Minutes(1).AreaChange("zzoega", Levski, 0, Levski, NyxSpace)
            .Minutes(3.5).AreaChange("zzoega", Levski, 0, NyxSpace, BaseArea).Seconds(0.6).MarkerRemoved("zzoega", "1952", m, "go-to")
            .ObjectiveEndPopup(m, "go-to", "Go to Asteroid Mining Base");
        Assert.True(f.Live.Uploads.Add("zzoega", Id(now.AddMinutes(-10)), 0, 1000, w.Lines.ToList(), now).Ok);
        var tracker = f.Tracker(f.AddKey("zzoega"));

        var res = await tracker.PostAsJsonAsync("/api/position", new { time = now, x = X, y = Y, z = 33676.674324 });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("nyx/asteroid-mining-base", JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement.GetProperty("site").GetString());
        var places = f.Services.GetRequiredService<MapPlaces>();
        var site = places.Atlas.Find("nyx/asteroid-mining-base")!;
        Assert.Equal(("Asteroid Mining Base", true, -43.498588585727356), (site.Name, site.Hidden, site.X));
        Assert.Contains(site, places.Atlas.Spots);   // a hidden spot: beacons at its distance are placed there (BeaconRanging)
        Assert.DoesNotContain(site, places.Atlas.Places);   // not drawn unless a player is there
        Assert.Contains(places.Entries(), e => e.Place.Key == site.Key);   // the admin sees it on Map places

        // the map has him there; a second fix 20 km away is the same site, not a new one
        var admin = await f.Admin();
        var z = Player(JsonDocument.Parse(await admin.GetStringAsync("/admin/map/map.json")).RootElement, "zzoega");
        Assert.Equal(("at", "nyx/asteroid-mining-base"), (z.GetProperty("state").GetString(), z.GetProperty("at").GetString()));
        res = await tracker.PostAsJsonAsync("/api/position", new { time = now.AddMinutes(1), x = X + 20_000, y = Y, z = 0.0 });
        Assert.Equal("nyx/asteroid-mining-base", JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement.GetProperty("site").GetString());
        Assert.Single(places.Atlas.Spots, s => s.Name == "Asteroid Mining Base");
        Assert.Equal(2, f.Live.Uploads.Logs.Single(l => l.Uploader == "zzoega").Fixes.Count);
    }

    [Fact]
    public async Task A_showlocation_away_from_a_mission_saves_nothing_and_needs_a_key()
    {
        using var f = new ServerFactory();
        var now = DateTime.UtcNow;
        var w = new GameLogWriter().At(now.AddMinutes(-5)).Login("zzatvo").Inventory("zzatvo", "Nyx_Levski");
        Assert.True(f.Live.Uploads.Add("zzatvo", Id(now.AddMinutes(-5)), 0, 1000, w.Lines.ToList(), now).Ok);
        var res = await f.Tracker(f.AddKey("zzatvo")).PostAsJsonAsync("/api/position", new { time = now, x = X, y = Y, z = 0.0 });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal(JsonValueKind.Null, JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement.GetProperty("site").ValueKind);
        Assert.DoesNotContain(f.Services.GetRequiredService<MapPlaces>().Atlas.Spots, s => !HiddenSpots.All.Contains(s));
        Assert.Single(f.Live.Uploads.Logs.Single(l => l.Uploader == "zzatvo").Fixes);
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.Tracker("not-a-key").PostAsJsonAsync("/api/position", new { time = now, x = X, y = Y, z = 0.0 })).StatusCode);

        // out in open space the map puts him exactly there, at a spot of its own that only the map's data for him has
        var map = JsonDocument.Parse(await (await f.Admin()).GetStringAsync("/admin/map/map.json")).RootElement;
        var a = Player(map, "zzatvo");
        var key = a.GetProperty("at").GetString()!;
        Assert.Equal(("at", true), (a.GetProperty("state").GetString(), key.StartsWith("nyx/showlocation-")));
        var spot = map.GetProperty("systems").EnumerateArray().Single(s => s.GetProperty("id").GetString() == "nyx").GetProperty("places").EnumerateArray()
            .Single(p => p.GetProperty("key").GetString() == key);
        Assert.Equal((X / 1e9, true), (spot.GetProperty("x").GetDouble(), spot.GetProperty("hidden").GetBoolean()));
    }

    [Fact]
    public async Task A_showlocation_long_after_a_mission_out_in_open_space_saves_nothing()
    {
        // he reached the base, then flew out of its area into open space; half an hour later, still with no jump, /showlocation is not at the site
        using var f = new ServerFactory();
        var now = DateTime.UtcNow;
        var m = GameLogWriter.MissionId(12);
        const long Levski = 3723364946, NyxSpace = 2627827560, BaseArea = 126935608;
        var w = new GameLogWriter().At(now.AddMinutes(-40)).Login("zzoega").Inventory("zzoega", "Nyx_Levski")
            .MissionMarker(m, GameLogWriter.BountyContract, "go-to", "1952").ObjectivePopup(m, "go-to", "Go to Asteroid Mining Base").BeaconPaired("1952", Beacon)
            .Minutes(1).AreaChange("zzoega", Levski, 0, Levski, NyxSpace)
            .Minutes(3.5).AreaChange("zzoega", Levski, 0, NyxSpace, BaseArea).Seconds(0.6).MarkerRemoved("zzoega", "1952", m, "go-to")
            .Minutes(5).AreaChange("zzoega", Levski, 0, BaseArea, NyxSpace);
        Assert.True(f.Live.Uploads.Add("zzoega", Id(now.AddMinutes(-40)), 0, 1000, w.Lines.ToList(), now).Ok);
        var res = await f.Tracker(f.AddKey("zzoega")).PostAsJsonAsync("/api/position", new { time = now, x = X, y = Y, z = 0.0 });
        Assert.Equal(JsonValueKind.Null, JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement.GetProperty("site").ValueKind);
        Assert.DoesNotContain(f.Services.GetRequiredService<MapPlaces>().Atlas.Spots, s => !HiddenSpots.All.Contains(s));
    }

    [Theory]
    [InlineData("Go to Asteroid Mining Base", "Asteroid Mining Base")]
    [InlineData("Return to a debris field above Lyria", "a debris field above Lyria")]
    [InlineData("Neutralize Hostiles", "Neutralize Hostiles")]
    public void A_site_is_named_after_its_objective(string objective, string name) => Assert.Equal(name, MissionSpots.SiteName(objective));
}
