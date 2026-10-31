using System.Net;
using System.Text.Json;
using SCTools.Tests.Support;

namespace SCTools.Tests.ServerPages;

/// <summary>
/// What every page has, not one tool (SiteStatusController, Frank 31 Oct 2026): the live text in the top bar left of Tracker (live.js asks /live when the page
/// polls nothing of its own), and the team status line at the right end of the gameplay tools' row of tabs (ticker.js, /status.json): the Hauling Planner, the
/// Companion, the Gauntlet Tracker and the Map, not the Session tool or the Mission Library.
/// </summary>
public class SiteStatusPageTests
{
    [Fact]
    public async Task Every_page_has_the_live_text_and_the_site_answers_it()
    {
        using var f = new ServerFactory();
        var visitor = f.Browser();
        var res = await visitor.GetAsync("/live");
        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);   // anybody, nothing in it
        var admin = await f.Admin();
        foreach (var page in new[] { "/", "/hauling", "/missions", "/session", "/gauntlet", "/companion", "/admin" })
        {
            var html = await admin.GetStringAsync(page);
            Assert.Contains("<span class=\"live\" id=\"live\"></span><a class=\"dl\" href=\"/tracker\">", html);   // left of Tracker
            Assert.Contains("live.js", html);
            Assert.DoesNotContain("id=\"updated\"", html);   // the time went (31 Oct 2026)
        }
    }

    [Fact]
    public async Task The_status_line_is_in_the_gameplay_tools_row_of_tabs_only()
    {
        using var f = new ServerFactory();
        var admin = await f.Admin();
        foreach (var page in new[] { "/hauling", "/companion", "/gauntlet", "/map" })
        {
            var html = await admin.GetStringAsync(page);
            Assert.Contains("<span class=\"ticker\" data-ticker></span>", html);
            Assert.Contains("ticker.js", html);
            Assert.True(html.IndexOf("data-ticker", StringComparison.Ordinal) < html.IndexOf("</nav>", html.IndexOf("class=\"subnav area\"", StringComparison.Ordinal), StringComparison.Ordinal), page + ": in the row of tabs");
        }
        foreach (var page in new[] { "/session", "/missions" })
            Assert.DoesNotContain("data-ticker", await admin.GetStringAsync(page));
        // the Companion's and the Map's systems have a box of their own in the row (their scripts fill it), beside the line
        foreach (var page in new[] { "/companion", "/map" })
            Assert.Contains("<div class=\"tabs\" id=\"systems\" role=\"tablist\" aria-label=\"Star system\"></div><span class=\"ticker\" data-ticker></span></nav>", await admin.GetStringAsync(page));
    }

    [Fact]
    public async Task The_status_line_has_a_site_address_and_says_nothing_to_a_visitor()
    {
        using var f = new ServerFactory();
        f.Upload(PlayedSession.Player, PlayedSession.Writer().Lines.ToList());
        var visitor = JsonDocument.Parse(await f.Browser().GetStringAsync("/status.json")).RootElement;
        Assert.Equal("", visitor.GetProperty("status").GetString());
        var admin = JsonDocument.Parse(await (await f.Admin()).GetStringAsync("/status.json")).RootElement;
        Assert.True(admin.TryGetProperty("key", out _));
        Assert.Equal(HttpStatusCode.NotFound, (await f.Browser().GetAsync("/hauling/status.json")).StatusCode);   // not the Hauling Planner's any more
    }
}
