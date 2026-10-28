using System.Net;
using HaulingTracker.Server;

namespace HaulingTracker.Tests.ServerPages;

/// <summary>
/// The Tools page: the admin picks which tools everybody sees on the front page. The others are only for him: their cards, and their pages (the login for
/// a visitor, "not for you" for a player).
/// </summary>
public class ToolsPageTests
{
    static string Card(string name) => "<span class=\"tool-name\">" + name + "</span>";

    [Fact]
    public async Task Only_the_admin_sees_the_tools_he_has_not_shown_to_everybody()
    {
        using var f = new ServerFactory();
        var visitor = f.Browser();
        var home = await visitor.GetStringAsync("/");
        Assert.Contains(Card("Hauling Tracker"), home);   // the hauling tracker is shown, the tools still being made are not
        Assert.DoesNotContain(Card("Session"), home);
        Assert.DoesNotContain(Card("Mission Library"), home);
        Assert.DoesNotContain(Card("Gauntlet Tracker"), home);

        var admin = await f.Admin();
        home = await admin.GetStringAsync("/");
        Assert.All(SiteTools.All, t => Assert.Contains(Card(t.Name), home));
        Assert.Equal(3, home.Split("Only you see this").Length - 1);   // the three hidden ones say so
        var page = await admin.GetStringAsync("/admin/tools");
        Assert.Contains("value=\"gauntlet\"", page);

        // the Gauntlet Tracker for everybody, the hauling tracker only for the admin
        await ServerFactory.Send(admin, "/admin/tools", "/admin/tools/set", ("id", "gauntlet"), ("value", "true"));
        var r = await ServerFactory.Send(admin, "/admin/tools", "/admin/tools/set", ("id", "hauling"));   // the tick taken off: no value
        Assert.Equal("/admin/tools", r.Headers.Location?.OriginalString);
        Assert.Contains("Hauling Tracker is only for you now.", await admin.GetStringAsync("/admin/tools"));
        var kept = f.Store.ToolSettings();   // kept in the database
        Assert.Equal(2, kept.Count);
        Assert.True(kept["gauntlet"]);
        Assert.False(kept["hauling"]);

        home = await visitor.GetStringAsync("/");
        Assert.Contains(Card("Gauntlet Tracker"), home);
        Assert.DoesNotContain(Card("Hauling Tracker"), home);
        Assert.DoesNotContain("href=\"/hauling\"", home);   // not in the menu either
        var hauling = await visitor.GetAsync("/hauling");
        Assert.Equal(HttpStatusCode.Redirect, hauling.StatusCode);
        Assert.StartsWith("/login", hauling.Headers.Location!.PathOf());
        Assert.Equal(HttpStatusCode.Forbidden, (await visitor.GetAsync("/data.json")).StatusCode);

        var player = await f.Player(f.AddKey("zzoega"));
        var denied = await player.GetAsync("/hauling");
        Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);
        Assert.StartsWith("/login/denied", denied.Headers.Location!.PathOf());
        Assert.Equal(HttpStatusCode.OK, (await player.GetAsync("/me")).StatusCode);   // his own page stays

        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/hauling")).StatusCode);   // the admin still sees it
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/data.json")).StatusCode);
        Assert.Contains("Only you see this", await admin.GetStringAsync("/"));
    }

    [Fact]
    public async Task The_tools_page_is_for_the_admin_only()
    {
        using var f = new ServerFactory();
        var r = await f.Browser().GetAsync("/admin/tools");
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.StartsWith("/login", r.Headers.Location!.PathOf());
        var player = await f.Player(f.AddKey("zzoega"));
        Assert.StartsWith("/login/denied", (await player.GetAsync("/admin/tools")).Headers.Location!.PathOf());
    }
}
