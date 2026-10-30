using System.Globalization;
using System.Net;
using System.Text.Json;
using SCTools.Tests.Support;

namespace SCTools.Tests.ServerPages;

/// <summary>
/// The Map tool: everybody playing where his quantum jumps put him (on his way: from where, to where, since when, with his drive), and the objectives. The
/// Hauling Planner's Trip Planner draws the same map of Pyro (/hauling/map.json) with the same engine (systemmap.js).
/// </summary>
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
        Assert.True(z.GetProperty("top").GetDouble() > 0);   // his drive's top speed: the meter shows it while he stands still too (30 Oct 2026)
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
        Assert.Contains("Area", page);
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

    [Fact]
    public async Task The_trip_planner_gets_the_map_tools_Pyro_and_a_pickup_the_planner_credits_is_off_it()
    {
        using var f = new ServerFactory();
        var now = DateTime.UtcNow;
        var m = GameLogWriter.MissionId(7);
        var contract = string.Format(GameLogWriter.HaulingContract, 2);
        // zzoega took a 2 stop contract (Gaslight, Patch City, hand-in at Ruin) and shared it with zzatvo; he picked up at Gaslight
        var mine = new GameLogWriter().At(now.AddMinutes(-40)).Login("zzoega").Accepted(m)
            .Marker(m, contract, "pickup_m7_0", PlayedSession.GaslightZone, GameLogWriter.Gaslight)
            .Marker(m, contract, "pickup_m7_1", PlayedSession.PatchZone, GameLogWriter.PatchCity)
            .Marker(m, contract, "dropoff_m7_0", PlayedSession.RuinZone, GameLogWriter.RuinStation)
            .Minutes(19).Unstow().Seconds(5).Objective(m, "pickup_m7_0", "COMPLETED");
        var his = new GameLogWriter().At(now.AddMinutes(-39)).Login("zzatvo").SharedAs(m, "Senior | Stellar Large Haul | to Ruin Station")
            .Marker(m, contract, "pickup_m7_0", PlayedSession.GaslightZone, GameLogWriter.Gaslight)
            .Marker(m, contract, "pickup_m7_1", PlayedSession.PatchZone, GameLogWriter.PatchCity)
            .Marker(m, contract, "dropoff_m7_0", PlayedSession.RuinZone, GameLogWriter.RuinStation)
            .Minutes(19).Objective(m, "pickup_m7_0", "COMPLETED");
        Assert.True(f.Live.Uploads.Add("zzoega", Id(now.AddMinutes(-40)), 0, 10000, mine.Lines.ToList(), now).Ok);
        Assert.True(f.Live.Uploads.Add("zzatvo", Id(now.AddMinutes(-39)), 0, 10000, his.Lines.ToList(), now).Ok);

        // a player sees the planner's map (the Map tool itself is only the admin's until it is shown): Pyro alone, with the players and the objectives
        var player = await f.Player(f.AddKey("zzoega"));
        Assert.StartsWith("/login/denied", (await player.GetAsync("/map/map.json")).Headers.Location!.PathOf());
        var res = await player.GetAsync("/hauling/map.json");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Contains("no-store", res.Headers.CacheControl?.ToString());
        var map = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(new[] { "pyro", "stanton", "nyx" }, map.GetProperty("systems").EnumerateArray().Select(x => x.GetProperty("id").GetString()));   // every system since stage 2 (routes through the gateways)
        Assert.Contains(map.GetProperty("systems")[0].GetProperty("places").EnumerateArray(), x => x.GetProperty("key").GetString() == "pyro/ruin-station");
        // a player sees the players of his party (Frank, 30 Oct 2026): zzatvo has the shared mission but is in no party with him; the admin sees both
        Assert.Equal(new[] { "zzoega" }, map.GetProperty("players").EnumerateArray().Select(x => x.GetProperty("name").GetString()));
        map = JsonDocument.Parse(await (await f.Admin()).GetStringAsync("/hauling/map.json")).RootElement;
        Assert.Equal(new[] { "zzoega", "zzatvo" }, map.GetProperty("players").EnumerateArray().Select(x => x.GetProperty("name").GetString()).OrderByDescending(n => n));
        Assert.Empty(JsonDocument.Parse(await f.Browser().GetStringAsync("/hauling/map.json")).RootElement.GetProperty("players").EnumerateArray());   // a visitor: the places alone
        var objectives = map.GetProperty("objectives").EnumerateArray().ToList();
        // the pickup at Gaslight is done; the one at Patch City is on the map and the hand-in (its zone is not a pickup station: only in the list), once each,
        // with both who have the mission
        static string? PlaceOf(JsonElement o) => o.TryGetProperty("place", out var place) ? place.GetString() : null;
        Assert.Equal(new[] { null, "pyro/patch-city" }, objectives.Select(PlaceOf).OrderBy(x => x));
        var patch = objectives.Single(o => PlaceOf(o) == "pyro/patch-city");
        Assert.Equal(new[] { "zzoega", "zzatvo" }, patch.GetProperty("who").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal(2, Assert.Single(map.GetProperty("missions").EnumerateArray()).GetProperty("open").GetInt32());

        // he starts loading at Patch City (INPROGRESS): the planner counts the stop as picked up, so the map takes the objective off too
        var more = new GameLogWriter().At(now.AddMinutes(-10)).Unstow().Seconds(5).Objective(m, "pickup_m7_1", "INPROGRESS");
        Assert.True(f.Live.Uploads.Add("zzoega", Id(now.AddMinutes(-40)), 10000, 20000, more.Lines.ToList(), now).Ok);
        var mission = f.Live.Uploads.Logs.First(l => l.Uploader == "zzoega").Missions[m];
        Assert.True(SCTools.Sessions.SessionBuilder.PickedUp(mission, "pickup_m7_1"));
        map = JsonDocument.Parse(await player.GetStringAsync("/hauling/map.json")).RootElement;
        Assert.Equal(new string?[] { null }, map.GetProperty("objectives").EnumerateArray().Select(PlaceOf));
        Assert.Equal(1, Assert.Single(map.GetProperty("missions").EnumerateArray()).GetProperty("open").GetInt32());
    }

    [Fact]
    public async Task The_players_stand_beside_the_map_and_on_the_companion_the_objectives_beside_them_and_the_missions_under_them()
    {
        using var f = new ServerFactory();
        var admin = await f.Admin();
        // the Map tool: the map and the players (the objectives list and the Active Contracts went to the Companion, #380; the map got the room)
        var page = await admin.GetStringAsync("/map");
        Assert.Contains("<nav class=\"subnav area\" id=\"systems\" role=\"tablist\" aria-label=\"Star system\" style=\"--tool:#e0734a\"></nav>", page);   // the systems: the tool's row of tabs (30 Oct 2026)
        Assert.DoesNotContain("<h1>", page);
        Assert.Contains("<div class=\"maptool maponly\">", page);
        Assert.True(page.IndexOf("<div class=\"mapbox\">", StringComparison.Ordinal) < page.IndexOf("<div class=\"mapside players\">", StringComparison.Ordinal));
        Assert.DoesNotContain("<div class=\"mapside objectives\">", page);
        Assert.DoesNotContain("<div class=\"mapmissions\">", page);
        var css = await f.Browser().GetStringAsync("/site.css");
        Assert.Contains(".maptool.maponly { grid-template-columns:minmax(0, 1060px) minmax(260px, 340px); }", css);   // a little bigger than the 820 px it had

        // the Companion: the four cells of the Map tool's grid as it was
        page = await admin.GetStringAsync("/companion");
        var players = page.IndexOf("<div class=\"mapside players\">", StringComparison.Ordinal);
        var objectives = page.IndexOf("<div class=\"mapside objectives\">", StringComparison.Ordinal);
        var missions = page.IndexOf("<div class=\"mapmissions\">", StringComparison.Ordinal);
        Assert.True(page.IndexOf("<div class=\"mapbox\">", StringComparison.Ordinal) < players && players < objectives && objectives < missions);
        Assert.Contains(".mapside.objectives { grid-column:3; grid-row:1; }", css);
        Assert.Contains(".mapmissions { grid-column:1 / span 3; grid-row:2;", css);   // from the map's left edge to the objectives' right edge
        // the objectives list says where and who, not which mission (the Active Contracts table says that)
        var script = await f.Browser().GetStringAsync("/companion.js");
        Assert.Contains("const text = (o.where || '') + (o.system && !here(o.system)", script);
        Assert.DoesNotContain("o.title + ' · '", script);
    }

    [Fact]
    public async Task The_map_tool_and_the_hauling_planner_draw_with_the_same_engine()
    {
        using var f = new ServerFactory();
        var admin = await f.Admin();
        // both pages load the engine (a file of the site, with its version in the address) before their own script
        foreach (var address in new[] { "/map", "/hauling", "/companion" })
        {
            var page = await admin.GetStringAsync(address);
            var engine = page.IndexOf("src=\"/systemmap.js?v=", StringComparison.Ordinal);
            Assert.True(engine > 0 && engine < page.IndexOf(".js?v=", engine + 20, StringComparison.Ordinal), address);
        }
        var res = await f.Browser().GetAsync("/systemmap.js");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("text/javascript", res.Content.Headers.ContentType?.MediaType);
        var script = await res.Content.ReadAsStringAsync();
        Assert.StartsWith("var SystemMap = (function ()", script.Split('\n').First(l => !l.StartsWith("//")));
        Assert.Contains("base.svg.appendChild(base.labels);", script);   // the names of the places above the routes (30 Oct 2026)
        Assert.Contains("[['#', ''], ['Mission', '']", await f.Browser().GetStringAsync("/companion.js"));   // the Active Contracts table numbered like the others
        var mapJs = await f.Browser().GetStringAsync("/map.js");
        Assert.Contains("b.href = '#' + s.id;", mapJs);   // the systems are links like the other tools' tabs, so the row has the same height (30 Oct 2026)
        Assert.Contains(".mapmissions table.missions th:first-child, .mapmissions table.missions td:first-child { width:36px;", await f.Browser().GetStringAsync("/site.css"));
        // the speed meter stays at 0 Mm/s when a player is not in quantum, so his card keeps its size (Frank, 29 Oct 2026)
        Assert.DoesNotContain("m.speed.style.display", script);
        Assert.Contains("m.unit.textContent = 'Mm/s · 0.00 c'", script);
    }
}
