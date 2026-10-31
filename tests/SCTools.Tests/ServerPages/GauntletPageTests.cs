using System.Globalization;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using SCTools.Gauntlet;
using SCTools.Server;
using SCTools.Tests.Support;

namespace SCTools.Tests.ServerPages;

/// <summary>
/// The Gauntlet Tracker pages: only for a logged in player or the admin, everybody his own runs (his in-game party's together when he is in one), the
/// grind sessions, the Stats tab with the earnings, the top 10, the earnings per scenario and the times, the History tab with the saved grind sessions,
/// the leaderboard of everybody, and the rewards on the admin's page.
/// </summary>
public class GauntletPageTests
{
    const string Id = "11111111-1111-1111-1111-111111111111";

    static string[] Lines() => new[]
    {
        $"<2026-09-30T18:00:00.000Z> [Notice] <SHUDEvent_OnNotification> Added notification \"Contract Accepted:  Combat Gauntlet - Scenario #7: \" [12] to queue. New queue size: 1, MissionId: [{Id}], ObjectiveId: [x]",
        $"<2026-09-30T18:05:00.000Z> [Notice] <SHUDEvent_OnNotification> Added notification \"Contract Complete:  Combat Gauntlet - Scenario #7: \" [12] to queue. New queue size: 1, MissionId: [{Id}], ObjectiveId: [x]",
    };

