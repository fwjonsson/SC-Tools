using System.Globalization;
using System.Net;
using System.Text.Json;
using SCTools.Tests.Support;

namespace SCTools.Tests.ServerPages;

/// <summary>
/// The Party tool: who is playing with whom (the game's own party, from everybody's log), everybody else, and a player's own page; the Party button in the top
/// bar with its panel, instead of a card on the landing page.
/// </summary>
public class PartyToolTests
{
    static string Id(DateTime t) => t.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

    [Fact]
    public async Task The_party_tool_shows_who_plays_with_whom_and_what_there_is_to_know_about_a_player()
    {
        using var f = new ServerFactory();
        Assert.StartsWith("/login", (await f.Browser().GetAsync("/party")).Headers.Location!.PathOf());   // never for somebody not logged in
        var now = DateTime.UtcNow;
        // zzoega made a party and zzatvo (no tracker) joined it, then he jumped in his C2; bileh123 plays alone; pilot_x has a key and has not played
        var mine = new GameLogWriter().At(now.AddMinutes(-20)).Login("zzoega").Minutes(1).PartyCreated().Minutes(1).MemberJoined("zzatvo").Minutes(15).QuantumSelected("Gaslight");
        Assert.True(f.Live.Uploads.Add("zzoega", Id(now.AddMinutes(-20)), 0, 1000, mine.Lines.ToList(), now).Ok);
        var his = new GameLogWriter().At(now.AddMinutes(-10)).Login("bileh123").Minutes(5);
        Assert.True(f.Live.Uploads.Add("bileh123", Id(now.AddMinutes(-10)), 0, 1000, his.Lines.ToList(), now).Ok);
        f.AddKey("pilot_x");
        var admin = await f.Admin();

        var page = WebUtility.HtmlDecode(await admin.GetStringAsync("/party"));   // only the admin until it is shown
        Assert.Contains("zzoega's party", page);
        Assert.Contains("href=\"/party/zzatvo\"", page);
        Assert.Contains("Crusader C2 Hercules Starlifter", page);   // the ship of his last quantum jump (not in a fleet: the game's name for its class)
        Assert.Contains("href=\"/party/bileh123\"", page);
        Assert.Contains("Not playing", page);
        Assert.Contains("href=\"/party/pilot_x\"", page);
        // not a card on the landing page: the Party button beside the name in the top bar, on the tools' own pages too, and its panel (fetched when it opens)
        var home = await admin.GetStringAsync("/");
        Assert.DoesNotContain("<span class=\"tool-name\">Party</span>", home);
        Assert.Contains("<details class=\"party\" style=\"--tool:#b77fdb\" ontoggle=\"partyPanel(this)\"><summary class=\"area\">Party</summary>", home);   // after the name and the admin's View as pick (30 Oct 2026)
        Assert.Contains("<span>admin</span><form method=\"post\" action=\"/admin/pov\" class=\"inline pov\">", home);
        Assert.Contains("function partyPanel(d)", home);
        Assert.Contains("<details class=\"party\"", await admin.GetStringAsync("/hauling"));
        Assert.Contains("<summary class=\"area on\" aria-current=\"page\">Party</summary>", await admin.GetStringAsync("/party/zzoega"));   // on the Party pages: marked
        var panel = WebUtility.HtmlDecode(await admin.GetStringAsync("/party/panel"));
        Assert.StartsWith("<div class=\"partypanel\">", panel.TrimStart());   // a fragment, no page around it
        Assert.Contains("3 playing in 2 parties", panel);
        Assert.Contains("zzoega's party", panel);
        // every party a table like the Hauling Planner's old Party table: member, ship, drive, the row in his colour (zzoega orange with black text)
        Assert.Contains("<table class=\"members\">", panel);
        Assert.Contains("<tr><th>Member</th><th>Ship</th><th>QT Drive</th>", panel);
        Assert.Contains("<tr style=\"background:#ff9628;color:#000000\"><td><a href=\"/party/zzoega\">ZZOEGA</a></td><td>", panel);
        Assert.Contains("href=\"/party/zzatvo\">ZZATVO</a></td><td>", panel);
        // the admin's Active tick (the Hauling Planner's party: zzoega and zzatvo are in it, bileh123 is not): off when somebody stopped playing and the
        // logs still count him, so the planner plans no trips for him; it holds until the logs change their mind (Frank, 29 Oct 2026)
        Assert.Contains("<th>Active</th>", panel);
        Assert.Contains("<input type=\"checkbox\" name=\"active\" data-name=\"zzatvo\" checked=\"checked\"", panel);
        Assert.DoesNotContain("data-name=\"bileh123\"", panel);   // not in the planner's party: nothing to tick
        var res = await ServerFactory.Send(admin, "/party", "/party/active", ("name", "zzatvo"), ("active", "false"));
        Assert.StartsWith("/party", res.Headers.Location!.PathOf());
        Assert.False(f.Live.Read(t => t.Crew.IsActiveAt("zzatvo", DateTime.UtcNow)));
        Assert.DoesNotContain("zzatvo", f.Live.Read(t => t.Crew.ActiveCrew()), StringComparer.OrdinalIgnoreCase);   // the planner's crew: without him
        var after = WebUtility.HtmlDecode(await admin.GetStringAsync("/party/panel"));
        Assert.Contains("<tr class=\"inactive\"", after);
        Assert.Contains("data-name=\"zzatvo\" aria-label", after);   // not checked
        Assert.Contains("function(r){if(!r.ok)throw new Error(r.status);return r.json();}", await admin.GetStringAsync("/"));   // the tick is sent in the background
        Assert.Contains("<td>Crusader C2 Hercules Starlifter</td><td></td>", panel);   // the class of his last quantum jump; no fleet: no drive
        Assert.Contains("Not playing: ", panel);   // everybody else the site knows (pilot_x has a key and has not played)
        Assert.Contains("pilot_x", panel[panel.IndexOf("Not playing: ", StringComparison.Ordinal)..]);
        Assert.Contains("href=\"/party\"", panel);
        Assert.StartsWith("/login", (await f.Browser().GetAsync("/party/panel")).Headers.Location!.PathOf());

        var zzoega = WebUtility.HtmlDecode(await admin.GetStringAsync("/party/zzoega"));
        Assert.Contains("<td>Playing</td>", zzoega);
        Assert.Contains("zzoega's party: zzatvo, zzoega", zzoega);
        Assert.Contains("(his last quantum jump", zzoega);
        Assert.Contains("No ships on his page yet.", zzoega);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/party/nobody")).StatusCode);

        var player = await f.Player(f.AddKey("bileh123"));
        Assert.StartsWith("/login/denied", (await player.GetAsync("/party")).Headers.Location!.PathOf());
        Assert.StartsWith("/login/denied", (await player.GetAsync("/party/panel")).Headers.Location!.PathOf());
        Assert.DoesNotContain("<details class=\"party\"", await player.GetStringAsync("/"));   // no button until the tool is shown
        await ServerFactory.Send(admin, "/admin/tools", "/admin/tools/set", ("id", "party"), ("value", "true"));
        Assert.Contains("Party is in the top bar for everybody now.", await admin.GetStringAsync("/admin/tools"));
        Assert.Equal(HttpStatusCode.OK, (await player.GetAsync("/party/zzoega")).StatusCode);
        Assert.Contains("<details class=\"party\"", await player.GetStringAsync("/"));
        var his2 = WebUtility.HtmlDecode(await player.GetStringAsync("/party/panel"));
        Assert.Contains("bileh123", his2);
        Assert.DoesNotContain("<th>Active</th>", his2);   // bileh123 leads nobody in the planner's party: no tick for him (zzoega's party is not his)
        // the leader of a party ticks his members (Frank, 29 Oct 2026): zzoega brings zzatvo back; bileh123 cannot touch zzoega's party
        var leader = await f.Player(f.AddKey("zzoega"));
        var leaders = WebUtility.HtmlDecode(await leader.GetStringAsync("/party/panel"));
        Assert.Contains("<th>Active</th>", leaders);
        Assert.Contains("data-name=\"zzatvo\" aria-label", leaders);
        var quiet = await Quiet(leader, "zzatvo", true);
        Assert.True(JsonDocument.Parse(await quiet.Content.ReadAsStringAsync()).RootElement.GetProperty("ok").GetBoolean());
        Assert.True(f.Live.Read(t => t.Crew.IsActiveAt("zzatvo", DateTime.UtcNow)));
        var refused = JsonDocument.Parse(await (await Quiet(player, "zzoega", false)).Content.ReadAsStringAsync()).RootElement;
        Assert.False(refused.GetProperty("ok").GetBoolean());
        Assert.Contains("leader", refused.GetProperty("said").GetString());
        Assert.True(f.Live.Read(t => t.Crew.IsActiveAt("zzoega", DateTime.UtcNow)));
    }

    /// <summary>The Active tick sent in the background, as the panel's script sends it (X-Autosave: JSON back).</summary>
    static async Task<HttpResponseMessage> Quiet(HttpClient c, string name, bool active)
    {
        var page = await c.GetStringAsync("/");
        using var req = new HttpRequestMessage(HttpMethod.Post, "/party/active")
        {
            Content = new FormUrlEncodedContent(new[] { KeyValuePair.Create("name", name), KeyValuePair.Create("active", active ? "true" : "false"),
                KeyValuePair.Create("__RequestVerificationToken", ServerFactory.Token(page)) }),
        };
        req.Headers.Add("X-Autosave", "1");
        return await c.SendAsync(req);
    }
}
