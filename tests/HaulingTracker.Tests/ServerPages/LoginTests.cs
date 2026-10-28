using System.Net;
using System.Text.RegularExpressions;
using HaulingTracker.Server;
using HaulingTracker.Server.Accounts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HaulingTracker.Tests.ServerPages;

/// <summary>
/// The logins of the web app: anybody watches; the admin logs in with his password and makes and takes back tracker keys; a player is logged in by his
/// tracker and changes only his own location, colour, ship and quantum drive.
/// </summary>
public class LoginTests
{
    static string StationNotRuin => PyroDistances.Points.First(p => p.Id != PyroDistances.Ruin).Id;

    [Fact]
    public async Task Watching_needs_no_login_and_changing_things_does()
    {
        using var f = new ServerFactory();
        var c = f.Browser();
        var page = await c.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var home = await page.Content.ReadAsStringAsync();
        Assert.Contains("href=\"/hauling\"", home);   // the landing page: a card for every tool
        Assert.Contains("href=\"/login\"", home);
        var live = await c.GetStringAsync("/hauling");
        Assert.Contains("href=\"/login\"", live);   // the live page has the link to log in, and back to the tools
        Assert.Contains("href=\"/\"", live);
        var old = await c.GetAsync("http://haulingtracker.fwjonsson.dev/");   // the old address (the trackers use it): straight to the hauling tracker
        Assert.Equal(HttpStatusCode.Redirect, old.StatusCode);
        Assert.Equal("/hauling", old.Headers.Location!.OriginalString);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/data.json")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/site.css")).StatusCode);

        foreach (var url in new[] { "/me", "/admin/keys", "/account", "/account/password" })
        {
            var r = await c.GetAsync(url);
            Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
            Assert.StartsWith("/login", r.Headers.Location!.PathOf());
        }
    }

    [Fact]
    public async Task The_admin_logs_in_with_his_password_and_makes_and_takes_back_a_key()
    {
        using var f = new ServerFactory();
        var wrong = await ServerFactory.Send(f.Browser(), "/login", "/login", ("UserName", ServerFactory.AdminName), ("Password", "not the password"));
        Assert.Equal(HttpStatusCode.OK, wrong.StatusCode);
        Assert.Contains("Wrong name or password.", await wrong.Content.ReadAsStringAsync());

        var admin = await f.Admin();
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/admin/keys")).StatusCode);

        // a name that cannot be a name in the game: no key
        var bad = await ServerFactory.Send(admin, "/admin/keys", "/admin/keys", ("New.Player", "not a name!"));
        Assert.Contains("only letters, digits", await bad.Content.ReadAsStringAsync());
        Assert.Empty(f.Store.TrackerKeys());

        // a key for Pilot_B: shown once, and his tracker can upload with it
        var made = await ServerFactory.Send(admin, "/admin/keys", "/admin/keys", ("New.Player", "Pilot_B"));
        var html = await made.Content.ReadAsStringAsync();
        var key = Regex.Match(html, "[0-9A-F]{48}").Value;
        Assert.Equal(48, key.Length);
        var me = await f.Tracker(key).GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.Contains("Pilot_B", await me.Content.ReadAsStringAsync());
        Assert.DoesNotContain(key, await admin.GetStringAsync("/admin/keys"));   // never shown again

        // taken back: the tracker is refused
        var row = f.Store.TrackerKeys().Single();
        var removed = await ServerFactory.Send(admin, "/admin/keys", "/admin/keys/" + row.Id + "/remove");
        Assert.Equal(HttpStatusCode.Redirect, removed.StatusCode);
        Assert.Empty(f.Store.TrackerKeys());
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.Tracker(key).GetAsync("/api/me")).StatusCode);
    }

    [Fact]
    public async Task A_tracker_logs_its_player_in_once_and_he_changes_only_his_own_settings()
    {
        using var f = new ServerFactory();
        var key = f.AddKey("zzatvo");
        var link = await f.LoginLink(key);
        var player = f.Browser();
        Assert.Equal("/me", (await player.GetAsync(link)).Headers.Location?.OriginalString);
        Assert.Contains("used already", await f.Browser().GetStringAsync(link));   // a link works once

        var page = await player.GetStringAsync("/me");
        Assert.Contains("ZZATVO", page);
        Assert.Contains("Automatic: ", page);
        Assert.Contains("href=\"/me\"", await player.GetStringAsync("/hauling"));   // the live page links to his page

        var station = StationNotRuin;
        var ship = Ships.All[0];
        var drive = QuantumDrives.All[0];
        var saved = await ServerFactory.Send(player, "/me", "/me",
            ("Location", station), ("Color", "#112233"), ("Ship", ship.Label), ("Drive", drive.Label));
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        Assert.Contains("Saved: location, colour, ship, quantum drive.", await player.GetStringAsync("/me"));

        var data = f.Live.Read(t => t.State.Data);
        f.Live.Read(t =>
        {
            Assert.Equal(station, t.Whereabouts.LocationOf("zzatvo").Id);
            Assert.Equal(unchecked((int)0xFF112233), t.State.Data.WorkerColors["zzatvo"]);
            Assert.Equal(ship.Name, t.State.Data.Ship["zzatvo"]);
            Assert.Equal(drive.Key, t.State.Data.QuantumDrive["zzatvo"]);
            Assert.False(t.State.Data.WorkerColors.ContainsKey("zzoega"));   // nobody else's
            return 0;
        });

        // and it is in the database
        lock (f.Live.Uploads.Sync) f.Store.Flush(data);
        Assert.Equal(unchecked((int)0xFF112233), f.Store.Load().WorkerColors["zzatvo"]);

        // the same again: nothing changes; the admin pages are not his
        await ServerFactory.Send(player, "/me", "/me", ("Location", station), ("Color", "#112233"), ("Ship", ship.Label), ("Drive", drive.Label));
        Assert.Contains("Nothing changed.", await player.GetStringAsync("/me"));
        var keys = await player.GetAsync("/admin/keys");
        Assert.Equal(HttpStatusCode.Redirect, keys.StatusCode);
        Assert.StartsWith("/login/denied", keys.Headers.Location!.PathOf());
    }

    [Fact]
    public async Task Something_that_is_not_a_choice_is_not_saved()
    {
        using var f = new ServerFactory();
        var player = await f.Player(f.AddKey("zzoega"));
        await ServerFactory.Send(player, "/me", "/me", ("Location", "no such station"), ("Color", "#112233"), ("Ship", ""), ("Drive", ""));
        Assert.Contains("Nothing was saved", await player.GetStringAsync("/me"));
        Assert.False(f.Live.Read(t => t.State.Data.WorkerColors.ContainsKey("zzoega")));
    }

    [Fact]
    public async Task The_tracker_of_the_admins_own_player_logs_in_as_the_player_only()
    {
        using var f = new ServerFactory();
        var player = await f.Player(f.AddKey(ServerFactory.AdminName));   // the admin plays too (Frank is zzoega)
        Assert.Equal(HttpStatusCode.OK, (await player.GetAsync("/me")).StatusCode);
        foreach (var url in new[] { "/admin", "/admin/keys", "/account" })
        {
            var r = await player.GetAsync(url);   // checked again on every request in the tests: the tracker's login stays a player's
            Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
            Assert.StartsWith("/login/denied", r.Headers.Location!.PathOf());
        }
        var admin = await f.Admin();   // the password still opens everything
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/admin")).StatusCode);
    }

    [Fact]
    public async Task The_admin_changes_his_password()
    {
        using var f = new ServerFactory();
        var admin = await f.Admin();
        const string NewPassword = "another password 456";
        var r = await ServerFactory.Send(admin, "/account", "/account/password", ("Password.Current", ServerFactory.AdminPassword), ("Password.New", NewPassword), ("Password.Again", NewPassword));
        Assert.Contains("The password is changed.", await r.Content.ReadAsStringAsync());

        var old = await ServerFactory.Send(f.Browser(), "/login", "/login", ("UserName", ServerFactory.AdminName), ("Password", ServerFactory.AdminPassword));
        Assert.Contains("Wrong name or password.", await old.Content.ReadAsStringAsync());
        var now = await ServerFactory.Send(f.Browser(), "/login", "/login", ("UserName", ServerFactory.AdminName), ("Password", NewPassword));
        Assert.Equal(HttpStatusCode.Redirect, now.StatusCode);
    }

    [Fact]
    public async Task The_admin_takes_the_name_of_his_own_player()
    {
        using var f = new ServerFactory();
        var key = f.AddKey("zzoega");
        await f.Player(key);   // zzoega's tracker made his login before

        var admin = await f.Admin();
        var r = await ServerFactory.Send(admin, "/account", "/account/name", ("Rename.NewName", "zzoega"));
        Assert.Contains("Your name is zzoega now", await r.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/admin")).StatusCode);   // still logged in, with the new name

        var old = await ServerFactory.Send(f.Browser(), "/login", "/login", ("UserName", ServerFactory.AdminName), ("Password", ServerFactory.AdminPassword));
        Assert.Contains("Wrong name or password.", await old.Content.ReadAsStringAsync());
        var now = await ServerFactory.Send(f.Browser(), "/login", "/login", ("UserName", "zzoega"), ("Password", ServerFactory.AdminPassword));
        Assert.Equal(HttpStatusCode.Redirect, now.StatusCode);

        // his tracker logs in to the same login, as the player only
        var player = await f.Player(key);
        Assert.Contains("ZZOEGA", await player.GetStringAsync("/me"));
        Assert.StartsWith("/login/denied", (await player.GetAsync("/admin")).Headers.Location!.PathOf());
    }

    [Fact]
    public async Task A_colour_too_close_to_another_members_is_not_taken()
    {
        using var f = new ServerFactory();
        var player = await f.Player(f.AddKey("zzatvo"));
        var orange = ViewPalette.Hex(ViewPalette.WorkerColor("zzoega", null, 0));   // zzoega's colour
        await ServerFactory.Send(player, "/me", "/me", ("Location", ""), ("Color", "#ff8c1e"), ("Ship", ""), ("Drive", ""));   // almost his orange
        Assert.Contains("too close to zzoega", WebUtility.HtmlDecode(await player.GetStringAsync("/me")));
        Assert.False(f.Live.Read(t => t.State.Data.WorkerColors.ContainsKey("zzatvo")));
        Assert.NotEqual("#ff8c1e", orange);

        await ServerFactory.Send(player, "/me", "/me", ("Location", ""), ("Color", "#3c46ff"), ("Ship", ""), ("Drive", ""));   // a blue
        Assert.Contains("Saved: colour.", await player.GetStringAsync("/me"));
    }

    [Fact]
    public void How_different_colours_look()
    {
        Assert.True(ViewPalette.Difference(ViewPalette.Rgb(255, 150, 40), ViewPalette.Rgb(255, 120, 0)) < ViewPalette.TooClose);   // two oranges
        Assert.True(ViewPalette.Difference(ViewPalette.Rgb(255, 0, 0), ViewPalette.Rgb(200, 0, 0)) < ViewPalette.TooClose);       // red and dark red
        Assert.True(ViewPalette.Difference(ViewPalette.Rgb(255, 150, 40), ViewPalette.Rgb(60, 205, 95)) > ViewPalette.TooClose);   // zzoega and zzatvo
        Assert.Equal(0, ViewPalette.Difference(0x123456, 0x123456), 6);
    }

    [Fact]
    public async Task A_forgotten_password_is_set_again_from_the_settings()
    {
        using var f = new ServerFactory();
        var admin = await f.Admin();
        await ServerFactory.Send(admin, "/account", "/account/password", ("Password.Current", ServerFactory.AdminPassword), ("Password.New", "forgotten password 1"), ("Password.Again", "forgotten password 1"));
        for (var i = 0; i < 5; i++) await ServerFactory.Send(f.Browser(), "/login", "/login", ("UserName", ServerFactory.AdminName), ("Password", "a wrong guess " + i));
        var locked = await ServerFactory.Send(f.Browser(), "/login", "/login", ("UserName", ServerFactory.AdminName), ("Password", "forgotten password 1"));
        Assert.Contains("Too many wrong passwords", await locked.Content.ReadAsStringAsync());

        // the server starts again with ResetPassword on
        using (var scope = f.Services.CreateScope())
        {
            await AccountSetup.RunAsync(scope.ServiceProvider,
                new AdminSettings { UserName = ServerFactory.AdminName, Password = "the password again", ResetPassword = true },
                scope.ServiceProvider.GetRequiredService<ILogger<LoginTests>>());
        }
        var now = await ServerFactory.Send(f.Browser(), "/login", "/login", ("UserName", ServerFactory.AdminName), ("Password", "the password again"));
        Assert.Equal(HttpStatusCode.Redirect, now.StatusCode);
    }

    [Fact]
    public void A_login_link_works_once_and_not_after_two_minutes()
    {
        var links = new LoginLinks();
        var now = new DateTime(2026, 10, 27, 20, 0, 0, DateTimeKind.Utc);
        var token = links.Make("zzoega", now);
        Assert.Equal("zzoega", links.Use(token, now.AddSeconds(30)));
        Assert.Null(links.Use(token, now.AddSeconds(31)));
        var late = links.Make("zzoega", now);
        Assert.Null(links.Use(late, now + LoginLinks.Valid + TimeSpan.FromSeconds(1)));
        Assert.Null(links.Use("made up", now));
        Assert.NotEqual(links.Make("zzoega", now), links.Make("zzoega", now));
    }
}

static class UriPaths
{
    /// <summary>The path (and query) of a redirect, whether the server sent it relative or with the host.</summary>
    public static string PathOf(this Uri u) => u.IsAbsoluteUri ? u.PathAndQuery : u.OriginalString;
}