    [Fact]
    public async Task A_player_sees_his_runs_and_a_visitor_sees_the_leaderboard_alone()
    {
        using var f = new ServerFactory();
        var admin = await f.Admin();
        await ServerFactory.Send(admin, "/admin/tools", "/admin/tools/set", ("id", "gauntlet"), ("value", "true"));
        f.Upload("zzoega", Lines());

        // a visitor sees the tool, not anybody's runs; the Leaderboard is everybody's (Frank, 30 Oct 2026)
        var visitor = f.Browser();
        var note = await visitor.GetStringAsync("/gauntlet");
        Assert.Contains("Log in", note);
        Assert.DoesNotContain("227,750", note);
        Assert.Contains("zzoega", WebUtility.HtmlDecode(await visitor.GetStringAsync("/gauntlet/leaderboard")));

        var player = await f.Player(f.AddKey("zzoega"));
        var page = await player.GetStringAsync("/gauntlet");
        Assert.Contains("227,750", page);
        var stats = await player.GetStringAsync("/gauntlet/stats?scenario=7");
        Assert.Contains("<option value=\"7\" selected=\"selected\">#7</option>", stats);
        Assert.Contains("action=\"/gauntlet/stats#top10\" class=\"line\" data-autosave", stats);   // picking a scenario shows it: no Show button
        // Stats (Frank, 30 Oct 2026): the earnings (Lifetime before) top left, the top 10 top right; since 31 Oct 2026 the Earnings/Scenario (Earnings per
        // scenario, the Earnings ranking before) with Scrip/h and the Scenario Times under the Earnings in the left half, the Top 10 Fastest alone in the right
        // half. History has the Saved Sessions (the earlier grind sessions), which were on the Session tab
        Assert.Contains("<div class=\"pair\">\n  <div>\n  <h2>Earnings</h2>", stats.Replace("\r\n", "\n"));
        Assert.Contains("<div class=\"titled\">", stats);   // the scenario to pick right of the title (30 Oct 2026)
        Assert.Contains("<h2 id=\"top10\">Top 10 Fastest</h2>", stats);
        Assert.True(stats.IndexOf("<div class=\"titled\">", StringComparison.Ordinal) < stats.IndexOf("<h2 id=\"top10\">", StringComparison.Ordinal));
        Assert.True(stats.IndexOf("<h2 id=\"top10\">", StringComparison.Ordinal) < stats.IndexOf("action=\"/gauntlet/stats#top10\"", StringComparison.Ordinal));
        Assert.Contains("<h2>Earnings/Scenario</h2>", stats);
        Assert.Contains("<th>Scrip/h</th>", stats);
        Assert.Contains("<td class=\"right\">192.0</td>", stats);   // 16 scrip in 5 minutes
        Assert.Contains("<h2>Scenario Times</h2>", stats);
        var earnings = stats.IndexOf("<h2>Earnings</h2>", StringComparison.Ordinal);
        var perScenario = stats.IndexOf("<h2>Earnings/Scenario</h2>", StringComparison.Ordinal);
        var times = stats.IndexOf("<h2>Scenario Times</h2>", StringComparison.Ordinal);
        Assert.True(earnings < perScenario && perScenario < times && times < stats.IndexOf("<div class=\"titled\">", StringComparison.Ordinal));   // the left half
        Assert.DoesNotContain("Earnings per scenario", stats);
        Assert.DoesNotContain("Times by scenario", stats);
        Assert.DoesNotContain("<h2>Lifetime</h2>", stats);
        Assert.DoesNotContain("Earnings ranking", stats);
        var history = await player.GetStringAsync("/gauntlet/history");
        Assert.Contains("<nav class=\"subnav area\" style=\"--tool:#e0734a\">", history);   // the tabs: a row under the bar in the tool's colour (30 Oct 2026; orange since 31 Oct)
        Assert.Contains("<a href=\"/gauntlet/history\" class=\"on\">History</a>", history);
        Assert.DoesNotContain("<h1>", history);
        Assert.Contains("<h2>Saved Sessions</h2>", history);   // his run of 30 Sep was a grind session by itself ("Earlier grind sessions" until 31 Oct 2026)
        Assert.Contains("<th class=\"n\">#</th><th>Started</th>", history);   // a # column as the other tools' tables (31 Oct 2026)
        Assert.Contains("<td class=\"n\">1</td>", history);
        Assert.DoesNotContain("<h2>Earnings</h2>", history);
        Assert.DoesNotContain("<h2>Earnings</h2>", page);   // the Session tab has the game and grind sessions only, in a column beside the Players (31 Oct 2026)
        Assert.Contains("gauntletsessions", page);
        Assert.Contains("<h2>Grind Session</h2>", page);
        Assert.DoesNotContain("Saved Sessions", page);
        var old = await player.GetAsync("/gauntlet/ranking");   // the old address
        Assert.Equal(HttpStatusCode.Redirect, old.StatusCode);
        Assert.Equal("/gauntlet/stats", old.Headers.Location?.OriginalString);
        Assert.DoesNotContain("Your own runs (you are not in a party now", page);
        Assert.Contains("<h2>All-time leaderboard</h2>", await player.GetStringAsync("/gauntlet/leaderboard"));   // the title over the ranking (30 Oct 2026)
        Assert.DoesNotContain("Start grind session", page);   // no buttons: they start and stop by themselves
        Assert.DoesNotContain("name=\"player\"", page);   // his own page only: nobody else's to pick
    }

