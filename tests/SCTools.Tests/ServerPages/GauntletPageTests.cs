using System.Globalization;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using SCTools.Gauntlet;
using SCTools.Server;
using SCTools.Tests.Support;

namespace SCTools.Tests.ServerPages;

/// <summary>
/// The Gauntlet Tracker pages: only for a logged in player or the admin, everybody his own runs (his in-game party's together when he is in one), the
/// grind sessions, the Stats tab with the earnings, the top 10, the earnings per scenario and the times, the History tab with the earlier grind sessions,
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
    public async Task A_player_sees_his_runs_and_a_visitor_has_to_log_in()
    {
        using var f = new ServerFactory();
        var admin = await f.Admin();
        await ServerFactory.Send(admin, "/admin/tools", "/admin/tools/set", ("id", "gauntlet"), ("value", "true"));
        f.Upload("zzoega", Lines());

        var visitor = f.Browser();
        var denied = await visitor.GetAsync("/gauntlet");
        Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);

        var player = await f.Player(f.AddKey("zzoega"));
        var page = await player.GetStringAsync("/gauntlet");
        Assert.Contains("227,750", page);
        var stats = await player.GetStringAsync("/gauntlet/stats?scenario=7");
        Assert.Contains("<option value=\"7\" selected=\"selected\">#7</option>", stats);
        Assert.Contains("action=\"/gauntlet/stats#top10\" class=\"line\" data-autosave", stats);   // picking a scenario shows it: no Show button
        // Stats (Frank, 30 Oct 2026): the earnings (Lifetime before) top left, the top 10 top right, the earnings per scenario (Earnings ranking before) with
        // Scrip/h under them, the times by scenario last; History has the earlier grind sessions, which were on the Session tab
        Assert.Contains("<div class=\"pair\">\n  <div>\n  <h2>Earnings</h2>", stats.Replace("\r\n", "\n"));
        Assert.Contains("<div class=\"titled\">", stats);   // the scenario to pick right of the title (30 Oct 2026)
        Assert.Contains("<h2 id=\"top10\">Top 10 fastest</h2>", stats);
        Assert.True(stats.IndexOf("<div class=\"titled\">", StringComparison.Ordinal) < stats.IndexOf("<h2 id=\"top10\">", StringComparison.Ordinal));
        Assert.True(stats.IndexOf("<h2 id=\"top10\">", StringComparison.Ordinal) < stats.IndexOf("action=\"/gauntlet/stats#top10\"", StringComparison.Ordinal));
        Assert.Contains("<h2>Earnings per scenario</h2>", stats);
        Assert.Contains("<th>Scrip/h</th>", stats);
        Assert.Contains("<td class=\"right\">192.0</td>", stats);   // 16 scrip in 5 minutes
        Assert.Contains("<h2>Times by scenario</h2>", stats);
        Assert.True(stats.IndexOf("<h2>Earnings per scenario</h2>", StringComparison.Ordinal) < stats.IndexOf("<h2>Times by scenario</h2>", StringComparison.Ordinal));
        Assert.DoesNotContain("<h2>Lifetime</h2>", stats);
        Assert.DoesNotContain("Earnings ranking", stats);
        var history = await player.GetStringAsync("/gauntlet/history");
        Assert.Contains("<nav class=\"subnav area\" style=\"--tool:#d9a441\">", history);   // the tabs: a row under the bar in the tool's colour (30 Oct 2026)
        Assert.Contains("<a href=\"/gauntlet/history\" class=\"on\">History</a>", history);
        Assert.DoesNotContain("<h1>", history);
        Assert.Contains("<h2>Earlier grind sessions</h2>", history);   // his run of 30 Sep was a grind session by itself
        Assert.DoesNotContain("<h2>Earnings</h2>", history);
        Assert.DoesNotContain("<h2>Earnings</h2>", page);   // the Session tab has the game and grind sessions only, side by side
        Assert.Contains("<div class=\"pair\">", page);
        Assert.Contains("<h2>Grind session</h2>", page);
        Assert.DoesNotContain("Earlier grind sessions", page);
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
        Assert.Contains("Running since", await zzoega.GetStringAsync("/gauntlet"));
        Assert.Contains("Earlier grind sessions", await zzoega.GetStringAsync("/gauntlet/history"));   // on History since 30 Oct 2026
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
        Assert.DoesNotContain("Your party:", page);   // the tabs say nothing about whose runs they show any more (30 Oct 2026): the numbers are the party's
        Assert.Contains("250,750 aUEC", page);   // #7 (227,750) and #1 (23,000) together
        Assert.Contains("<td class=\"right\">2</td>", page);   // completed
        var stats = WebUtility.HtmlDecode(await zzoega.GetStringAsync("/gauntlet/stats?scenario=1"));
        Assert.Contains("<th>Who</th>", stats);   // the top 10 of a party says whose run it is
        Assert.Contains("<td>zzatvo</td>", stats);
        var session = WebUtility.HtmlDecode(await zzoega.GetStringAsync("/gauntlet"));
        Assert.Contains("Running since", session);   // one grind session for the party
        Assert.Contains("the first of the party's games", session);

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
        await ServerFactory.Send(admin, "/admin/missions", "/admin/missions/set", ("id", GauntletService.LibraryKey(7)), ("field", "auec"), ("value", "300000"));
        Assert.Contains("300,000", await bileh.GetStringAsync("/gauntlet/leaderboard"));   // everybody counts with them
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/admin/gauntlet")).StatusCode);   // no page of its own any more: Edit Mission Info

        // what a MG scrip is worth is on the admin's Scrip page, for every tool: #7 pays 16 scrip
        await ServerFactory.Send(admin, "/admin/scrip", "/admin/scrip/set", ("field", "mg"), ("value", "1,000"));
        await ServerFactory.Send(admin, "/admin/scrip", "/admin/scrip/set", ("field", "council"), ("value", "2500"));
        Assert.Contains("316,000", await bileh.GetStringAsync("/gauntlet/leaderboard"));   // 300,000 aUEC + 16 x 1,000
        var scrip = await admin.GetStringAsync("/admin/scrip");
        Assert.Contains("value=\"1000\"", scrip);
        Assert.Contains("value=\"2500\"", scrip);
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
}
