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
        var css = await visitor.GetStringAsync("/hauling.css");
        Assert.Contains(".hauling .side h2 { margin-top:0; }", css);   // the Session table level with the Stops table, which has no margin over it either (31 Oct 2026)
        Assert.Contains(".hauling .kv td:first-child { font-weight:600; width:50%; background:var(--header); color:#fff; }", css);   // the Session's row titles in the tool's colour
        Assert.Contains(".hauling table th.idx, .hauling table td.idx { width:36px !important;", await visitor.GetStringAsync("/hauling.css"));   // one width for every # column (31 Oct 2026)
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
        var contracts = "<h2>Completed Contracts</h2><p class=\"note\" id=\"rewardsParty\"></p><div class=\"scroll\" id=\"rewards\"></div>";   // the completed contracts under the Session tab's stops, over the whole width (31 Oct 2026)
        Assert.True(page.IndexOf("id=\"belowS\"", StringComparison.Ordinal) < page.IndexOf(contracts, StringComparison.Ordinal));
        Assert.True(page.IndexOf(contracts, StringComparison.Ordinal) < page.IndexOf("<h2>Cargo runs</h2>", StringComparison.Ordinal));
        Assert.DoesNotContain("<h1>", page);   // no title of its own: the bar says where you are
        // the live dot in the top bar left of Tracker, as on every page (live.js); the time the data was made went (31 Oct 2026)
        Assert.Contains("<span class=\"live\" id=\"live\"></span><a class=\"dl\" href=\"/tracker\">", page);
        Assert.DoesNotContain("id=\"updated\"", page);
        // the team status line at the right end of the tabs, the session's name gone from there (the Session tab says it; 31 Oct 2026)
        Assert.Contains("<span class=\"ticker\" data-ticker></span></nav>", page);
        Assert.DoesNotContain("id=\"session\"", page);
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
        // Stops (Frank, 31 Oct 2026): the two places as wide as each other, Value as wide as 10,000,000 needs, Boxes takes the rest; Finished stops the same way
        Assert.Contains("const STOP_WIDTHS = { 'Pickup Location': '21%', 'Drop-off Location': '21%', 'Batches': '80px', 'Delivered': '80px', 'SCU': '110px', 'Value': '90px' };", script);
        Assert.Contains("stopColumns('pickups'); stopColumns('finished');", script);
        Assert.DoesNotContain("fix('pickups'", script);
        Assert.Contains("const TABS = { S: 'session', P: 'planner', D: 'debts', H: 'history', L: 'leaderboard' };", script);   // /hauling/planner
        Assert.Contains("function systemIn(d, id)", script);   // the map's data has every system (stage 2)
        Assert.Contains("function legEnds(x, y)", script);   // a leg through a gateway is drawn to the gateway on each system's map
        Assert.Contains("map.systems(all.map(function (s) {", script);   // the systems' buttons in the map's corner, the engine's, for every system as on the other maps (31 Oct 2026)
        Assert.Contains("pm.buttons();", script);   // the counts follow the map's data
        Assert.DoesNotContain("systems.length > 1 ?", script);   // not only when the routes are in more than one any more
        Assert.DoesNotContain("'sub systems'", script);   // no row of its own over the map any more
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
        Assert.Contains("players.className = 'planplayers'; players.textContent = 'Players';", script);   // the route cards under a Players heading (31 Oct 2026)
        Assert.Contains("none.textContent = ME ? 'Nobody of your party is playing now.' : 'Log in to see yourself and your party here.';", script);   // and with no card, the heading and that line
        Assert.Contains("if (h.text === '#') th.className = 'idx';", script);   // every # column has one width (31 Oct 2026)
        Assert.Contains("if (g.headers[i].text === '#') td.className = 'idx';", script);
        Assert.Contains("function sinceText", script);   // the map's line in the route card's heading, not under it (30 Oct 2026)
        Assert.Contains("if (pick && ownCard && (w.name || '').toLowerCase() === ME) h.appendChild(pick);", script);   // the choice in his own route card (30 Oct 2026)
        Assert.Contains("function renderPlanner", script);
        Assert.Contains("function paymentTicks", script);
        Assert.Contains("PAGE.dataset.me", script);   // who is looking, from the page
        Assert.Contains("getElementById('sideS').classList.toggle('hidden', which !== 'S')", script);
        var style = await visitor.GetStringAsync("/hauling.css");
        Assert.Contains(".hauling .planworker", style);   // every rule under .hauling: nothing reaches the other pages
        Assert.DoesNotContain("\nbody", style);
        // the maps of the Route Planner, the Companion and the Gauntlet Tracker: half the page's width, the same at every width (Frank, 31 Oct 2026), 340 px at
        // 1200 px or less
        Assert.Contains(".hauling .planleft { flex:0 0 50%; max-width:100%; }", style);
        var siteCss = await visitor.GetStringAsync("/site.css");
        Assert.Contains(".maptool { --mapw:50%; display:grid; grid-template-columns:minmax(0, var(--mapw))", siteCss);
        Assert.Contains("@media (max-width:1200px) { .maptool { --mapw:340px; } }", siteCss);
        Assert.DoesNotContain("--mapw:520px", siteCss);
        var engine = await visitor.GetStringAsync("/systemmap.js");
        Assert.Contains("const W = 760, ROOM = 16,", engine);   // the outermost places near the edge, the middle bigger (31 Oct 2026; 46 before)
        Assert.StartsWith("var SystemMap = (function ()", engine.Split('\n').First(l => !l.StartsWith("//")));
        Assert.Equal("image/x-icon", (await visitor.GetAsync("/favicon.ico")).Content.Headers.ContentType?.MediaType);
    }
}
