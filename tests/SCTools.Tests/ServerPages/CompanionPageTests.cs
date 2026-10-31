using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SCTools.Missions;
using SCTools.Server;
using SCTools.Tests.Support;

namespace SCTools.Tests.ServerPages;

/// <summary>
/// The Companion (#380): a player's own page to keep open while playing. The map with him and his party, his party's Active Contracts and the ones it ended
/// this game session with who had each, his game session's figures; the admin fills in the Mission Library from it. The Map tool lost its objectives list
/// and its contracts to it.
/// </summary>
public class CompanionPageTests
{
    static string T(DateTime t) => t.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

    static string Accept(string id, string title, DateTime t) =>
        $"<{T(t)}> [Notice] <SHUDEvent_OnNotification> Added notification \"Contract Accepted:  {title}: \" [12] to queue. New queue size: 1, MissionId: [{id}], ObjectiveId: [x]";

    static string End(string id, string title, string how, DateTime t) =>
        $"<{T(t)}> [Notice] <SHUDEvent_OnNotification> Added notification \"Contract {how}:  {title}: \" [12] to queue. New queue size: 1, MissionId: [{id}], ObjectiveId: [x]";

    static string Abandon(string id, string player, DateTime t) =>
        $"<{T(t)}> [Notice] <EndMission> Ending mission for player. MissionId[{id}] Player[{player}] PlayerId[100000000001] CompletionType[Abandon] Reason[Player left] [Team_MissionFeatures][Missions]";

    static string Id(int n) => $"c0000000-0000-4000-8000-{n:000000000000}";

    static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement;

    static string[] Names(JsonElement list, string property = "name") => list.EnumerateArray().Select(p => p.GetProperty(property).GetString()!).Order().ToArray();

    /// <summary>
    /// zzoega and zzatvo play in a party, bileh123 alone. zzoega played an earlier game hours ago (a mission completed in it), and in this one has a contract
    /// going and ended three (completed, failed, abandoned); zzatvo completed one; bileh123 completed one of his own.
    /// </summary>
    static (ServerFactory F, DateTime Now) Played()
    {
        var f = new ServerFactory();
        var now = DateTime.UtcNow;
        var old = now.AddHours(-5);
        f.Upload("zzoega", new GameLogWriter().At(old).Login("zzoega").Lines
            .Concat(new[] { Accept(Id(9), "Old Bounty", old.AddMinutes(5)), End(Id(9), "Old Bounty", "Complete", old.AddMinutes(30)) })
            .Append($"<{T(old.AddHours(1))}> [Notice] <SystemQuit> CSystem::Quit invoked").ToList(), T(old));

        var start = now.AddMinutes(-40);
        var going = GameLogWriter.MissionId(1);
        f.Upload("zzoega", new GameLogWriter().At(start).Login("zzoega").Minutes(1).PartyCreated().Minutes(1).MemberJoined("zzatvo").Lines
            .Concat(new[]
            {
                Accept(Id(1), "Stop Rival Attack", start.AddMinutes(5)), End(Id(1), "Stop Rival Attack", "Complete", start.AddMinutes(15)),
                Accept(Id(2), "Recover Stolen Goods", start.AddMinutes(16)), End(Id(2), "Recover Stolen Goods", "Failed", start.AddMinutes(20)),
                Accept(Id(3), "Clear the Outpost", start.AddMinutes(21)), Abandon(Id(3), "zzoega", start.AddMinutes(22)),
            })
            .Concat(new GameLogWriter().At(start.AddMinutes(30)).Accepted(going)
                .Marker(going, string.Format(GameLogWriter.HaulingContract, 2), "pickup_aaaa_0", "900000000202", GameLogWriter.PatchCity).Lines)
            .ToList(), T(start));
        f.Upload("zzatvo", new GameLogWriter().At(start.AddMinutes(1)).Login("zzatvo").Minutes(1).PartyJoined().Lines
            .Concat(new[] { Accept(Id(4), "Deliver the Package", start.AddMinutes(10)), End(Id(4), "Deliver the Package", "Complete", start.AddMinutes(25)) })
            .Concat(new GameLogWriter().At(now.AddMinutes(-2)).Inventory("zzatvo", "Nyx_Levski").Lines).ToList(),
            T(start.AddMinutes(1)));
        f.Upload("bileh123", new GameLogWriter().At(start).Login("bileh123").Lines
            .Concat(new[] { Accept(Id(5), "Bileh's Bounty", start.AddMinutes(5)), End(Id(5), "Bileh's Bounty", "Complete", start.AddMinutes(20)) })
            .Concat(new GameLogWriter().At(now.AddMinutes(-2)).Inventory("bileh123", "Nyx_Levski").Lines).ToList(), T(start));
        return (f, now);
    }

