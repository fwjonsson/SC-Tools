using System.Net;
using HaulingTracker.Updates;

namespace HaulingTracker.Tests.ServerPages;

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
        Assert.Contains("https://github.com/fwjonsson/SCHaulingTracker/releases/download/latest/SCToolsTracker.zip", html);
        Assert.Contains("https://sct.fwjonsson.dev", html);   // the server it has already
        Assert.Contains("SCToolsTracker.exe", html);
    }

    [Fact]
    public void A_tracker_that_is_still_HaulingTrackerSender_moves_to_its_new_name_next_to_it()
    {
        var dir = Path.Combine("OneDrive", "SCTools");
        Assert.Equal(Path.Combine(dir, "SCToolsTracker.exe"), TrackerName.MovedPath(Path.Combine(dir, "HaulingTrackerSender.exe")));
        Assert.Equal(Path.Combine(dir, "SCToolsTracker.exe"), TrackerName.MovedPath(Path.Combine(dir, "haulingtrackersender.EXE")));
        Assert.Null(TrackerName.MovedPath(Path.Combine(dir, "SCToolsTracker.exe")));   // it has its name already
        Assert.Null(TrackerName.MovedPath(Path.Combine(dir, "MyTracker.exe")));        // a name of your own stays
    }
}
