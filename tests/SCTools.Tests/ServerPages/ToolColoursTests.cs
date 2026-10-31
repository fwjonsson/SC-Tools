using SCTools.Server;

namespace SCTools.Tests.ServerPages;

/// <summary>
/// Every tool's tables in its colour (Frank, 31 Oct 2026): a page sets the colour of its tables' heads and its buttons (--header) from the part of the site it
/// is in; the Companion is yellow, the Gauntlet Tracker red, Session History teal and the Mission Library grey; and a # column is as narrow as the Hauling
/// Planner's on every tool.
/// </summary>
public class ToolColoursTests
{
    static string Header(string color) => "<body style=\"--header:color-mix(in srgb, " + color + " 42%, #0d1117)\">";

    [Fact]
    public async Task Every_page_has_its_tools_colour_for_its_tables()
    {
        Assert.Equal(("#d9a441", "#cf4a5a", "#3fb8a4", "#8b949e"), (SiteTools.Companion.Color, SiteTools.Gauntlet.Color, SiteTools.Session.Color, SiteTools.Missions.Color));
        Assert.NotEqual(TopBar.AdminColor, SiteTools.Gauntlet.Color);   // a red of its own, not the admin's

        using var f = new ServerFactory();
        var admin = await f.Admin();
        foreach (var tool in new[] { SiteTools.Hauling, SiteTools.Companion, SiteTools.Gauntlet, SiteTools.Session, SiteTools.Missions })
            Assert.Contains(Header(tool.Color), await admin.GetStringAsync(tool.Path!));
        Assert.Contains(Header(SiteTools.Gauntlet.Color), await admin.GetStringAsync("/gauntlet/stats"));   // every tab of it
        Assert.Contains(Header(TopBar.AdminColor), await admin.GetStringAsync("/admin/places"));   // the admin's pages in Admin's red
        Assert.Contains("<body>", await admin.GetStringAsync("/"));   // the landing page keeps the site's blue
    }

    [Fact]
    public async Task The_number_columns_are_as_narrow_as_the_hauling_planners()
    {
        using var f = new ServerFactory();
        var admin = await f.Admin();
        const string id = "11111111-1111-1111-1111-111111111111";   // one Combat Gauntlet run, so the leaderboard has a row
        f.Upload("zzoega", new[]
        {
            $"<2026-09-30T18:00:00.000Z> [Notice] <SHUDEvent_OnNotification> Added notification \"Contract Accepted:  Combat Gauntlet - Scenario #7: \" [12] to queue. New queue size: 1, MissionId: [{id}], ObjectiveId: [x]",
            $"<2026-09-30T18:05:00.000Z> [Notice] <SHUDEvent_OnNotification> Added notification \"Contract Complete:  Combat Gauntlet - Scenario #7: \" [12] to queue. New queue size: 1, MissionId: [{id}], ObjectiveId: [x]",
        });
        var board = await admin.GetStringAsync("/gauntlet/leaderboard");
        Assert.Contains("<thead><tr><th class=\"n\">#</th><th>Player</th>", board);
        Assert.Contains("<td class=\"n\">1</td>", board);
        Assert.Contains("th.n, td.n { width:36px;", await admin.GetStringAsync("/site.css"));
    }
}
