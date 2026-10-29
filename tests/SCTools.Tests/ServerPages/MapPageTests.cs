using System.Globalization;
using System.Net;
using System.Text.Json;
using SCTools.Tests.Support;

namespace SCTools.Tests.ServerPages;

/// <summary>The Map tool: everybody playing where his quantum jumps put him (on his way: from where, to where, since when, with his drive), and the objectives.</summary>
public class MapPageTests
{
    static string Id(DateTime t) => t.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
    const string Gaslight = "rs_ext_pyro5_l2";

    static JsonElement Player(JsonElement map, string name) => map.GetProperty("players").EnumerateArray().Single(p => p.GetProperty("name").GetString() == name);

    [Fact]
    public async Task A_player_in_Stanton_is_on_the_map_of_Stanton()
    {
        using var f = new ServerFactory();
        var now = DateTime.UtcNow;
        // from Aberdeen (a moon of Hurston) to HUR-L2, half a minute ago
        var his = new GameLogWriter().At(now.AddMinutes(-2)).Login("zzoega").Minutes(1).QuantumSelected("LOC_RR_S1_L2").RouteStart("Aberdeen", "LOC_RR_S1_L2");
        Assert.True(f.Live.Uploads.Add("zzoega", Id(now.AddMinutes(-2)), 0, 1000, his.Lines.ToList(), now).Ok);
        var map = JsonDocument.Parse(await (await f.Admin()).GetStringAsync("/map/map.json")).RootElement;
        var z = Player(map, "zzoega");
        Assert.Equal(("stanton", "jump", "stanton/aberdeen", "stanton/hur-l2-faithful-dream-station"),
            (z.GetProperty("system").GetString(), z.GetProperty("state").GetString(), z.GetProperty("from").GetString(), z.GetProperty("to").GetString()));
        Assert.InRange(z.GetProperty("dist").GetDouble(), 1.0e9, 1.5e9);
    }

    [Fact]
    public async Task A_player_who_opened_his_inventory_is_at_that_place()
    {
        using var f = new ServerFactory();
        var now = DateTime.UtcNow;
        var log = new GameLogWriter().At(now.AddMinutes(-3)).Login("zzoii").Minutes(1).Inventory("zzoii", "Nyx_Levski").Inventory("zzoii", "Stanton9_Nowhere");
        Assert.True(log.Lines.Where(l => l.Contains(HaulingLog.InventoryLine)).All(l => new LineFilter().Keep(l)));   // the trackers send these lines
        Assert.True(f.Live.Uploads.Add("zzoii", Id(now.AddMinutes(-3)), 0, 1000, log.Lines.Take(log.Lines.Count - 1).ToList(), now).Ok);
        var admin = await f.Admin();
        var z = Player(JsonDocument.Parse(await admin.GetStringAsync("/map/map.json")).RootElement, "zzoii");
        Assert.Equal(("nyx", "at", "nyx/levski"), (z.GetProperty("system").GetString(), z.GetProperty("state").GetString(), z.GetProperty("at").GetString()));

        // a location the map does not know: on the admin's Map places, and he is still where the map had him (not "no signal yet")
        Assert.True(f.Live.Uploads.Add("zzoii", Id(now.AddMinutes(-3)), 1000, 2000, log.Lines.Skip(log.Lines.Count - 1).ToList(), now).Ok);
        var page = System.Net.WebUtility.HtmlDecode(await admin.GetStringAsync("/admin/places"));
        Assert.Contains("Stanton9_Nowhere", page);
        Assert.Contains("Inventory location", page);
        z = Player(JsonDocument.Parse(await admin.GetStringAsync("/map/map.json")).RootElement, "zzoii");
        Assert.Equal(("nyx", "at", "nyx/levski"), (z.GetProperty("system").GetString(), z.GetProperty("state").GetString(), z.GetProperty("at").GetString()));
    }