    [Fact]
    public async Task A_grind_session_starts_when_a_scenario_is_accepted_and_ends_an_hour_after_the_last_one()
    {
        using var f = new ServerFactory();
        var admin = await f.Admin();
        await ServerFactory.Send(admin, "/admin/tools", "/admin/tools/set", ("id", "gauntlet"), ("value", "true"));
        var now = DateTime.UtcNow;
        string T(DateTime t) => t.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", System.Globalization.CultureInfo.InvariantCulture);
        string Accept(string id, DateTime t) => $"<{T(t)}> [Notice] <SHUDEvent_OnNotification> Added notification \"Contract Accepted:  Combat Gauntlet - Scenario #7: \" [12] to queue. New queue size: 1, MissionId: [{id}], ObjectiveId: [x]";
        string Done(string id, DateTime t) => $"<{T(t)}> [Notice] <SHUDEvent_OnNotification> Added notification \"Contract Complete:  Combat Gauntlet - Scenario #7: \" [12] to queue. New queue size: 1, MissionId: [{id}], ObjectiveId: [x]";
        f.Upload("zzoega", new[]
        {
            Accept("21111111-1111-1111-1111-111111111111", now.AddHours(-4)), Done("21111111-1111-1111-1111-111111111111", now.AddHours(-4).AddMinutes(6)),   // a grind of its own
            Accept("31111111-1111-1111-1111-111111111111", now.AddMinutes(-30)), Done("31111111-1111-1111-1111-111111111111", now.AddMinutes(-24)),
            Accept("41111111-1111-1111-1111-111111111111", now.AddMinutes(-3)),   // the one he is in now
        });

        var gauntlet = f.Services.GetRequiredService<GauntletService>();
        gauntlet.Collect();   // the runs into the database, as the server does every few seconds
        var sessions = gauntlet.GrindSessions("zzoega", now);
        Assert.Equal(2, sessions.Count);
        Assert.Null(sessions[0].Stop);   // newest first: it runs
        Assert.Equal(now.AddMinutes(-30), sessions[0].Start, TimeSpan.FromSeconds(1));
        Assert.Equal(now.AddHours(-4).AddMinutes(11), sessions[1].Stop!.Value, TimeSpan.FromSeconds(1));   // 5 minutes after the last thing that happened

        var zzoega = await f.Player(f.AddKey("zzoega"));
        // the one going on: its Start time row under the Grind Session title (the "Running since" line over it went on 31 Oct 2026)
        var running = await zzoega.GetStringAsync("/gauntlet");
        Assert.Contains("<th>Start time (UTC)</th>", running[running.IndexOf("<h2>Grind Session</h2>", StringComparison.Ordinal)..]);
        Assert.DoesNotContain("Running since", running);
        Assert.Contains("Saved Sessions", await zzoega.GetStringAsync("/gauntlet/history"));   // on History since 30 Oct 2026

        // the admin's Start time row (Frank, 31 Oct 2026): in UTC, as the page's times; the grind session counts from it
        Assert.DoesNotContain("action=\"/gauntlet/start\"", await zzoega.GetStringAsync("/gauntlet"));   // not a player's
        await ServerFactory.Send(admin, "/", "/admin/pov", ("name", "zzoega"), ("back", "/gauntlet"));
        Assert.Contains("action=\"/gauntlet/start\"", await admin.GetStringAsync("/gauntlet"));
        var later = sessions[0].Start.AddMinutes(1);
        later = later.AddTicks(-(later.Ticks % TimeSpan.TicksPerMinute));
        await ServerFactory.Send(admin, "/gauntlet", "/gauntlet/start", ("kind", "grind"), ("from", sessions[0].Start.ToString("O")), ("startTime", later.ToString("HH:mm")));
        var moved = await zzoega.GetStringAsync("/gauntlet");
        var grindStart = moved[moved.IndexOf("<th>Start time (UTC)</th>", moved.IndexOf("<h2>Grind Session</h2>", StringComparison.Ordinal), StringComparison.Ordinal)..];
        Assert.Contains(later.ToString("HH:mm", CultureInfo.InvariantCulture), grindStart[..grindStart.IndexOf("</tr>", StringComparison.Ordinal)]);
    }

