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
        Assert.Contains("function renderPlanner", page);   // app.js
    }

    [Fact]
    public void Every_tab_has_its_own_address_on_the_site_and_a_hash_on_the_programs_own_page()
    {
        var page = WebAssets.Page;
        Assert.Contains("const TABS = { S: 'session', P: 'planner', D: 'debts', H: 'history' };", page);   // /hauling/planner, or #planner
        Assert.Contains("'/hauling/' + TABS[which]", page);
    }
}