    [Fact]
    public async Task The_admin_sets_when_a_players_game_session_started()
    {
        var (f, _) = Played();
        using var _f = f;
        var admin = await f.Admin();
        await ServerFactory.Send(admin, "/admin/tools", "/admin/tools/set", ("id", "companion"), ("value", "true"));
        var zzoega = await f.Player(f.AddKey("zzoega"));
        var s = Json(await zzoega.GetStringAsync("/companion/session.json")).GetProperty("session");
        var from = s.GetProperty("from").GetDateTime().ToUniversalTime();
        Assert.False(s.GetProperty("startSet").GetBoolean());

        // a minute later (UTC: tz 0), as his Start time row sends it
        var later = from.AddMinutes(1);
        later = later.AddTicks(-(later.Ticks % TimeSpan.TicksPerMinute));
        var r = await ServerFactory.Send(admin, "/companion", "/companion/start", ("player", "zzoega"), ("from", from.ToString("O")), ("startTime", later.ToString("HH:mm")), ("tz", "0"));
        Assert.Contains("The session runs from", await r.Content.ReadAsStringAsync());
        s = Json(await zzoega.GetStringAsync("/companion/session.json")).GetProperty("session");
        Assert.True(s.GetProperty("startSet").GetBoolean());
        Assert.Equal(later, s.GetProperty("start").GetDateTime().ToUniversalTime());
        Assert.Equal(from, s.GetProperty("from").GetDateTime().ToUniversalTime());   // the same session
        Assert.Equal(later, s.GetProperty("running").GetDateTime().ToUniversalTime());   // the game going on counts from it

        Assert.Contains("Type the start time as 19:30.", await (await ServerFactory.Send(admin, "/companion", "/companion/start", ("player", "zzoega"), ("from", from.ToString("O")), ("startTime", "soon"), ("tz", "0"))).Content.ReadAsStringAsync());
        await ServerFactory.Send(admin, "/companion", "/companion/start", ("player", "zzoega"), ("from", from.ToString("O")), ("startTime", ""), ("tz", "0"));
        Assert.False(Json(await zzoega.GetStringAsync("/companion/session.json")).GetProperty("session").GetProperty("startSet").GetBoolean());   // the logs' again

        // a player cannot
        Assert.StartsWith("/login/denied", (await ServerFactory.Send(zzoega, "/companion", "/companion/start", ("player", "zzoega"), ("from", from.ToString("O")), ("startTime", "00:00"), ("tz", "0"))).Headers.Location!.PathOf());
    }

