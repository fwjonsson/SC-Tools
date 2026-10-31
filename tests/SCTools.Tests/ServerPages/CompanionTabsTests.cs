using System.Net;

namespace SCTools.Tests.ServerPages;

/// <summary>
/// The Companion's tabs (Frank, 31 Oct 2026): Overview (the map, the players, the objectives where the game session was, the contracts going on),
/// Session (the game session) and Contracts (the ones going on, and under them the ones ended this session). One page with an address per tab; the
/// address picks the tab shown, and companion.js switches between them without a reload.
/// </summary>
public class CompanionTabsTests
{
    static string Section(string page, string tab)
    {
        var at = page.IndexOf("data-tab=\"" + tab + "\"", page.IndexOf("<section", StringComparison.Ordinal), StringComparison.Ordinal);
        return page[at..page.IndexOf("</section>", at, StringComparison.Ordinal)];
    }

    [Fact]
    public async Task Every_tab_has_its_address_and_its_part_of_the_page()
    {
        using var f = new ServerFactory();
        var admin = await f.Admin();
        var page = await admin.GetStringAsync("/companion");

        // the row: the three tabs, Overview marked, the status line at its end
        Assert.Contains("<a href=\"/companion/overview\" data-tab=\"overview\" class=\"on\" aria-current=\"page\">Overview</a><a href=\"/companion/session\" data-tab=\"session\">Session</a><a href=\"/companion/contracts\" data-tab=\"contracts\">Contracts</a>", page);

        // Overview: the map, the players, the objectives (where the game session was), the contracts going on; not the session, not the completed ones
        var overview = Section(page, "overview");
        Assert.DoesNotContain(" hidden", overview[..overview.IndexOf('>')]);
        Assert.Contains("id=\"map\"", overview);
        Assert.Contains("<h2>Objectives</h2>", overview);
        Assert.Contains("id=\"missions\"", overview);
        Assert.DoesNotContain("id=\"session\"", overview);
        Assert.DoesNotContain("id=\"completed\"", overview);

        // Session: the game session; Contracts: the ones going on and the completed ones under them; both hidden on Overview
        var session = Section(page, "session");
        Assert.Contains(" hidden", session[..session.IndexOf('>')]);
        Assert.Contains("id=\"session\"", session);
        var contracts = Section(page, "contracts");
        Assert.True(contracts.IndexOf("id=\"missionsAll\"", StringComparison.Ordinal) < contracts.IndexOf("id=\"completed\"", StringComparison.Ordinal));

        // the address picks the tab shown
        page = await admin.GetStringAsync("/companion/contracts");
        Assert.Contains("data-tab=\"contracts\" class=\"on\"", page);
        Assert.DoesNotContain(" hidden", Section(page, "contracts")[..Section(page, "contracts").IndexOf('>')]);
        Assert.Contains(" hidden", Section(page, "overview")[..Section(page, "overview").IndexOf('>')]);
        Assert.Contains("data-tab=\"session\" class=\"on\"", await admin.GetStringAsync("/companion/session"));
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/companion/elsewhere")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/companion/session.json")).StatusCode);   // the feeds keep their addresses
    }

    [Fact]
    public async Task The_session_tool_is_session_history_at_its_new_address()
    {
        using var f = new ServerFactory();
        var admin = await f.Admin();
        var page = await admin.GetStringAsync("/history");
        Assert.Contains("<title>Session History - SC Tools</title>", page);
        Assert.Contains(">Session History</a>", page);   // in the top bar
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/session")).StatusCode);   // the old address went (Frank: no bookmarks to keep)
    }
}
