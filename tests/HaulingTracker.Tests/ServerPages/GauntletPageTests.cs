using System.Net;

namespace HaulingTracker.Tests.ServerPages;

/// <summary>The Gauntlet Tracker pages: only for a logged in player or the admin, his own runs, the grind session buttons and the options.</summary>
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
        Assert.Contains("MG scrip", await player.GetStringAsync("/gauntlet/options"));
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
