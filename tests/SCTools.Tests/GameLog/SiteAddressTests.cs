namespace SCTools.Tests.GameLog;

/// <summary>A tracker with one of the site's old addresses saved moves to the new one by itself; any other address it keeps.</summary>
public class SiteAddressTests
{
    [Theory]
    [InlineData("https://haulingtracker.fwjonsson.dev")]
    [InlineData("https://haulingtracker.fwjonsson.dev/")]
    [InlineData(" HTTPS://HaulingTracker.fwjonsson.dev ")]
    [InlineData("http://haulingtracker.fwjonsson.dev/hauling")]
    [InlineData("haulingtracker.fwjonsson.dev")]
    [InlineData("https://sct.fwjonsson.dev")]
    [InlineData("https://sct.fwjonsson.dev/hauling/planner")]
    public void An_old_address_becomes_the_site(string saved) => Assert.Equal("https://sc-tools.fwjonsson.dev", SiteAddress.Moved(saved));

    [Theory]
    [InlineData("https://sc-tools.fwjonsson.dev")]
    [InlineData("http://localhost:8780")]
    [InlineData("https://haulingtracker.example.com")]
    [InlineData("")]
    public void Any_other_address_stays(string saved) => Assert.Equal(saved, SiteAddress.Moved(saved));
}
