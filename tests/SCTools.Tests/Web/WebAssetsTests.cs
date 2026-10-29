namespace SCTools.Tests.Web;

/// <summary>The live web page is put together from the files in wwwroot (embedded in SCTools.Web).</summary>
public class WebAssetsTests
{
    [Fact]
    public void The_page_is_one_html_document_with_its_style_and_script_inline()
    {
        var page = WebAssets.Page;
        Assert.StartsWith("<!doctype html>", page);
        Assert.Contains("<style>", page);
        Assert.Contains("</style>", page);
        Assert.Contains("<script>", page);
        Assert.Contains("</script>", page);
        Assert.DoesNotContain("href=\"app.css\"", page);   // the style is in the page, not a file of its own
        Assert.DoesNotContain("src=\"app.js\"", page);
    }

    [Fact]
    public void The_page_has_the_style_and_the_script_of_the_trip_planner()
    {
        var page = WebAssets.Page;
        Assert.Contains(".planworker", page);   // app.css
        Assert.Contains("body { margin:0 auto; max-width:1900px;", page);   // no wider than that on an ultrawide screen
        Assert.Contains("function renderPlanner", page);   // app.js
    }

    [Fact]
    public void Every_tab_has_its_own_address_on_the_site_and_a_hash_on_the_programs_own_page()
    {
        var page = WebAssets.Page;
        Assert.Contains("const TABS = { S: 'session', C: 'contracts', P: 'planner', D: 'debts', H: 'history' };", page);   // /hauling/planner, or #planner
        Assert.Contains("<div id=\"viewC\"", page);   // the Contracts tab, with the contracts table in it
        Assert.Contains("<div class=\"scroll\" id=\"rewards\">", page[page.IndexOf("<div id=\"viewC\"", StringComparison.Ordinal)..]);
        Assert.Contains("'/hauling/' + TABS[which]", page);
    }

    [Fact]
    public void The_party_table_is_beside_the_tabs_and_there_is_no_party_picker()
    {
        var page = WebAssets.Page;
        var top = page.IndexOf("<div class=\"top\">", StringComparison.Ordinal);
        var nav = page.IndexOf("<div class=\"nav\">", StringComparison.Ordinal);
        var side = page.IndexOf("<div class=\"side\" id=\"sideS\">", StringComparison.Ordinal);
        var below = page.IndexOf("<div id=\"belowS\">", StringComparison.Ordinal);
        Assert.True(top > 0 && top < nav && nav < side && side < below, "the tabs and the Party table in the same row, the leaderboard and the runs under them");
        Assert.Contains("<div class=\"scroll\" id=\"party\" aria-label=\"Party\"></div>", page[side..below]);
        Assert.Contains("<h2>Session leaderboard</h2>", page[below..]);
        Assert.DoesNotContain("partyPick", page);   // no picker: the viewer's own party by itself
        Assert.Contains("<p class=\"note hidden\" id=\"partyNote\"></p>", page);
        Assert.Contains("#party th:nth-child(2) { width:46%; }", page);   // the Ship column has the room
        Assert.Contains("getElementById('sideS').classList.toggle('hidden', which !== 'S')", page);
    }
}
