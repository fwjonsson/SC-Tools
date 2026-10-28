using System.Net;
using SCTools.Gauntlet;
using SCTools.Server;

namespace SCTools.Tests.ServerPages;

/// <summary>
/// The Gauntlet Tracker pages: only for a logged in player or the admin, everybody his own runs and the grind session buttons, the leaderboard of everybody,
/// and the rewards on the admin's page.
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
        var ranking = await player.GetStringAsync("/gauntlet/ranking");
        Assert.Contains("Earnings ranking", ranking);
        Assert.Contains("Start grind session", page);
        Assert.DoesNotContain("name=\"player\"", page);   // his own page only: nobody else's to pick
        Assert.Equal(HttpStatusCode.Redirect, (await player.GetAsync("/admin/gauntlet")).StatusCode);   // the rewards are the admin's
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
        await ServerFactory.Send(admin, "/missions", "/missions/set", ("id", GauntletService.LibraryKey(7)), ("field", "auec"), ("value", "300000"));
        Assert.Contains("300,000", await bileh.GetStringAsync("/gauntlet/leaderboard"));   // everybody counts with them
        Assert.Contains("300,000", await admin.GetStringAsync("/admin/gauntlet"));

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