    [Fact]
    public async Task A_player_sees_himself_and_his_party_their_contracts_and_his_game_session()
    {
        var (f, _) = Played();
        using var _f = f;
        var admin = await f.Admin();
        await ServerFactory.Send(admin, "/admin/tools", "/admin/tools/set", ("id", "companion"), ("value", "true"));
        var zzoega = await f.Player(f.AddKey("zzoega"));
        Assert.Contains("companion.js", await zzoega.GetStringAsync("/companion"));

        // the map: him and his party, not bileh123; who he is, so the map follows him; his contract going on, with who has it; no library fields for him
        var map = Json(await zzoega.GetStringAsync("/companion/map.json"));
        Assert.Equal("zzoega", map.GetProperty("me").GetString());
        Assert.Equal(new[] { "zzatvo", "zzoega" }, Names(map.GetProperty("players")));
        var going = Assert.Single(map.GetProperty("missions").EnumerateArray());
        Assert.Equal(("zzoega", 1), (Assert.Single(going.GetProperty("who").EnumerateArray()).GetString(), going.GetProperty("open").GetInt32()));
        Assert.False(going.TryGetProperty("edit", out _));

        // the session: this game only (not the one five hours ago), with his own completed mission; the contracts his party ended in it, with who and how
        var res = await zzoega.GetAsync("/companion/session.json");
        var data = Json(await res.Content.ReadAsStringAsync());
        var s = data.GetProperty("session");
        Assert.NotEqual(JsonValueKind.Null, s.GetProperty("running").ValueKind);   // a game going on: the page counts its time on from its start (the clock rule)
        Assert.False(s.GetProperty("over").GetBoolean());
        Assert.Equal((0, 1), (s.GetProperty("crashes").GetInt32(), s.GetProperty("completed").GetInt32()));
        Assert.Equal(0, s.GetProperty("playedBefore").GetDouble());   // no game of the session ended yet
        var ended = data.GetProperty("completed").EnumerateArray().ToList();
        Assert.Equal(new[] { ("Deliver the Package", "zzatvo", "Completed"), ("Clear the Outpost", "zzoega", "Abandoned"), ("Recover Stolen Goods", "zzoega", "Failed"),
                ("Stop Rival Attack", "zzoega", "Completed") },   // the newest first; not bileh123's, not the old game's
            ended.Select(m => (m.GetProperty("title").GetString()!, string.Join(",", m.GetProperty("who").EnumerateArray().Select(w => w.GetString())), m.GetProperty("outcome").GetString()!)));
        Assert.All(ended, m => Assert.True(m.TryGetProperty("faction", out _) && m.TryGetProperty("legality", out _)));   // the Active Contracts' columns

        // polled with its ETag: the same data again is a 304
        var again = new HttpRequestMessage(HttpMethod.Get, "/companion/session.json");
        again.Headers.TryAddWithoutValidation("If-None-Match", "W/" + res.Headers.ETag!.Tag);   // as Cloudflare hands it back
        Assert.Equal(HttpStatusCode.NotModified, (await zzoega.SendAsync(again)).StatusCode);

        // bileh123 alone: only himself
        var bileh = await f.Player(f.AddKey("bileh123"));
        Assert.Equal(new[] { "bileh123" }, Names(Json(await bileh.GetStringAsync("/companion/map.json")).GetProperty("players")));
        Assert.Equal(new[] { "Bileh's Bounty" }, Json(await bileh.GetStringAsync("/companion/session.json")).GetProperty("completed").EnumerateArray().Select(m => m.GetProperty("title").GetString()));
    }