    [Fact]
    public async Task A_bounty_is_in_the_list_and_a_jump_at_its_marker_puts_him_at_the_mission()
    {
        using var f = new ServerFactory();
        var now = DateTime.UtcNow;
        var m = GameLogWriter.MissionId(5);
        const string beacon = "MISSION_QT_Quantum_Beacon_LongRange_865476577647";
        // zzoega took a bounty in Nyx at Levski (the game gives its marker no position) and jumped at its marker, with no destination picked
        var his = new GameLogWriter().At(now.AddMinutes(-6)).Login("zzoega").Inventory("zzoega", "Nyx_Levski")
            .MissionMarker(m, GameLogWriter.BountyContract, "obj-1", "21517").ObjectivePopup(m, "obj-1", "Go to Asteroid Cluster").BeaconPaired("21517", beacon)
            .Minutes(4).QuantumArrived("RSI_Perseus", "898683539008");
        Assert.True(f.Live.Uploads.Add("zzoega", Id(now.AddMinutes(-6)), 0, 1000, his.Lines.ToList(), now).Ok);
        var map = JsonDocument.Parse(await (await f.Admin()).GetStringAsync("/map/map.json")).RootElement;

        var z = Player(map, "zzoega");
        Assert.Equal(("nyx", "mission", "the mission: Go to Asteroid Cluster"),
            (z.GetProperty("system").GetString(), z.GetProperty("state").GetString(), z.GetProperty("atName").GetString()));
        var objective = Assert.Single(map.GetProperty("objectives").EnumerateArray());
        Assert.Equal(("nyx", "Go to Asteroid Cluster", false), (objective.GetProperty("system").GetString(), objective.GetProperty("where").GetString(),
            objective.TryGetProperty("place", out _)));
        Assert.Equal(1, map.GetProperty("unplaced").GetInt32());   // only in the list

        // zzatvo jumped from Levski at something with no mission going on: off the grid, last seen at Levski
        var lost = new GameLogWriter().At(now.AddMinutes(-6)).Login("zzatvo").Inventory("zzatvo", "Nyx_Levski").Minutes(4).QuantumArrived("ANVL_Arrow", "777777777777");
        Assert.True(f.Live.Uploads.Add("zzatvo", Id(now.AddMinutes(-6)), 0, 1000, lost.Lines.ToList(), now).Ok);
        var a = Player(JsonDocument.Parse(await (await f.Admin()).GetStringAsync("/map/map.json")).RootElement, "zzatvo");
        Assert.Equal(("nyx", "unknown", true, "Levski"),
            (a.GetProperty("system").GetString(), a.GetProperty("state").GetString(), a.GetProperty("off").GetBoolean(), a.GetProperty("lastPing").GetString()));
    }

    [Fact]
    public async Task The_planners_pickups_place_a_member_when_they_are_newer_than_his_jumps()
    {
        using var f = new ServerFactory { Data = PlayedSession.Data() };
        var now = DateTime.UtcNow;
        // Pilot_A hauled: his last pickup was at Gaslight (the planner knows), and his log has no quantum travel at all (the map does not)
        Assert.True(f.Live.Uploads.Add(PlayedSession.Player, Id(GameLogWriter.Start), 0, 10000, PlayedSession.Writer().Lines.ToList(), now).Ok);
        f.Live.Refresh(now);
        var map = JsonDocument.Parse(await (await f.Admin()).GetStringAsync("/map/map.json")).RootElement;
        var a = Player(map, PlayedSession.Player);
        Assert.Equal(("pyro", "at", "pyro/gaslight", "the Hauling Planner"),
            (a.GetProperty("system").GetString(), a.GetProperty("state").GetString(), a.GetProperty("at").GetString(), a.GetProperty("via").GetString()));
    }

