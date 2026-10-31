using System.Net;
using System.Text;
using System.Text.Json;
using SCTools.Server;

namespace SCTools.Tests.ServerPages;

/// <summary>
/// Who sees what (Frank, 30 Oct 2026): everybody sees the tools; a visitor nobody's data but the public leaderboards; a player his own and his party's; the
/// admin everybody's, and with the top bar's View what that player sees. data.json is cut to the viewer by DataViews.
/// </summary>
public class WhoSeesWhatTests
{
    static string Table(string worker0, params string[][] rows) =>
        "{\"headers\":[{\"text\":\"a\"}],\"rows\":[" + string.Join(",", rows.Select(r => "{\"cells\":[" + string.Join(",", r.Select(c => "\"" + c + "\"")) + "]}")) + "]}";

    /// <summary>A table with # first (the Debts, History and Leaderboard tables since 31 Oct 2026): the viewer's rows are numbered 1, 2, 3 again.</summary>
    static string Numbered(params string[][] rows) =>
        "{\"headers\":[{\"text\":\"#\"},{\"text\":\"a\"}],\"rows\":[" + string.Join(",", rows.Select(r => "{\"cells\":[" + string.Join(",", r.Select(c => "\"" + c + "\"")) + "]}")) + "]}";

    /// <summary>A data.json of a session two parties haul in: zzoega with zzatvo, and bileh123 alone.</summary>
    static LiveSnapshot Snapshot()
    {
        var json = "{" +
            "\"updated\":\"12:00:00\",\"session\":\"Session: all\",\"summary\":[{\"k\":\"Start Time\",\"v\":\"10:00\"}]," +
            "\"pickups\":" + Table("", new[] { "1", "Gaslight" }) + ",\"runs\":" + Table("", new[] { "1", "zzoega" }, new[] { "2", "bileh123" }) + "," +
            "\"runsLive\":[null,\"2026-10-30T11:00:00Z\"],\"runKeys\":[\"k1\",\"k2\"],\"runsLost\":\"/hauling/runs/lost\"," +
            "\"planRuns\":" + Table("", new[] { "1", "zzoega" }, new[] { "2", "bileh123" }) + "," +
            "\"planner\":{\"summary\":\"everybody's plan\",\"maps\":[{\"id\":\"pyro\"}],\"workers\":[{\"name\":\"zzoega\"},{\"name\":\"zzatvo\"},{\"name\":\"bileh123\"}]}," +
            "\"debts\":{\"earnings\":" + Numbered(new[] { "1", "zzoega", "1" }, new[] { "2", "bileh123", "2" }) + "," +
                "\"balances\":[{\"name\":\"zzoega\",\"grid\":{}},{\"name\":\"bileh123\",\"grid\":{}}]," +
                "\"transfers\":" + Numbered(new[] { "1", "09:00", "zzatvo", "bileh123", "4", "" }, new[] { "2", "10:00", "zzoega", "zzatvo", "5", "" }, new[] { "3", "11:00", "bileh123", "zzoega", "6", "" }, new[] { "4", "12:00", "zzatvo", "bileh123", "7", "" }) + "," +
                "\"transfersEdit\":[{\"key\":\"t0\",\"from\":\"zzatvo\"},{\"key\":\"t1\",\"from\":\"zzoega\"},{\"key\":\"t2\",\"from\":\"bileh123\"},{\"key\":\"t3\",\"from\":\"zzatvo\"}]," +
                "\"payouts\":" + Numbered(new[] { "1", "11:00", "m2", "100", "bileh123", "100" }, new[] { "2", "10:00", "m1", "100", "zzoega, zzatvo", "50" }) + "}," +
            "\"history\":{\"allTime\":" + Numbered(new[] { "1", "zzoega", "1" }) + ",\"best\":\"Best session\",\"saved\":" +
                Numbered(new[] { "1", "22 okt", "13:28", "9", "20", "bileh123 1,000" }, new[] { "2", "23 okt", "13:28", "9", "20", "zzatvo 8,842,521, zzoega 7,644,729" }) + "}," +
            "\"parties\":[" +
                "{\"title\":\"zzoega's party\",\"leader\":\"zzoega\",\"names\":[\"zzoega\",\"zzatvo\"],\"playing\":true,\"view\":{\"session\":\"Session: zzoega's\",\"runs\":" + Table("", new[] { "1", "zzoega" }) + ",\"runsLive\":[null]}}," +
                "{\"title\":\"bileh123's party\",\"leader\":\"bileh123\",\"names\":[\"bileh123\"],\"playing\":true,\"view\":{\"session\":\"Session: bileh123's\",\"runs\":" + Table("", new[] { "2", "bileh123" }) + ",\"runsLive\":[\"2026-10-30T11:00:00Z\"]}}]" +
            "}";
        return new LiveSnapshot(json, "\"run-1\"");
    }