    [Fact]
    public async Task The_admin_sees_his_own_companion_views_a_player_s_and_fills_in_the_library_from_it()
    {
        var (f, _) = Played();
        using var _f = f;
        var admin = await f.Admin();
        Assert.Contains("companion.js", await admin.GetStringAsync("/companion"));   // only for the admin until it is shown

        // the admin's own view (View as: Admin): everybody on the map, the rest his own player's (the admin login has none here: no contracts), and the lists
        // beside the map keep to his party (Frank, 30 Oct 2026)
        var own = Json(await admin.GetStringAsync("/companion/map.json"));
        Assert.Equal(new[] { "bileh123", "zzatvo", "zzoega" }, Names(own.GetProperty("players")));
        Assert.NotEmpty(own.GetProperty("objectives").EnumerateArray());
        Assert.Empty(own.GetProperty("missions").EnumerateArray());
        Assert.Equal(new[] { "admin" }, own.GetProperty("party").EnumerateArray().Select(n => n.GetString()));
        Assert.Equal(new[] { "bileh123", "zzatvo", "zzoega" }, Names(Json(await admin.GetStringAsync("/admin/map/map.json")).GetProperty("players")));   // the Map tool: everybody

        // View as zzoega: exactly his Companion, with the library's fields of every contract for the admin
        await ServerFactory.Send(admin, "/", "/admin/pov", ("name", "zzoega"), ("back", "/companion"));
        var page = await admin.GetStringAsync("/companion");
        Assert.Contains("id=\"factions\"", page);
        Assert.Contains("__RequestVerificationToken", page);
        var map = Json(await admin.GetStringAsync("/companion/map.json"));
        Assert.Equal("zzoega", map.GetProperty("me").GetString());
        Assert.Equal(new[] { "zzatvo", "zzoega" }, Names(map.GetProperty("players")));
        Assert.False(map.TryGetProperty("party", out _));   // a player's Companion, also when the admin looks as his own player
        var edit = Assert.Single(map.GetProperty("missions").EnumerateArray()).GetProperty("edit");
        Assert.Equal("Senior | Stellar Large Haul | to Ruin Station [50/200/250/500/1000/2000/4000 Rep]", edit.GetProperty("name").GetString());
        var rival = Json(await admin.GetStringAsync("/companion/session.json")).GetProperty("completed").EnumerateArray()
            .Single(m => m.GetProperty("title").GetString() == "Stop Rival Attack");
        var key = rival.GetProperty("edit").GetProperty("key").GetString()!;
        Assert.Equal("", rival.GetProperty("reward").GetString());

        // a reward typed in its row: saved in the background through Edit Mission Info's address, and the session counts it at once
        var set = new HttpRequestMessage(HttpMethod.Post, "/admin/missions/set")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["id"] = key, ["field"] = "auec", ["value"] = "120000", ["__RequestVerificationToken"] = ServerFactory.Token(page),
            }),
        };
        set.Headers.Add("X-Autosave", "1");
        var said = Json(await (await admin.SendAsync(set)).Content.ReadAsStringAsync());
        Assert.True(said.GetProperty("ok").GetBoolean());
        var data = Json(await admin.GetStringAsync("/companion/session.json"));
        Assert.Equal("120,000", data.GetProperty("completed").EnumerateArray().Single(m => m.GetProperty("title").GetString() == "Stop Rival Attack").GetProperty("reward").GetString());
        Assert.Equal(120000, data.GetProperty("session").GetProperty("auec").GetInt64());

        // a player gets no fields, also not when he asks
        var zzoega = await f.Player(f.AddKey("zzoega"));
        await ServerFactory.Send(admin, "/admin/tools", "/admin/tools/set", ("id", "companion"), ("value", "true"));
        Assert.DoesNotContain("id=\"factions\"", await zzoega.GetStringAsync("/companion"));
        Assert.All(Json(await zzoega.GetStringAsync("/companion/session.json")).GetProperty("completed").EnumerateArray(),
            m => Assert.Equal(JsonValueKind.Null, m.GetProperty("edit").ValueKind));
    }

    [Fact]
    public async Task A_visitor_sees_the_map_and_a_note_to_log_in_once_it_is_shown()
    {
        var (f, _) = Played();
        using var _f = f;
        Assert.StartsWith("/login", (await f.Browser().GetAsync("/companion")).Headers.Location!.PathOf());   // only for the admin until it is shown
        var zzoega = await f.Player(f.AddKey("zzoega"));
        Assert.StartsWith("/login/denied", (await zzoega.GetAsync("/companion/map.json")).Headers.Location!.PathOf());

        var admin = await f.Admin();
        await ServerFactory.Send(admin, "/admin/tools", "/admin/tools/set", ("id", "companion"), ("value", "true"));
        var visitor = f.Browser();
        var page = await visitor.GetStringAsync("/companion");
        Assert.Contains("Log in", page);
        Assert.DoesNotContain("id=\"missions\"", page);
        var map = Json(await visitor.GetStringAsync("/companion/map.json"));
        Assert.Empty(map.GetProperty("players").EnumerateArray());
        Assert.Empty(map.GetProperty("missions").EnumerateArray());
        Assert.NotEmpty(map.GetProperty("systems").EnumerateArray());
        Assert.True(Json(await visitor.GetStringAsync("/companion/session.json")).GetProperty("visitor").GetBoolean());
    }

    [Fact]
    public async Task The_companion_sits_left_of_the_Hauling_Planner_and_the_Map_tool_keeps_the_map_and_the_players()
    {
        using var f = new ServerFactory();
        var admin = await f.Admin();
        var home = await admin.GetStringAsync("/");
        Assert.True(home.IndexOf("href=\"/companion\"", StringComparison.Ordinal) is var c && c >= 0 && c < home.IndexOf("href=\"/hauling\"", StringComparison.Ordinal),
            "the Companion's link comes before the Hauling Planner's");
        Assert.Equal(new[] { SiteTools.Companion, SiteTools.Hauling }, SiteTools.Bar.Take(2));

        var map = await admin.GetStringAsync("/admin/map");
        Assert.Contains("id=\"players\"", map);
        Assert.DoesNotContain("id=\"objectives\"", map);
        Assert.DoesNotContain("Active Contracts", map);
    }

    [Fact]
    public async Task A_contract_the_game_took_from_him_at_an_exit_to_the_menu_leaves_the_Active_Contracts_the_map_and_the_contracts_tracker()
    {
        // zzoega, 31 Oct 2026 (#399): a bounty going on, an exit to the menu, the game's PlayerLeft for his own id a second later and no EndMission; the bounty
        // was gone in the game when he came back twenty seconds later
        using var f = new ServerFactory();
        var now = DateTime.UtcNow;
        now = now.AddTicks(-(now.Ticks % TimeSpan.TicksPerMillisecond));   // the log writes milliseconds
        var start = now.AddMinutes(-30);
        var bounty = GameLogWriter.MissionId(7);
        var w = new GameLogWriter().At(start).Login("zzoega").Minutes(5).AcceptedAs(bounty, "Reclusive Bounty: Burt Gearhart | Extreme Risk")
            .MissionMarker(bounty, GameLogWriter.BountyContract, "obj-1", "3846").ObjectivePopup(bounty, "obj-1", "Go to Asteroid Cluster").DataBank(bounty, "obj-1", "zzoega")
            .Minutes(23).Inventory("zzoega", "Nyx_Levski");   // still playing two minutes ago
        var file = T(start);
        var first = w.Lines.ToList();
        f.Upload("zzoega", first, file);
        var admin = await f.Admin();
        await ServerFactory.Send(admin, "/admin/tools", "/admin/tools/set", ("id", "companion"), ("value", "true"));
        var zzoega = await f.Player(f.AddKey("zzoega"));
        var map = Json(await zzoega.GetStringAsync("/companion/map.json"));
        Assert.Single(map.GetProperty("missions").EnumerateArray());     // his Active Contracts
        Assert.Single(map.GetProperty("objectives").EnumerateArray());

        // the exit and the PlayerLeft, then he came back and went on playing (the Mission Library waits ten seconds of log for an accept that gives it back); the
        // tracker sends the lines the tools read, not the exit itself
        w.Seconds(30).ExitToMenu().Seconds(1.4).PlayerLeft(bounty, GameLogWriter.OwnId);
        var left = w.Now;
        w.Seconds(17).Seconds(30).Inventory("zzoega", "Nyx_Levski");
        var filter = new LineFilter();
        var rest = w.Lines.Skip(first.Count).Where(filter.Keep).ToList();
        Assert.Equal(2, rest.Count);   // the PlayerLeft and the inventory, not the exit
        Assert.True(f.Live.Uploads.Add("zzoega", file, first.Count, first.Count + rest.Count, rest, DateTime.UtcNow).Ok);
        f.Live.Refresh(DateTime.UtcNow);
        map = Json(await zzoega.GetStringAsync("/companion/map.json"));
        Assert.Empty(map.GetProperty("missions").EnumerateArray());     // not among his Active Contracts
        Assert.Empty(map.GetProperty("objectives").EnumerateArray());   // not on his map
        Assert.Empty(JsonDocument.Parse(await admin.GetStringAsync("/admin/map/map.json")).RootElement.GetProperty("objectives").EnumerateArray());   // nor the Map tool's

        // the contracts tracker: he took it, and it ended for him as an abandon (the game's rule for the scrip) when the game took it from him
        f.Services.GetRequiredService<MissionLibrary>().Collect();
        var c = f.Services.GetRequiredService<ContractTracker>().Of(new[] { bounty })[bounty];
        var holder = Assert.Single(c.Holders);
        Assert.Equal("zzoega", holder.Player);
        Assert.True(holder.Took);
        Assert.Equal(MissionOutcome.Abandon, holder.Outcome);
        Assert.Equal(left, holder.EndedUtc);
        Assert.Equal(("zzoega", MissionOutcome.Abandon), (c.Taker, c.Outcome));
    }
}
