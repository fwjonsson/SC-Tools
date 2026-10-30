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
        Assert.Contains(".hauling .side h2 { margin-top:22px; }", await visitor.GetStringAsync("/hauling.css"));   // the Session table level with the Stops table (30 Oct 2026)
        Assert.Contains("src=\"/systemmap.js?v=", page);   // the map engine, then the page's own script
        Assert.True(page.IndexOf("systemmap.js?v=", StringComparison.Ordinal) < page.IndexOf("src=\"/hauling.js?v=", StringComparison.Ordinal));
        Assert.Contains("<div class=\"hauling\" data-me=\"\">", page);   // a watcher: nobody
        // the tabs: a row under the bar in the tool's colour, like the admin's pages (30 Oct 2026), every tab a link to its address; the Trip Planner first (29 Oct)
        Assert.Contains("<nav class=\"subnav area\" style=\"--tool:#4682ff\"><a href=\"/hauling/planner\" id=\"tabP\" class=\"on\">Route Planner</a><a href=\"/hauling/session\" id=\"tabS\">Session</a>"
            + "<a href=\"/hauling/debts\" id=\"tabD\">Debts</a><a href=\"/hauling/history\" id=\"tabH\">History</a><a href=\"/hauling/leaderboard\" id=\"tabL\">Leaderboard</a>", page);   // no Contracts tab, a Leaderboard tab (30 Oct 2026)
        Assert.Contains("<div id=\"viewL\" class=\"hidden\">", page);
        Assert.DoesNotContain("id=\"tabN\"", page);   // the Route Planner (new) tab of 30 Oct 2026 is the Route Planner now
        Assert.Equal(System.Net.HttpStatusCode.NotFound, (await visitor.GetAsync("/hauling/planner-new")).StatusCode);   // the address of the Route Planner (new) of 30 Oct 2026 is gone (Frank)
        Assert.Contains("<h2>Session leaderboard</h2><div class=\"scroll\" id=\"leaders\"></div>", page);
        Assert.Contains("<h2>All-time leaderboard</h2><div class=\"scroll\" id=\"allTime\"></div>", page);
        Assert.DoesNotContain("id=\"viewC\"", page);
        Assert.Contains("<h2>Active Contracts</h2><div id=\"active\">", page);   // the map's missions going on, above the contracts (30 Oct 2026)
        var contracts = "<h2>Contracts</h2><p class=\"note\" id=\"rewardsParty\"></p><div class=\"scroll\" id=\"rewards\"></div>";   // the contracts under the Session tab's stops, over the whole width
        Assert.True(page.IndexOf("id=\"belowS\"", StringComparison.Ordinal) < page.IndexOf(contracts, StringComparison.Ordinal));
        Assert.True(page.IndexOf(contracts, StringComparison.Ordinal) < page.IndexOf("<h2>Cargo runs</h2>", StringComparison.Ordinal));
        Assert.DoesNotContain("<h1>", page);   // no title of its own: the bar says where you are
        // the live dot and the time the data was made: in the top bar left of Tracker, not on every tab (30 Oct 2026); hauling.js fills them
        Assert.Contains("<span class=\"live\" id=\"live\"></span><span class=\"updated\" id=\"updated\"></span><a class=\"dl\" href=\"/tracker\">", page);
        Assert.Contains("<span id=\"session\" class=\"session\"></span></nav>", page);   // the session shown: at the right end of the tabs (30 Oct 2026)
        Assert.DoesNotContain("id=\"partyNote\"", page);   // no line saying whose session on the Session tab any more (30 Oct 2026)
        Assert.Contains("<div id=\"viewP\" class=\"hidden\">", page);   // every view starts hidden: the script shows the first tab there is (30 Oct 2026)
        Assert.Contains("<div id=\"viewS\" class=\"hidden\">", page);
        Assert.DoesNotContain("id=\"party\"", page);   // no Party table of its own: the Party button in the top bar shows it (29 Oct 2026)
        Assert.DoesNotContain("<style>", page);   // nothing inline any more
        Assert.Equal(page, await visitor.GetStringAsync("/hauling/planner"));   // a tab's address: the same page, the script shows the tab
        Assert.Equal(page, await visitor.GetStringAsync("/hauling/leaderboard"));
        Assert.Equal(page, await visitor.GetStringAsync("/hauling/contracts"));   // an old link: the script shows the Session tab
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
        Assert.Contains("const TABS = { S: 'session', P: 'planner', D: 'debts', H: 'history', L: 'leaderboard' };", script);   // /hauling/planner
        Assert.Contains("function systemIn(d, id)", script);   // the map's data has every system (stage 2)
        Assert.Contains("function legEnds(x, y)", script);   // a leg through a gateway is drawn to the gateway on each system's map
        Assert.Contains("row.className = 'sub systems';", script);   // the row of systems over the map, when the routes are in more than one
        Assert.Contains("if (name === 'contracts') name = 'session';", script);   // the old Contracts tab's address opens the Session tab (30 Oct 2026)
        Assert.Contains("function leaderWidths", script);   // the Session tables line up without the leaderboard on the same tab
        Assert.Contains("function missionsTable", script);   // the Active Contracts of the Session tab, from the map's data (30 Oct 2026)
        Assert.Contains("function submitButton", script);   // the Submit button of a Current Route (30 Oct 2026)
        Assert.Contains("(shownTab !== 'P' && shownTab !== 'S')", script);   // the map is polled on the Route Planner and the Session tab
        Assert.True(script.IndexOf("section(el, 'h2', 'Saved sessions', d.saved);", StringComparison.Ordinal) < script.IndexOf("best.textContent = d.best", StringComparison.Ordinal));   // the best session line under the saved sessions (30 Oct 2026)
        Assert.Contains("'/hauling/' + TABS[which]", script);
        Assert.Contains("|| tabOf(location.hash) || FIRST);", script);   // /hauling opens the first Route Planner tab there is (the admin may hide one, 30 Oct 2026)
        Assert.Contains("const HAVE = Object.keys(TABS).filter(function (k) { return !!document.getElementById('tab' + k); });", script);
        Assert.Contains("function lostPicks", script);   // the admin's Lost Cargo pick in the Cargo runs
        Assert.Contains("function renderPlanner(tab) { keepScroll(document.getElementById('planBody' + tab), function () { buildPlanner(tab); }); }", script);   // the page does not jump up when the planner is drawn anew (30 Oct 2026)
        Assert.Contains("function sinceText", script);   // the map's line in the route card's heading, not under it (30 Oct 2026)
        Assert.Contains("if (pick && ownCard && (w.name || '').toLowerCase() === ME) h.appendChild(pick);", script);   // the choice in his own route card (30 Oct 2026)
        Assert.Contains("function renderPlanner", script);
        Assert.Contains("function paymentTicks", script);
        Assert.Contains("PAGE.dataset.me", script);   // who is looking, from the page
        Assert.Contains("getElementById('sideS').classList.toggle('hidden', which !== 'S')", script);
        var style = await visitor.GetStringAsync("/hauling.css");
        Assert.Contains(".hauling .planworker", style);   // every rule under .hauling: nothing reaches the other pages
        Assert.DoesNotContain("\nbody", style);
        Assert.Contains(".hauling .planleft { flex:0 0 820px; max-width:100%; }", style);   // the Trip Planner's map is the Map tool's size
        Assert.Contains(".maptool { display:grid; grid-template-columns:minmax(0, 820px)", await visitor.GetStringAsync("/site.css"));   // the Map tool's map: 820 px too
        var engine = await visitor.GetStringAsync("/systemmap.js");
        Assert.StartsWith("var SystemMap = (function ()", engine.Split('\n').First(l => !l.StartsWith("//")));
        Assert.Equal("image/x-icon", (await visitor.GetAsync("/favicon.ico")).Content.Headers.ContentType?.MediaType);
    }
}