    [Fact]
    public async Task The_tabs_show_the_party_s_runs_together_when_the_viewer_is_in_a_party()
    {
        using var f = new ServerFactory();
        var admin = await f.Admin();
        await ServerFactory.Send(admin, "/admin/tools", "/admin/tools/set", ("id", "gauntlet"), ("value", "true"));
        var now = DateTime.UtcNow;
        string T(DateTime t) => t.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
        string Accept(int scenario, string id, DateTime t) => $"<{T(t)}> [Notice] <SHUDEvent_OnNotification> Added notification \"Contract Accepted:  Combat Gauntlet - Scenario #{scenario}: \" [12] to queue. New queue size: 1, MissionId: [{id}], ObjectiveId: [x]";
        string Done(int scenario, string id, DateTime t) => $"<{T(t)}> [Notice] <SHUDEvent_OnNotification> Added notification \"Contract Complete:  Combat Gauntlet - Scenario #{scenario}: \" [12] to queue. New queue size: 1, MissionId: [{id}], ObjectiveId: [x]";
        // zzoega made a party and zzatvo joined it; each ran a scenario (#7 in 5 minutes, #1 in 4 minutes); bileh123 plays alone and ran #2
        var zzoegaLog = new GameLogWriter().At(now.AddMinutes(-30)).Login("zzoega").Minutes(1).PartyCreated().Minutes(1).MemberJoined("zzatvo").Lines
            .Concat(new[] { Accept(7, "71111111-1111-1111-1111-111111111111", now.AddMinutes(-12)), Done(7, "71111111-1111-1111-1111-111111111111", now.AddMinutes(-7)) }).ToList();
        var zzatvoLog = new GameLogWriter().At(now.AddMinutes(-29)).Login("zzatvo").Minutes(1).PartyJoined().Lines
            .Concat(new[] { Accept(1, "11111111-2222-1111-1111-111111111111", now.AddMinutes(-10)), Done(1, "11111111-2222-1111-1111-111111111111", now.AddMinutes(-6)) }).ToList();
        var bilehLog = new GameLogWriter().At(now.AddMinutes(-25)).Login("bileh123").Lines
            .Concat(new[] { Accept(2, "21111111-3333-1111-1111-111111111111", now.AddMinutes(-12)), Done(2, "21111111-3333-1111-1111-111111111111", now.AddMinutes(-7)) }).ToList();
        // the files are named by the time the game started, as the trackers name them: the game runs from before the party lines
        f.Upload("zzoega", zzoegaLog, T(now.AddMinutes(-30)));
        f.Upload("zzatvo", zzatvoLog, T(now.AddMinutes(-29)));
        f.Upload("bileh123", bilehLog, T(now.AddMinutes(-25)));

        var zzoega = await f.Player(f.AddKey("zzoega"));
        var page = WebUtility.HtmlDecode(await zzoega.GetStringAsync("/gauntlet/stats"));   // the Earnings are on Stats since 30 Oct 2026
        Assert.DoesNotContain("Your party:", page);   // the tabs say nothing about whose runs they show any more (30 Oct 2026)
        // the whole Stats page is personal (Frank, 30 Oct 2026): his #7 alone, not zzatvo's #1 with it
        Assert.Contains("227,750 aUEC", page);
        Assert.DoesNotContain("250,750 aUEC", page);
        var stats = WebUtility.HtmlDecode(await zzoega.GetStringAsync("/gauntlet/stats?scenario=1"));
        Assert.DoesNotContain("<th>Who</th>", stats);   // his own top 10: no Who column
        Assert.DoesNotContain("<td>zzatvo</td>", stats);
        var session = WebUtility.HtmlDecode(await zzoega.GetStringAsync("/gauntlet"));
        Assert.Contains("<th>Start time (UTC)</th>", session[session.IndexOf("<h2>Grind Session</h2>", StringComparison.Ordinal)..]);   // one grind session for the party
        Assert.Contains("when your party accepts a scenario and goes on while your party plays them", session);   // the note under the grind session says whose

        var bileh = await f.Player(f.AddKey("bileh123"));
        var his = WebUtility.HtmlDecode(await bileh.GetStringAsync("/gauntlet/stats"));   // the Earnings are on Stats since 30 Oct 2026
        Assert.DoesNotContain("Your own runs (you are not in a party now", his);
        Assert.Contains("36,000 aUEC", his);   // #2 alone
        Assert.DoesNotContain("250,750", his);
        Assert.DoesNotContain("<th>Who</th>", WebUtility.HtmlDecode(await bileh.GetStringAsync("/gauntlet/stats?scenario=2")));
        var board = WebUtility.HtmlDecode(await bileh.GetStringAsync("/gauntlet/leaderboard"));   // the leaderboard is everybody's, one row each
        Assert.Contains("<td>zzoega</td>", board);
        Assert.Contains("<td>zzatvo</td>", board);
        Assert.Contains("<td>bileh123</td>", board);
    }

