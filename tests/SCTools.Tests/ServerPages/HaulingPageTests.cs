using System.Net;

namespace SCTools.Tests.ServerPages;

/// <summary>
/// The Hauling Planner's page: an ordinary page of the site (the layout with the bar, hauling.css and hauling.js from wwwroot, the map engine), every tab at
/// its own address, and the page saying who is looking.
/// </summary>
public class HaulingPageTests
{
    [Fact]
    public async Task The_page_is_a_page_of_the_site_with_its_tabs_and_says_who_is_looking()
    {
        using var f = new ServerFactory();
        var visitor = f.Browser();
        var page = await visitor.GetStringAsync("/hauling");
        Assert.Contains("<header class=\"topbar\">", page);   // the site's bar, from the layout
        Assert.Contains("<footer class=\"sitefoot\">", page);
        Assert.Contains("href=\"/site.css?v=", page);
        Assert.Contains("href=\"/hauling.css?v=", page);
        Assert.Contains("src=\"/systemmap.js?v=", page);   // the map engine, then the page's own script
        Assert.True(page.IndexOf("systemmap.js?v=", StringComparison.Ordinal) < page.IndexOf("src=\"/hauling.js?v=", StringComparison.Ordinal));
        Assert.Contains("<div class=\"hauling\" data-me=\"\">", page);   // a watcher: nobody
        Assert.Contains("<button id=\"tabS\" class=\"on\">Session</button>", page);
        Assert.Contains("<div class=\"scroll\" id=\"party\" aria-label=\"Party\"></div>", page);
        Assert.Contains("<select id=\"partyPick\" class=\"hidden\"", page);
        Assert.DoesNotContain("<style>", page);   // nothing inline any more
        Assert.Equal(page, await visitor.GetStringAsync("/hauling/planner"));   // a tab's address: the same page, the script shows the tab
        Assert.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync("/hauling/nothing")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync("/index.html")).StatusCode);   // the old address of the inlined page

        var admin = await f.Admin();
        Assert.Contains("<div class=\"hauling\" data-me=\"admin\" data-admin=\"1\">", await admin.GetStringAsync("/hauling"));   // the admin picks whose session
        var player = await f.Player(f.AddKey("zzoega"));
        Assert.Contains("<div class=\"hauling\" data-me=\"zzoega\">", await player.GetStringAsync("/hauling"));
    }

    [Fact]
    public async Task The_script_the_style_and_the_engine_are_files_of_the_site()
    {
        using var f = new ServerFactory();
        var visitor = f.Browser();
        var script = await visitor.GetStringAsync("/hauling.js");
        Assert.Contains("const TABS = { S: 'session', C: 'contracts', P: 'planner', D: 'debts', H: 'history' };", script);   // /hauling/planner
        Assert.Contains("'/hauling/' + TABS[which]", script);
        Assert.Contains("function renderPlanner", script);
        Assert.Contains("function paymentTicks", script);
        Assert.Contains("PAGE.dataset.me", script);   // who is looking, from the page
        Assert.Contains("getElementById('sideS').classList.toggle('hidden', which !== 'S')", script);
        var style = await visitor.GetStringAsync("/hauling.css");
        Assert.Contains(".hauling .planworker", style);   // every rule under .hauling: nothing reaches the other pages
        Assert.Contains(".hauling #party th:nth-child(2) { width:46%; }", style);
        Assert.DoesNotContain("\nbody", style);
        var engine = await visitor.GetStringAsync("/systemmap.js");
        Assert.StartsWith("var SystemMap = (function ()", engine.Split('\n').First(l => !l.StartsWith("//")));
        Assert.Equal("image/x-icon", (await visitor.GetAsync("/favicon.ico")).Content.Headers.ContentType?.MediaType);
    }
}
