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
        Assert.Equal(("Ruin", "69936"), (z.GetProperty("from").GetString(), z.GetProperty("to").GetString()));
        Assert.Equal(105.09e9, z.GetProperty("dist").GetDouble(), 0);
        var start = new DateTimeOffset(now.AddMinutes(-1) + SCTools.Server.MapService.JumpDelay).ToUnixTimeMilliseconds();
        Assert.InRange(z.GetProperty("start").GetInt64(), start - 5, start + 5);   // a few seconds after he picked the destination
        Assert.True(z.GetProperty("motion").GetProperty("v").GetDouble() > 1e8);   // a drive (an assumed one: he set none)

        var b = Player(map, "bileh123");
        Assert.Equal(("at", "69936"), (b.GetProperty("state").GetString(), b.GetProperty("at").GetString()));

        var objective = Assert.Single(map.GetProperty("objectives").EnumerateArray());
        Assert.Equal(("70120", "Hauling: 2 To Ruin"), (objective.GetProperty("station").GetString(), objective.GetProperty("title").GetString()));

        var player = await f.Player(f.AddKey("bileh123"));
        Assert.StartsWith("/login/denied", (await player.GetAsync("/map")).Headers.Location!.PathOf());
        Assert.StartsWith("/login", (await f.Browser().GetAsync("/map/map.json")).Headers.Location!.PathOf());
    }

    [Fact]
    public void A_point_of_the_system_is_placed_on_the_map_where_the_stations_are()
    {
        // Ruin's real position (Gm, from the Wiki) in metres lands on Ruin on the map, within a few pixels
        var (x, y) = PyroDistances.MapOfSystem(-52.3368e9, -43.9179e9);
        var ruin = PyroDistances.Points.Single(p => p.Id == PyroDistances.Ruin);
        Assert.True(Math.Abs(x - ruin.X) < 3 && Math.Abs(y - ruin.Y) < 3, $"{x}, {y}");
        Assert.Equal(PyroDistances.Ruin, PyroDistances.IdOfPlace("Ruin Station"));
        Assert.Equal("74320", PyroDistances.IdOfPlace("Nyx Gateway"));
        Assert.Equal("72645", PyroDistances.IdOfPlace("Rod's Fuel 'N Supplies"));
        Assert.Null(PyroDistances.IdOfPlace("Somewhere"));
    }
}
