using System.Net;
using SCTools.Server;

namespace SCTools.Tests.ServerPages;

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
        Assert.Contains(Card("Hauling Planner"), home);   // the hauling tracker is shown, the tools still being made are not
        Assert.DoesNotContain(Card("Session"), home);
        Assert.DoesNotContain(Card("Mission Library"), home);
        Assert.DoesNotContain(Card("Gauntlet Tracker"), home);

        var admin = await f.Admin();
        home = await admin.GetStringAsync("/");
        Assert.All(SiteTools.All, t => Assert.Contains(Card(t.Name), home));
        Assert.Equal(5, home.Split("Only you see this").Length - 1);   // the five hidden ones say so
        var page = await admin.GetStringAsync("/admin/tools");
        Assert.Contains("value=\"gauntlet\"", page);

        // the Gauntlet Tracker for everybody, the hauling tracker only for the admin
        await ServerFactory.Send(admin, "/admin/tools", "/admin/tools/set", ("id", "gauntlet"), ("value", "true"));
        var r = await ServerFactory.Send(admin, "/admin/tools", "/admin/tools/set", ("id", "hauling"));   // the tick taken off: no value
        Assert.Equal("/admin/tools", r.Headers.Location?.OriginalString);
        Assert.Contains("Hauling Planner is only for you now.", await admin.GetStringAsync("/admin/tools"));
        var kept = f.Store.ToolSettings();   // kept in the database
        Assert.Equal(2, kept.Count);
        Assert.True(kept["gauntlet"]);
        Assert.False(kept["hauling"]);

        home = await visitor.GetStringAsync("/");
        Assert.Contains(Card("Gauntlet Tracker"), home);
        Assert.DoesNotContain(Card("Hauling Planner"), home);
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
    public async Task Every_page_has_the_bar_of_the_site_with_the_tools_and_only_SC_Tools_goes_back_to_the_cards()
    {
        using var f = new ServerFactory();
        var visitor = f.Browser();
        var live = await visitor.GetStringAsync("/hauling");
        Assert.Contains("<body class=\"tool\">\n<header class=\"topbar\">", live);   // the hauling tracker's own page has the bar at the top
        Assert.Contains("<a class=\"brand\" href=\"/\">SC Tools</a>", live);   // back to the cards
        Assert.Contains("<a href=\"/hauling\" class=\"on\">Hauling Planner</a>", live);   // the tool you are in
        Assert.Contains("href=\"/topbar.css?v=", live);   // with its version, so a new look is never an old one from a cache
        Assert.Contains("<a href=\"/login\">Log in</a>", live);
        Assert.DoesNotContain("position:absolute", live);   // the old links in the corner are gone
        Assert.Equal(HttpStatusCode.OK, (await visitor.GetAsync("/topbar.css")).StatusCode);

        var home = await visitor.GetStringAsync("/");
        Assert.Contains("<a href=\"/hauling\">Hauling Planner</a>", home);   // the same bar on the landing page, no tool marked

        var admin = await f.Admin();
        live = await admin.GetStringAsync("/hauling");
        Assert.Contains("<a href=\"/admin\">Admin</a>", live);
        var party = await admin.GetStringAsync("/admin/party");
        Assert.Contains("<nav class=\"subnav\">", party);   // the admin's pages under the bar, the one he is on marked
        Assert.Contains("<a href=\"/admin/party\" class=\"on\">Party</a>", party);
        Assert.Contains("<a href=\"/admin\">Session</a>", party);
        Assert.DoesNotContain("<nav class=\"subnav\">", await admin.GetStringAsync("/"));

        // Log out from the hauling tracker's page: its form has the token
        var r = await admin.PostAsync("/logout", new FormUrlEncodedContent(new[] { KeyValuePair.Create("__RequestVerificationToken", ServerFactory.Token(live)) }));
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.StartsWith("/login", (await admin.GetAsync("/admin")).Headers.Location!.PathOf());
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
