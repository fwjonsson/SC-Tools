using System.Net;

namespace SCTools.Tests.ServerPages;

/// <summary>The admin looks at the site as a player: the pick in the top bar (View as), the sct-pov cookie, and the pages that follow it.</summary>
public class PointOfViewTests
{
    [Fact]
    public async Task The_admin_picks_a_player_in_the_top_bar_and_the_pages_show_that_players_view()
    {
        using var f = new ServerFactory();
        f.AddKey("zzoega");
        var admin = await f.Admin();
        var home = await admin.GetStringAsync("/");
        Assert.Contains("<form method=\"post\" action=\"/admin/pov\" class=\"inline pov\">", home);
        Assert.Contains("<option value=\"zzoega\">zzoega</option>", home);
        Assert.Contains("data-me=\"admin\" data-admin=\"1\"", await admin.GetStringAsync("/hauling"));   // his own view: the admin's, with his extras

        var res = await ServerFactory.Send(admin, "/", "/admin/pov", ("name", "zzoega"), ("back", "/hauling"));
        Assert.Equal("/hauling", res.Headers.Location!.OriginalString);
        var page = await admin.GetStringAsync("/hauling");
        Assert.Contains("<div class=\"hauling\" data-me=\"zzoega\">", page);   // zzoega's view: his party's session and his own trips, no admin extras
        Assert.DoesNotContain("id=\"partyPick\"", page);   // the planner's own pick of a party went with it
        Assert.Contains("<option value=\"zzoega\" selected>zzoega</option>", page);   // the pick says so
        Assert.Contains(">Admin</a>", page);   // still the admin
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/gauntlet")).StatusCode);   // the Gauntlet Tracker as zzoega

        await ServerFactory.Send(admin, "/", "/admin/pov", ("name", ""), ("back", "/"));   // back to his own view
        Assert.Contains("data-admin=\"1\"", await admin.GetStringAsync("/hauling"));

        var player = await f.Player(f.AddKey("zzatvo"));   // not for a player: no pick, and the form is the admin's
        Assert.DoesNotContain("action=\"/admin/pov\"", await player.GetStringAsync("/"));
        Assert.StartsWith("/login/denied", (await ServerFactory.Send(player, "/", "/admin/pov", ("name", "zzoega"))).Headers.Location!.PathOf());
    }
}
