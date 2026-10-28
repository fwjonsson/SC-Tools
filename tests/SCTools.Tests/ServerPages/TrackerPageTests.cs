using System.Net;
using SCTools.Server;
using SCTools.Updates;

namespace SCTools.Tests.ServerPages;

/// <summary>The SC Tools Tracker: its page on the site (download and steps, linked from the landing page), and its new name.</summary>
public class TrackerPageTests
{
    [Fact]
    public async Task The_landing_page_links_to_the_trackers_page_with_the_download_and_the_steps()
    {
        using var f = new ServerFactory();
        var c = f.Browser();
        Assert.Contains("href=\"/tracker\"", await c.GetStringAsync("/"));
        var page = await c.GetAsync("/tracker");   // for everybody
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var html = await page.Content.ReadAsStringAsync();
        Assert.Contains("https://github.com/fwjonsson/SC-Tools/releases/download/latest/SCToolsTracker.zip", html);
        Assert.Contains("https://sc-tools.fwjonsson.dev", html);   // the server it has already
        Assert.Contains("SCToolsTracker.exe", html);
    }

    [Fact]
    public async Task A_player_sees_which_version_his_tracker_runs_and_whether_it_is_the_newest()
    {
        using var f = new ServerFactory();   // the newest build is abcdef1...
        Assert.DoesNotContain("Your tracker", await f.Browser().GetStringAsync("/tracker"));   // only for somebody logged in
        var key = f.AddKey("zzoega");
        var player = await f.Player(key);   // the login link came from a tracker that does not say its version
        Assert.Contains("An older version (from before the trackers said their version)", WebUtility.HtmlDecode(await player.GetStringAsync("/tracker")));

        var tracker = f.Tracker(key);
        tracker.DefaultRequestHeaders.Add("X-Tracker-Version", "1234567");
        (await tracker.GetAsync("/api/me")).EnsureSuccessStatusCode();
        var page = WebUtility.HtmlDecode(await player.GetStringAsync("/tracker"));
        Assert.Contains("Version 1234567, not the newest (that is abcdef1), in touch just now.", page);
        Assert.Contains("Check for an update", page);

        var updated = f.Tracker(key);
        updated.DefaultRequestHeaders.Add("X-Tracker-Version", "abcdef1");
        (await updated.GetAsync("/api/me")).EnsureSuccessStatusCode();
        Assert.Contains("Version abcdef1, the newest, in touch just now.", WebUtility.HtmlDecode(await player.GetStringAsync("/tracker")));
        var admin = await f.Admin();
        Assert.Contains("Version abcdef1, the newest", WebUtility.HtmlDecode(await admin.GetStringAsync("/admin/keys")));   // the admin sees everybody's

        var own = f.Tracker(key);
        own.DefaultRequestHeaders.Add("X-Tracker-Version", "own build");
        (await own.GetAsync("/api/me")).EnsureSuccessStatusCode();
        Assert.Contains("A build of its own", WebUtility.HtmlDecode(await player.GetStringAsync("/tracker")));
    }

    [Fact]
    public async Task GitHub_is_asked_at_most_every_ten_minutes_and_a_failed_answer_keeps_the_last_one()
    {
        var asked = 0;
        string? answer = "abc1234";
        var versions = new TrackerVersions(_ => { asked++; return answer == null ? throw new HttpRequestException("down") : Task.FromResult<string?>(answer); });
        var t = new DateTime(2026, 10, 28, 5, 0, 0, DateTimeKind.Utc);
        Assert.Equal("abc1234", await versions.NewestAsync(t));
        Assert.Equal("abc1234", await versions.NewestAsync(t.AddMinutes(5)));
        Assert.Equal(1, asked);
        answer = null;
        Assert.Equal("abc1234", await versions.NewestAsync(t.AddMinutes(11)));   // GitHub down: the last answer
        Assert.Equal(2, asked);
        Assert.Equal((true, false), (TrackerVersions.IsNewest("abc1234", "abc1234" + new string('0', 33)), TrackerVersions.IsNewest("1111111", "abc1234")));
        Assert.Null(TrackerVersions.IsNewest(TrackerVersions.OwnBuild, "abc1234"));
    }

    [Fact]
    public void A_program_still_under_its_old_name_moves_to_its_new_name_next_to_it()
    {
        var dir = Path.Combine("OneDrive", "SCTools");
        Assert.Equal(Path.Combine(dir, "SCToolsTracker.exe"), AppName.Tracker.MovedPath(Path.Combine(dir, "HaulingTrackerSender.exe")));
        Assert.Equal(Path.Combine(dir, "SCToolsTracker.exe"), AppName.Tracker.MovedPath(Path.Combine(dir, "haulingtrackersender.EXE")));
        Assert.Null(AppName.Tracker.MovedPath(Path.Combine(dir, "SCToolsTracker.exe")));   // it has its name already
        Assert.Null(AppName.Tracker.MovedPath(Path.Combine(dir, "MyTracker.exe")));        // a name of your own stays
        Assert.Equal(Path.Combine(dir, "HaulingPlanner.exe"), AppName.Planner.MovedPath(Path.Combine(dir, "HaulingTracker.exe")));   // the Windows program
        Assert.Null(AppName.Planner.MovedPath(Path.Combine(dir, "HaulingTrackerSender.exe")));   // not the tracker
        Assert.Equal(("SCToolsTracker.zip", "HaulingPlanner.zip"), (AppName.Tracker.Zip, AppName.Planner.Zip));
    }
}
