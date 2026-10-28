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

        // a location the map does not know: on the admin's Map places
        Assert.True(f.Live.Uploads.Add("zzoii", Id(now.AddMinutes(-3)), 1000, 2000, log.Lines.Skip(log.Lines.Count - 1).ToList(), now).Ok);
        var page = System.Net.WebUtility.HtmlDecode(await admin.GetStringAsync("/admin/places"));
        Assert.Contains("Stanton9_Nowhere", page);
        Assert.Contains("Inventory location", page);
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
        Assert.Equal(("pyro", "pyro/patch-city", "Hauling: 2 To Ruin"),
            (objective.GetProperty("system").GetString(), objective.GetProperty("place").GetString(), objective.GetProperty("title").GetString()));

        // the systems the page draws, with their places
        var systems = map.GetProperty("systems").EnumerateArray().ToList();
        Assert.Equal(new[] { "pyro", "stanton", "nyx" }, systems.Select(s => s.GetProperty("id").GetString()));
        Assert.Contains(systems[1].GetProperty("places").EnumerateArray(), p => p.GetProperty("key").GetString() == "stanton/lorville" && p.GetProperty("kind").GetString() == "city");

        var player = await f.Player(f.AddKey("bileh123"));
        Assert.StartsWith("/login/denied", (await player.GetAsync("/map")).Headers.Location!.PathOf());
        Assert.StartsWith("/login", (await f.Browser().GetAsync("/map/map.json")).Headers.Location!.PathOf());
    }
}