    [Fact]
    public async Task The_leaderboard_has_everybody_and_the_admin_sets_the_rewards_for_everybody()
    {
        using var f = new ServerFactory();
        var admin = await f.Admin();
        await ServerFactory.Send(admin, "/admin/tools", "/admin/tools/set", ("id", "gauntlet"), ("value", "true"));
        f.Upload("zzoega", Lines());
        f.Upload("bileh123", Lines().Select(l => l.Replace(Id, "22222222-2222-2222-2222-222222222222").Replace("#7", "#1")).ToArray());

        var bileh = await f.Player(f.AddKey("bileh123"));
        var board = await bileh.GetStringAsync("/gauntlet/leaderboard");
        Assert.True(board.IndexOf("<td>zzoega</td>", StringComparison.Ordinal) < board.IndexOf("<td>bileh123</td>", StringComparison.Ordinal));   // the most earned first
        Assert.Contains("<tr class=\"me\">", board);   // his own row stands out
        Assert.Contains("227,750", board);

        // the rewards are in the Mission Library (the admin's Gauntlet page shows them), the same for everybody
        await ServerFactory.Send(admin, "/missions", "/admin/missions/set", ("id", GauntletService.LibraryKey(7)), ("field", "auec"), ("value", "300000"));
        Assert.Contains("300,000", await bileh.GetStringAsync("/gauntlet/leaderboard"));   // everybody counts with them
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/admin/gauntlet")).StatusCode);   // no page of its own any more: Edit Mission Info

        // what a MG scrip is worth is on the admin's Scrip Value page, for every tool: #7 pays 16 scrip
        await ServerFactory.Send(admin, "/admin/scrip", "/admin/scrip/set", ("field", "mg"), ("value", "1,000"));
        await ServerFactory.Send(admin, "/admin/scrip", "/admin/scrip/set", ("field", "council"), ("value", "2500"));
        Assert.Contains("316,000", await bileh.GetStringAsync("/gauntlet/leaderboard"));   // 300,000 aUEC + 16 x 1,000
        var scrip = await admin.GetStringAsync("/admin/scrip");
        Assert.Contains("value=\"1000\"", scrip);
        Assert.Contains("value=\"2500\"", scrip);
        Assert.Contains("<a href=\"/admin/scrip\" class=\"on\">Scrip Value</a>", scrip);   // "Scrip" until 31 Oct 2026
        Assert.Contains("<h1>Scrip Value</h1>", scrip);
        var bad = await ServerFactory.Send(admin, "/admin/scrip", "/admin/scrip/set", ("field", "mg"), ("value", "cheap"));
        Assert.Contains("Not saved", await admin.GetStringAsync("/admin/scrip"));
        Assert.Equal(HttpStatusCode.Redirect, (await bileh.GetAsync("/admin/scrip")).StatusCode);   // the admin's
    }