    [Fact]
    public async Task A_player_in_quantum_is_on_his_way_and_one_who_arrived_is_there()
    {
        using var f = new ServerFactory();
        var now = DateTime.UtcNow;
        var m1 = GameLogWriter.MissionId(1);
        // zzoega picked Gaslight at Ruin a minute ago (and has a contract with a pickup at Patch City); bileh123 jumped to Gaslight and arrived
        var mine = new GameLogWriter().At(now.AddMinutes(-3)).Login("zzoega").Accepted(m1)
            .Marker(m1, string.Format(GameLogWriter.HaulingContract, 2), "pickup_aaaa_0", "900000000202", GameLogWriter.PatchCity)
            .Minutes(2).QuantumSelected(Gaslight).RouteStart("Ruin Station", Gaslight);
        Assert.True(f.Live.Uploads.Add("zzoega", Id(now.AddMinutes(-3)), 0, 1000, mine.Lines.ToList(), now).Ok);
        var his = new GameLogWriter().At(now.AddMinutes(-9)).Login("bileh123").QuantumSelected(Gaslight).RouteStart("Ruin Station", Gaslight).Minutes(4).QuantumArrived();
        Assert.True(f.Live.Uploads.Add("bileh123", Id(now.AddMinutes(-9)), 0, 1000, his.Lines.ToList(), now).Ok);
        var admin = await f.Admin();
        Assert.Contains("map.js", await admin.GetStringAsync("/map"));   // only the admin until it is shown

        var map = JsonDocument.Parse(await admin.GetStringAsync("/map/map.json")).RootElement;
        var z = Player(map, "zzoega");
        Assert.Equal("jump", z.GetProperty("state").GetString());
        Assert.Equal(("pyro", "pyro/ruin-station", "pyro/gaslight"), (z.GetProperty("system").GetString(), z.GetProperty("from").GetString(), z.GetProperty("to").GetString()));
        Assert.Equal(105.09e9, z.GetProperty("dist").GetDouble(), 0);
        var start = new DateTimeOffset(now.AddMinutes(-1) + SCTools.Server.MapService.JumpDelay).ToUnixTimeMilliseconds();
        Assert.InRange(z.GetProperty("start").GetInt64(), start - 5, start + 5);   // a few seconds after he picked the destination
        Assert.True(z.GetProperty("motion").GetProperty("v").GetDouble() > 1e8);   // a drive (an assumed one: he set none)

        var b = Player(map, "bileh123");
        Assert.Equal(("at", "pyro/gaslight"), (b.GetProperty("state").GetString(), b.GetProperty("at").GetString()));

        var objective = Assert.Single(map.GetProperty("objectives").EnumerateArray());
        Assert.Equal(("pyro", "pyro/patch-city", "Senior | Stellar Large Haul | to Ruin Station [50/200/250/500/1000/2000/4000 Rep]"),   // by its name, as the library
            (objective.GetProperty("system").GetString(), objective.GetProperty("place").GetString(), objective.GetProperty("title").GetString()));
        var mission = Assert.Single(map.GetProperty("missions").EnumerateArray());   // the table under the map: the mission, who has it, the library's columns
        Assert.Equal(("Senior | Stellar Large Haul | to Ruin Station [50/200/250/500/1000/2000/4000 Rep]", "zzoega", 1),
            (mission.GetProperty("title").GetString(), Assert.Single(mission.GetProperty("who").EnumerateArray()).GetString(), mission.GetProperty("open").GetInt32()));
        Assert.True(mission.TryGetProperty("faction", out _) && mission.TryGetProperty("legality", out _));

        // the systems the page draws, with their places
        var systems = map.GetProperty("systems").EnumerateArray().ToList();
        Assert.Equal(new[] { "pyro", "stanton", "nyx" }, systems.Select(s => s.GetProperty("id").GetString()));
        Assert.Contains(systems[1].GetProperty("places").EnumerateArray(), p => p.GetProperty("key").GetString() == "stanton/lorville" && p.GetProperty("kind").GetString() == "city");

        var player = await f.Player(f.AddKey("bileh123"));
        Assert.StartsWith("/login/denied", (await player.GetAsync("/map")).Headers.Location!.PathOf());
        Assert.StartsWith("/login", (await f.Browser().GetAsync("/map/map.json")).Headers.Location!.PathOf());
    }
}