    static JsonElement View(Viewer viewer, params string[] party)
    {
        var (bytes, etag) = new DataViews().For(Snapshot(), viewer, viewer.Everything ? null : new HashSet<string>(party, StringComparer.OrdinalIgnoreCase));
        Assert.StartsWith("\"", etag);
        return JsonDocument.Parse(bytes).RootElement;
    }

    static string[] Column(JsonElement table, int i) =>
        table.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("cells")[i].GetString()!).ToArray();

    [Fact]
    public void A_visitor_sees_the_maps_and_the_all_time_leaderboard_and_nobody_s_session()
    {
        var d = View(new Viewer(null, false));
        Assert.True(d.GetProperty("visitor").GetBoolean());
        Assert.Equal("", d.GetProperty("session").GetString());
        foreach (var k in new[] { "summary", "pickups", "runs", "planRuns", "runsLive", "runKeys", "debts", "parties" }) Assert.False(d.TryGetProperty(k, out _), k);
        Assert.Empty(d.GetProperty("planner").GetProperty("workers").EnumerateArray());
        Assert.Equal("pyro", d.GetProperty("planner").GetProperty("maps")[0].GetProperty("id").GetString());   // the map is there
        Assert.Equal(new[] { "zzoega" }, Column(d.GetProperty("history").GetProperty("allTime"), 1));   // the public leaderboard (# first, 31 Oct 2026)
        Assert.False(d.GetProperty("history").TryGetProperty("saved", out _));
    }

    [Fact]
    public void A_player_sees_his_party_s_session_and_his_own_debts()
    {
        var d = View(new Viewer("zzoega", false), "zzoega", "zzatvo");
        Assert.Equal("Session: zzoega's", d.GetProperty("session").GetString());   // his party's view, not all of the session's
        Assert.Equal(new[] { "zzoega" }, Column(d.GetProperty("runs"), 1));
        Assert.Equal(new[] { "zzoega" }, Column(d.GetProperty("planRuns"), 1));
        Assert.Single(d.GetProperty("planRunsLive").EnumerateArray());
        Assert.Equal(new[] { "zzoega's party" }, d.GetProperty("parties").EnumerateArray().Select(p => p.GetProperty("title").GetString()));
        Assert.False(d.GetProperty("parties")[0].TryGetProperty("view", out _));
        Assert.False(d.TryGetProperty("runKeys", out _));
        var planner = d.GetProperty("planner");
        Assert.Equal(new[] { "zzoega", "zzatvo" }, planner.GetProperty("workers").EnumerateArray().Select(w => w.GetProperty("name").GetString()));
        Assert.Equal("", planner.GetProperty("summary").GetString());   // it was about everybody

        var debts = d.GetProperty("debts");
        Assert.Equal(new[] { "zzoega" }, Column(debts.GetProperty("earnings"), 1));   // every table has # first (31 Oct 2026)
        Assert.Equal(new[] { "zzoega" }, debts.GetProperty("balances").EnumerateArray().Select(b => b.GetProperty("name").GetString()));
        Assert.Equal(new[] { "10:00", "11:00" }, Column(debts.GetProperty("transfers"), 1));   // he sent the first and got the second; not zzatvo's to bileh123
        Assert.Equal(new[] { "t1", "t2" }, debts.GetProperty("transfersEdit").EnumerateArray().Select(e => e.GetProperty("key").GetString()));   // one for one with the rows
        Assert.Equal(new[] { "m1" }, Column(debts.GetProperty("payouts"), 2));
        Assert.Equal(new[] { "1", "2" }, Column(debts.GetProperty("transfers"), 0));   // his rows numbered 1, 2 again (they were 2 and 3)
        Assert.Equal(new[] { "1" }, Column(debts.GetProperty("payouts"), 0));
        Assert.Equal(new[] { "23 okt" }, Column(d.GetProperty("history").GetProperty("saved"), 1));   // the saved sessions he carried in
        Assert.Equal("1", Column(d.GetProperty("history").GetProperty("saved"), 0)[0]);   // numbered
        Assert.Equal(new[] { "zzoega" }, Column(d.GetProperty("history").GetProperty("allTime"), 1));
        Assert.Equal("1", Column(d.GetProperty("history").GetProperty("allTime"), 0)[0]);
    }

    [Fact]
    public void A_player_in_no_party_of_the_session_sees_none_of_it_and_the_admin_sees_all()
    {
        var outsider = View(new Viewer("pilot_x", false), "pilot_x");
        Assert.Equal("", outsider.GetProperty("session").GetString());
        Assert.False(outsider.TryGetProperty("runs", out _));
        Assert.Empty(outsider.GetProperty("parties").EnumerateArray());
        Assert.Empty(outsider.GetProperty("planner").GetProperty("workers").EnumerateArray());

        var admin = View(new Viewer(null, true));
        Assert.Equal("Session: all", admin.GetProperty("session").GetString());
        Assert.Equal(2, admin.GetProperty("parties").GetArrayLength());
        Assert.Equal(4, admin.GetProperty("debts").GetProperty("transfers").GetProperty("rows").GetArrayLength());
    }

    [Fact]
    public void Every_viewer_has_his_own_etag_and_the_same_data_again_is_the_same()
    {
        var views = new DataViews();
        var snapshot = Snapshot();
        var party = new HashSet<string>(new[] { "zzoega", "zzatvo" }, StringComparer.OrdinalIgnoreCase);
        var a = views.For(snapshot, new Viewer("zzoega", false), party);
        var b = views.For(snapshot, new Viewer("zzoega", false), party);
        var visitor = views.For(snapshot, new Viewer(null, false), new HashSet<string>());
        Assert.Equal(a.ETag, b.ETag);
        Assert.Same(a.Bytes, b.Bytes);   // made once per version and viewer
        Assert.NotEqual(a.ETag, visitor.ETag);
        Assert.Equal(snapshot.ETag, views.For(snapshot, new Viewer(null, true), null).ETag);   // the admin's own view is data.json itself
        Assert.Equal("Session: zzoega's", JsonDocument.Parse(a.Bytes).RootElement.GetProperty("session").GetString());
    }

    [Fact]
    public async Task A_visitor_opens_every_shown_tool_without_logging_in_and_sees_nobody_s_data()
    {
        using var f = new ServerFactory();
        var admin = await f.Admin();
        foreach (var id in new[] { "gauntlet", "session", "party", "map" })
            await ServerFactory.Send(admin, "/admin/tools", "/admin/tools/set", ("id", id), ("value", "true"));
        f.Upload(SCTools.Tests.Support.PlayedSession.Player, SCTools.Tests.Support.PlayedSession.Writer().Lines.ToList());
        var visitor = f.Browser();
        foreach (var page in new[] { "/session", "/party", "/gauntlet", "/gauntlet/stats", "/hauling" })
        {
            var res = await visitor.GetAsync(page);
            Assert.True(res.StatusCode == HttpStatusCode.OK, page + ": " + res.StatusCode);
        }
        Assert.Contains("Log in", await visitor.GetStringAsync("/session"));
        Assert.Contains("Log in", await visitor.GetStringAsync("/party"));
        Assert.Contains("Log in to see your party", await visitor.GetStringAsync("/"));   // the landing page's line of the Hauling Planner
        // the Map is the admin's (31 Oct 2026): a visitor is sent to log in; the Gauntlet Tracker's map gives him the places and nobody on them
        Assert.StartsWith("/login", (await visitor.GetAsync("/admin/map")).Headers.Location!.PathOf());
        var map = JsonDocument.Parse(await visitor.GetStringAsync("/gauntlet/map.json")).RootElement;
        Assert.Empty(map.GetProperty("players").EnumerateArray());
        Assert.NotEmpty(map.GetProperty("systems").EnumerateArray());
        var data = JsonDocument.Parse(await visitor.GetStringAsync("/data.json")).RootElement;
        Assert.False(data.TryGetProperty("pickups", out _));
        Assert.Equal("", JsonDocument.Parse(await visitor.GetStringAsync("/status.json")).RootElement.GetProperty("status").GetString());
    }
}