    [Fact]
    public async Task The_page_is_only_for_the_admin_until_the_tool_is_shown()
    {
        using var f = new ServerFactory();
        var player = await f.Player(f.AddKey("zzoega"));
        var r = await player.GetAsync("/gauntlet");
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await (await f.Admin()).GetAsync("/gauntlet")).StatusCode);
    }

    [Fact]
    public async Task The_session_tab_has_the_companions_map_with_the_viewer_and_his_party()
    {
        using var f = new ServerFactory();
        var admin = await f.Admin();
        // the map over the game session (Frank, 31 Oct 2026): the systems as the engine's buttons in its corner, the players beside it, its own feed, the
        // Companion's size (gauntletmap)
        var page = (await admin.GetStringAsync("/gauntlet/session")).Replace("\r\n", "\n");
        Assert.Contains("<div class=\"maptool gauntletmap\">\n    <div class=\"mapbox\">\n      <div id=\"map\" data-src=\"/gauntlet/map.json\"></div>", page);
        Assert.DoesNotContain("id=\"systems\"", page);
        Assert.Contains("map.js", page);
        Assert.True(page.IndexOf("id=\"map\"", StringComparison.Ordinal) < page.IndexOf("Grind Session", StringComparison.Ordinal));
        Assert.DoesNotContain("id=\"map\"", await admin.GetStringAsync("/gauntlet/stats"));   // the Session tab's alone
        // beside the Players, a column with the Game Session and under it the Grind Session (Frank, 31 Oct 2026), both tables there with nothing counted
        // yet; what a grind session is under its table, said right; no note under the Game Session
        var players = page.IndexOf("<h2>Players</h2>", StringComparison.Ordinal);
        var game = page.IndexOf("<h2>Game Session</h2>", StringComparison.Ordinal);
        var grind = page.IndexOf("<h2>Grind Session</h2>", StringComparison.Ordinal);
        Assert.True(players > 0 && game > players && grind > game);
        Assert.Contains("mapside objectives gauntletsessions", page[players..game]);
        Assert.Contains("<th>Completed</th>", page[grind..]);   // the Grind Session's table, though no grind session is going on
        // nothing between the title and the table (Frank, 31 Oct 2026: "No grind session now." was there), and the two tables' row titles as wide as each other
        Assert.StartsWith("<table", page[(grind + "<h2>Grind Session</h2>".Length)..].TrimStart());
        Assert.DoesNotContain("No grind session now.", page);
        Assert.Contains(".gauntletsessions table { table-layout:fixed; }", await admin.GetStringAsync("/site.css"));
        var note = page.IndexOf("A grind session starts by itself when you accept a scenario and goes on while you play them.", StringComparison.Ordinal);
        Assert.True(note > page.IndexOf("<th>Completed</th>", grind, StringComparison.Ordinal));
        Assert.DoesNotContain("Counted passively", page);
        Assert.DoesNotContain("This game session", page);
        // no runs yet: said under the map on this tab, so the map, the Players and the Game Session start level at the top (31 Oct 2026); over the other tabs
        Assert.True(page.IndexOf("No gauntlet missions seen yet", StringComparison.Ordinal) > page.IndexOf("<h2>Grind Session</h2>", StringComparison.Ordinal));
        var stats = await admin.GetStringAsync("/gauntlet/stats");
        Assert.True(stats.IndexOf("No gauntlet missions seen yet", StringComparison.Ordinal) < stats.IndexOf("<h2>", StringComparison.Ordinal));

        // its feed is the Companion's map: zzoega, who is playing, is on the admin's, and on his own; a player in no party with him does not see him
        var now = DateTime.UtcNow;
        var his = new GameLogWriter().At(now.AddMinutes(-2)).Login("zzoega").Minutes(1).Inventory("zzoega", "Nyx_Levski");
        Assert.True(f.Live.Uploads.Add("zzoega", now.AddMinutes(-2).ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture), 0, 1000, his.Lines.ToList(), now).Ok);
        static string[] Names(string json) => System.Text.Json.JsonDocument.Parse(json).RootElement.GetProperty("players").EnumerateArray()
            .Select(p => p.GetProperty("name").GetString()!).ToArray();
        Assert.Equal(new[] { "zzoega" }, Names(await admin.GetStringAsync("/gauntlet/map.json")));
        await ServerFactory.Send(admin, "/admin/tools", "/admin/tools/set", ("id", "gauntlet"), ("value", "true"));   // the players see the tool
        Assert.Equal(new[] { "zzoega" }, Names(await (await f.Player(f.AddKey("zzoega"))).GetStringAsync("/gauntlet/map.json")));
        Assert.Empty(Names(await (await f.Player(f.AddKey("bileh123"))).GetStringAsync("/gauntlet/map.json")));
    }
}
